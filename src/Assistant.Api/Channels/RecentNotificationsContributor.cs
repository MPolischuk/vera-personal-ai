using System.Globalization;
using Assistant.Abstractions.Context;
using Assistant.Abstractions.Notifications;
using Assistant.Abstractions.Time;
using Assistant.Core.Tools;

namespace Assistant.Api.Channels;

/// <summary>
/// Last delivered notices so «listo», «posponelo» or «¿qué era?» have something to refer to.
/// Lower importance than conversation history, so the budget drops this fragment first.
/// Untrusted bodies are fenced. The text is not logged.
/// </summary>
internal sealed class RecentNotificationsContributor(
    INotificationStore notifications,
    IUserTimeZoneProvider timeZones) : IContextContributor
{
    public const int MaxItems = 3;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(30);

    public string Name => "recent.notifications";

    public int Priority => 110;

    public bool IsEnabled => true;

    public async ValueTask<ContextFragment?> ContributeAsync(ContextRequest request, CancellationToken ct)
    {
        var items = await notifications.ListRecentDeliveredAsync(
            request.UserId, request.Now - Window, MaxItems, ct);
        if (items.Count == 0)
        {
            return null;
        }

        var zone = await timeZones.GetTimeZoneAsync(request.UserId, ct);
        var lines = new List<string>(items.Count + 1)
        {
            "Avisos recientes que le mandaste (más nuevo primero):",
        };
        foreach (var item in items)
        {
            lines.Add(Format(item, zone));
        }

        return ContextFragment.Text(Name, ContextPlacement.AppendedMessage, string.Join('\n', lines));
    }

    private static string Format(DeliveredNotificationNotice item, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(item.DeliveredAt, zone);
        var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        var channel = ChannelLabel(item.Channel);
        var body = item.Trust == NotificationContentTrust.Untrusted
            ? ExternalContent.Wrap(item.Channel ?? "notification", "notification", channel, item.Body)
            : "«" + item.Body + "»";
        var response = string.IsNullOrEmpty(item.ActionTaken) ? "sin respuesta" : ActionLabel(item.ActionTaken);
        return FormattableString.Invariant($"- {time} por {channel} · {item.Title}: {body} ({response})");
    }

    private static string ChannelLabel(string? channel)
        => channel switch
        {
            "telegram" => "Telegram",
            "hub" => "la app",
            "webpush" => "el celular",
            "whatsapp" => "WhatsApp",
            null or "" => "un canal",
            _ => channel,
        };

    private static string ActionLabel(string action)
        => action switch
        {
            "done" => "listo",
            "snooze:10m" => "pospuesto",
            "opened" => "abierto",
            _ => action,
        };
}
