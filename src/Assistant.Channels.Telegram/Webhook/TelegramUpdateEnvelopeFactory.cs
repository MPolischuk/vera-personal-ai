using System.Globalization;
using System.Text.Json;
using Assistant.Abstractions.Channels;

namespace Assistant.Channels.Telegram.Webhook;

/// <summary>Minimal parse: <c>update_id</c>, chat id, chat type, and the update kind.</summary>
public static class TelegramUpdateEnvelopeFactory
{
    public static bool TryCreate(string rawJson, DateTimeOffset receivedAt, out ParsedTelegramUpdate update)
    {
        update = default;
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(rawJson);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("update_id", out var updateIdElement)
                || !updateIdElement.TryGetInt64(out var updateId))
            {
                return false;
            }

            if (!TryReadKind(document.RootElement, out var kind, out var peerId))
            {
                return false;
            }

            update = new ParsedTelegramUpdate(
                new InboundEnvelope(
                    ChannelId.Telegram,
                    updateId.ToString(CultureInfo.InvariantCulture),
                    peerId,
                    null,
                    receivedAt,
                    rawJson),
                kind);
            return true;
        }
    }

    private static bool TryReadKind(JsonElement root, out InboundUpdateKind kind, out string peerId)
    {
        if (root.TryGetProperty(TelegramAllowedUpdates.Message, out var message)
            && message.ValueKind == JsonValueKind.Object)
        {
            kind = new InboundUpdateKind.Message();
            return TryReadChat(message, out peerId);
        }

        if (root.TryGetProperty(TelegramAllowedUpdates.CallbackQuery, out var callback)
            && callback.ValueKind == JsonValueKind.Object)
        {
            kind = new InboundUpdateKind.CallbackQuery();
            if (callback.TryGetProperty("message", out var callbackMessage)
                && callbackMessage.ValueKind == JsonValueKind.Object
                && TryReadChat(callbackMessage, out peerId))
            {
                return true;
            }

            return TryReadUser(callback, out peerId);
        }

        if (root.TryGetProperty(TelegramAllowedUpdates.MyChatMember, out var member)
            && member.ValueKind == JsonValueKind.Object)
        {
            kind = new InboundUpdateKind.MyChatMember();
            return TryReadChat(member, out peerId);
        }

        if (root.TryGetProperty(TelegramAllowedUpdates.StoppedMessageGeneration, out var stopped)
            && stopped.ValueKind == JsonValueKind.Object)
        {
            kind = new InboundUpdateKind.StoppedMessageGeneration();
            return TryReadChat(stopped, out peerId);
        }

        kind = new InboundUpdateKind.Unknown();
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object && TryReadChat(property.Value, out peerId))
            {
                return true;
            }
        }

        peerId = "";
        return false;
    }

    private static bool TryReadChat(JsonElement owner, out string peerId)
    {
        peerId = "";
        if (!owner.TryGetProperty("chat", out var chat) || chat.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!chat.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id))
        {
            return false;
        }

        if (!chat.TryGetProperty("type", out var typeElement)
            || typeElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(typeElement.GetString()))
        {
            return false;
        }

        peerId = id.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryReadUser(JsonElement owner, out string peerId)
    {
        peerId = "";
        if (!owner.TryGetProperty("from", out var from) || from.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!from.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id))
        {
            return false;
        }

        peerId = id.ToString(CultureInfo.InvariantCulture);
        return true;
    }
}

/// <summary>Envelope plus the closed update kind the acceptor routes.</summary>
/// <param name="Envelope">Row to insert.</param>
/// <param name="Kind">Update kind.</param>
public readonly record struct ParsedTelegramUpdate(InboundEnvelope Envelope, InboundUpdateKind Kind);
