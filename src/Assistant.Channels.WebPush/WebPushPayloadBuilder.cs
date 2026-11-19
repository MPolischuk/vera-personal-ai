using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assistant.Abstractions.Channels;
using Assistant.Abstractions.Notifications;

namespace Assistant.Channels.WebPush;

/// <summary>
/// Declarative Web Push document. The body is at most 240 characters and the JSON is at most
/// <see cref="MaxBytes"/> bytes before encryption.
/// </summary>
internal static class WebPushPayloadBuilder
{
    public const int MaxBytes = 3_000;

    public const int RetryBytes = 1_500;

    public const int MaxBodyCharacters = 240;

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Build(
        OutboundMessage message,
        NotificationKind kind,
        Guid? conversationId,
        string? publicBaseUrl,
        int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1);

        var kindName = NotificationKindNames.ToWire(kind);
        var tagKey = message.CollapseKey
            ?? message.NotificationId?.ToString("N")
            ?? message.MessageId.ToString("N");
        var document = new WebPushDocument(
            8030,
            new WebPushNotification(
                string.IsNullOrWhiteSpace(message.Title) ? "Aviso" : message.Title.Trim(),
                Trim(message.Text, MaxBodyCharacters),
                Navigate(publicBaseUrl, message.NotificationId, conversationId),
                $"{kindName}:{tagKey}",
                Silent: false,
                "es-AR",
                "ltr"),
            new WebPushVera(
                message.NotificationId,
                message.MessageId == Guid.Empty ? null : message.MessageId,
                kindName,
                MapActions(message.Actions)));

        var json = Serialize(document);
        if (Encoding.UTF8.GetByteCount(json) <= maxBytes)
        {
            return json;
        }

        var fitted = Fit(document, maxBytes);
        return fitted ?? Serialize(document with
        {
            Notification = document.Notification with { Title = "Aviso", Body = "" },
            Vera = document.Vera with { Actions = null },
        });
    }

    public static string? TopicSource(OutboundMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!string.IsNullOrEmpty(message.CollapseKey))
        {
            return message.CollapseKey;
        }

        if (message.NotificationId is Guid notificationId)
        {
            return notificationId.ToString("N");
        }

        return message.MessageId == Guid.Empty ? null : message.MessageId.ToString("N");
    }

    public static string Trim(string? text, int maxCharacters)
    {
        var value = text ?? "";
        if (maxCharacters <= 0 || value.Length == 0)
        {
            return "";
        }

        if (value.Length <= maxCharacters)
        {
            return value;
        }

        if (maxCharacters == 1)
        {
            return "…";
        }

        return string.Concat(value.AsSpan(0, maxCharacters - 1), "…");
    }

    private static string? Fit(WebPushDocument document, int maxBytes)
    {
        var withoutActions = document with { Vera = document.Vera with { Actions = null } };
        var body = document.Notification.Body;
        var best = FitBody(withoutActions, body, maxBytes);
        if (best is not null)
        {
            return best;
        }

        var title = document.Notification.Title;
        for (var length = Math.Min(title.Length, 80); length >= 0; length--)
        {
            var candidate = withoutActions with
            {
                Notification = withoutActions.Notification with
                {
                    Title = length == 0 ? "Aviso" : Trim(title, length),
                    Body = "",
                },
            };
            var json = Serialize(candidate);
            if (Encoding.UTF8.GetByteCount(json) <= maxBytes)
            {
                return json;
            }
        }

        return null;
    }

    private static string? FitBody(WebPushDocument document, string body, int maxBytes)
    {
        var low = 0;
        var high = body.Length;
        string? best = null;
        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var candidate = document with
            {
                Notification = document.Notification with { Body = Trim(body, mid) },
            };
            var json = Serialize(candidate);
            if (Encoding.UTF8.GetByteCount(json) <= maxBytes)
            {
                best = json;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return best;
    }

    private static WebPushAction[]? MapActions(IReadOnlyList<OutboundAction>? actions)
    {
        if (actions is null || actions.Count == 0)
        {
            return null;
        }

        var mapped = new WebPushAction[actions.Count];
        for (var i = 0; i < actions.Count; i++)
        {
            mapped[i] = new WebPushAction(actions[i].ActionId, actions[i].Label);
        }

        return mapped;
    }

    private static string Navigate(string? publicBaseUrl, Guid? notificationId, Guid? conversationId)
    {
        var origin = string.IsNullOrWhiteSpace(publicBaseUrl)
            ? ""
            : publicBaseUrl.Trim().TrimEnd('/');
        if (notificationId is not Guid notification)
        {
            return origin.Length == 0 ? "/" : origin + "/";
        }

        var path = "/?notification=" + notification.ToString("D");
        if (conversationId is Guid conversation)
        {
            path += "&conversation=" + conversation.ToString("D");
        }

        return origin + path;
    }

    private static string Serialize(WebPushDocument document)
        => JsonSerializer.Serialize(document, Json);

    private sealed record WebPushDocument(
        [property: JsonPropertyName("web_push")] int WebPush,
        [property: JsonPropertyName("notification")] WebPushNotification Notification,
        [property: JsonPropertyName("vera")] WebPushVera Vera);

    private sealed record WebPushNotification(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("body")] string Body,
        [property: JsonPropertyName("navigate")] string Navigate,
        [property: JsonPropertyName("tag")] string Tag,
        [property: JsonPropertyName("silent")] bool Silent,
        [property: JsonPropertyName("lang")] string Lang,
        [property: JsonPropertyName("dir")] string Dir);

    private sealed record WebPushVera(
        [property: JsonPropertyName("notificationId")] Guid? NotificationId,
        [property: JsonPropertyName("deliveryId")] Guid? DeliveryId,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("actions")] IReadOnlyList<WebPushAction>? Actions);

    private sealed record WebPushAction(
        [property: JsonPropertyName("action")] string Action,
        [property: JsonPropertyName("title")] string Title);
}
