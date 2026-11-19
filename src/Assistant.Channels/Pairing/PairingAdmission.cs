using System.Text.Json;
using Assistant.Abstractions.Channels;

namespace Assistant.Channels.Pairing;

internal enum PairingAdmissionKind
{
    /// <summary>Not a user message. The inbox handler still runs.</summary>
    Skip,

    /// <summary>Group, channel, or bot. Silence, and do not call the handler.</summary>
    Ignore,

    /// <summary>Private chat from a person. Pairing decides.</summary>
    Pair,
}

internal readonly record struct PairingAdmission(
    PairingAdmissionKind Kind,
    string? DisplayName,
    string? Username,
    string? Text);

/// <summary>Reads the Telegram filters that run before pairing. Other channels skip this step.</summary>
internal static class PairingAdmissionReader
{
    public static PairingAdmission Read(InboundEnvelope envelope)
    {
        if (envelope.Channel != ChannelId.Telegram)
        {
            return new PairingAdmission(PairingAdmissionKind.Skip, envelope.PeerDisplayName, null, null);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(string.IsNullOrWhiteSpace(envelope.RawPayloadJson) ? "{}" : envelope.RawPayloadJson);
        }
        catch (JsonException)
        {
            return new PairingAdmission(PairingAdmissionKind.Ignore, null, null, null);
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("message", out var message)
                || message.ValueKind != JsonValueKind.Object)
            {
                return new PairingAdmission(PairingAdmissionKind.Skip, envelope.PeerDisplayName, null, null);
            }

            var isBot = false;
            string? username = null;
            string? displayName = null;
            if (message.TryGetProperty("from", out var from) && from.ValueKind == JsonValueKind.Object)
            {
                isBot = from.TryGetProperty("is_bot", out var bot) && bot.ValueKind == JsonValueKind.True;
                username = ReadString(from, "username");
                displayName = JoinName(ReadString(from, "first_name"), ReadString(from, "last_name"));
            }

            if (!message.TryGetProperty("chat", out var chat)
                || chat.ValueKind != JsonValueKind.Object
                || !chat.TryGetProperty("type", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String)
            {
                return new PairingAdmission(PairingAdmissionKind.Ignore, displayName, username, null);
            }

            var text = ReadString(message, "text");
            if (!string.Equals(typeElement.GetString(), "private", StringComparison.Ordinal) || isBot)
            {
                return new PairingAdmission(PairingAdmissionKind.Ignore, displayName, username, text);
            }

            return new PairingAdmission(PairingAdmissionKind.Pair, displayName ?? envelope.PeerDisplayName, username, text);
        }
    }

    public static bool TryReadStartToken(string? text, out string token)
    {
        token = "";
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        var space = trimmed.IndexOf(' ');
        if (space <= 0)
        {
            return false;
        }

        var command = trimmed[..space];
        if (!command.Equals("/start", StringComparison.Ordinal)
            && !command.StartsWith("/start@", StringComparison.Ordinal))
        {
            return false;
        }

        token = trimmed[(space + 1)..].Trim();
        return token.Length > 0;
    }

    private static string? JoinName(string? first, string? last)
    {
        var joined = string.Join(' ', new[] { first, last }.Where(static part => !string.IsNullOrWhiteSpace(part)));
        return joined.Length == 0 ? null : joined;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
