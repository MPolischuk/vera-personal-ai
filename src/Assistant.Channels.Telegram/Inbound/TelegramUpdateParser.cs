using System.Globalization;
using System.Text.Json;
using Assistant.Abstractions.Channels;

namespace Assistant.Channels.Telegram.Inbound;

/// <summary>A voice note still on Telegram's servers. The adapter downloads it.</summary>
/// <param name="FileId">Bot API <c>file_id</c>.</param>
/// <param name="Duration">Duration reported by Telegram.</param>
/// <param name="FileSize">Size in bytes, when Telegram sent it.</param>
/// <param name="Trust">Owner-authored, unless the note was forwarded.</param>
/// <param name="OriginLabel">Forward origin, when <paramref name="Trust"/> is untrusted.</param>
internal sealed record TelegramVoiceNote(
    string FileId,
    TimeSpan Duration,
    long? FileSize,
    ContentTrust Trust,
    string? OriginLabel);

/// <summary>Closed result of parsing one Telegram update into channel parts.</summary>
internal abstract record TelegramParsedUpdate
{
    private TelegramParsedUpdate()
    {
    }

    /// <summary>Nothing to do (not a private chat, or not a kind this adapter owns).</summary>
    internal sealed record Ignored(string Reason) : TelegramParsedUpdate;

    /// <summary>The user blocked or unblocked the bot.</summary>
    internal sealed record Membership(bool DeliveryBlocked) : TelegramParsedUpdate;

    /// <summary>A known channel command. The model is not called.</summary>
    internal sealed record Command(string Name) : TelegramParsedUpdate;

    /// <summary>Parts for a turn. <paramref name="Voice"/> is downloaded by the adapter.</summary>
    internal sealed record Content(IReadOnlyList<InboundPart> Parts, TelegramVoiceNote? Voice) : TelegramParsedUpdate;
}

/// <summary>
/// Maps a raw <c>message</c> or <c>my_chat_member</c> update to inbound parts.
/// Own text is trusted. Forwards, quotes, and external replies are not.
/// </summary>
internal static class TelegramUpdateParser
{
    public static TelegramParsedUpdate Parse(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return new TelegramParsedUpdate.Ignored("unparseable");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(rawJson);
        }
        catch (JsonException)
        {
            return new TelegramParsedUpdate.Ignored("unparseable");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new TelegramParsedUpdate.Ignored("unparseable");
            }

            if (TryObject(root, "my_chat_member", out var member))
            {
                return ParseMembership(member);
            }

            if (!TryObject(root, "message", out var message))
            {
                return new TelegramParsedUpdate.Ignored("unsupported_update");
            }

            if (!IsPrivate(message))
            {
                return new TelegramParsedUpdate.Ignored("not_private");
            }

            var text = ReadString(message, "text");
            if (TelegramCommands.TryParse(text, out var command))
            {
                return new TelegramParsedUpdate.Command(command);
            }

            return ParseContent(message, text);
        }
    }

    private static TelegramParsedUpdate ParseMembership(JsonElement member)
    {
        if (!IsPrivate(member))
        {
            return new TelegramParsedUpdate.Ignored("not_private");
        }

        if (!TryObject(member, "new_chat_member", out var next))
        {
            return new TelegramParsedUpdate.Ignored("membership");
        }

        var status = ReadString(next, "status");
        return status switch
        {
            "kicked" => new TelegramParsedUpdate.Membership(DeliveryBlocked: true),
            "member" or "administrator" or "creator" => new TelegramParsedUpdate.Membership(DeliveryBlocked: false),
            _ => new TelegramParsedUpdate.Ignored("membership"),
        };
    }

    private static TelegramParsedUpdate.Content ParseContent(JsonElement message, string? text)
    {
        var forwarded = TryObject(message, "forward_origin", out var forwardOrigin);
        var trust = forwarded ? ContentTrust.Untrusted : ContentTrust.OwnerAuthored;
        var origin = forwarded ? OriginLabel(forwardOrigin) : null;
        var parts = new List<InboundPart>();

        if (TryObject(message, "quote", out var quote))
        {
            var quoted = ReadString(quote, "text");
            if (!string.IsNullOrWhiteSpace(quoted))
            {
                parts.Add(new InboundPart.Text(quoted, ContentTrust.Untrusted, "quote"));
            }
        }

        if (TryObject(message, "external_reply", out var external))
        {
            var externalText = ReadString(external, "text") ?? ReadString(external, "caption");
            if (!string.IsNullOrWhiteSpace(externalText))
            {
                var externalOrigin = TryObject(external, "origin", out var externalOriginElement)
                    ? OriginLabel(externalOriginElement)
                    : "external_reply";
                parts.Add(new InboundPart.Text(externalText, ContentTrust.Untrusted, externalOrigin));
            }
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            parts.Add(new InboundPart.Text(text, trust, origin));
        }

        var caption = ReadString(message, "caption");
        if (!string.IsNullOrWhiteSpace(caption))
        {
            parts.Add(new InboundPart.Text(caption, trust, origin));
        }

        TelegramVoiceNote? voice = null;
        if (TryObject(message, "voice", out var voiceElement))
        {
            var fileId = ReadString(voiceElement, "file_id");
            if (!string.IsNullOrWhiteSpace(fileId))
            {
                voice = new TelegramVoiceNote(
                    fileId,
                    TimeSpan.FromSeconds(ReadSeconds(voiceElement)),
                    ReadInt64(voiceElement, "file_size"),
                    trust,
                    origin);
            }
        }

        AddUnsupported(message, parts);
        return new TelegramParsedUpdate.Content(parts, voice);
    }

    private static void AddUnsupported(JsonElement message, List<InboundPart> parts)
    {
        if (message.TryGetProperty("photo", out var photo) && photo.ValueKind == JsonValueKind.Array && photo.GetArrayLength() > 0)
        {
            parts.Add(new InboundPart.Unsupported("photo"));
        }

        AddIfObject(message, "document", "document", parts);
        AddIfObject(message, "sticker", "sticker", parts);
        AddIfObject(message, "location", "location", parts);
        AddIfObject(message, "contact", "contact", parts);
        AddIfObject(message, "audio", "audio", parts);
        AddIfObject(message, "video_note", "video_note", parts);
        AddIfObject(message, "video", "video", parts);
        AddIfObject(message, "animation", "animation", parts);
    }

    private static void AddIfObject(JsonElement message, string property, string kind, List<InboundPart> parts)
    {
        if (TryObject(message, property, out _))
        {
            parts.Add(new InboundPart.Unsupported(kind));
        }
    }

    private static string OriginLabel(JsonElement origin)
    {
        var type = ReadString(origin, "type");
        return type switch
        {
            "user" => "user:" + PersonName(origin, "sender_user"),
            "hidden_user" => "hidden_user",
            "channel" => "channel:" + ChatTitle(origin),
            "chat" => "chat:" + ChatTitle(origin),
            _ => string.IsNullOrWhiteSpace(type) ? "forward" : type,
        };
    }

    private static string PersonName(JsonElement origin, string property)
    {
        if (!TryObject(origin, property, out var user))
        {
            return ReadString(origin, "sender_user_name") ?? "user";
        }

        return ReadString(user, "first_name")
            ?? ReadString(user, "username")
            ?? "user";
    }

    private static string ChatTitle(JsonElement origin)
    {
        if (!TryObject(origin, "chat", out var chat))
        {
            return "chat";
        }

        return ReadString(chat, "title") ?? "chat";
    }

    private static bool IsPrivate(JsonElement owner)
    {
        if (!TryObject(owner, "chat", out var chat))
        {
            return false;
        }

        return string.Equals(ReadString(chat, "type"), "private", StringComparison.Ordinal);
    }

    private static bool TryObject(JsonElement owner, string name, out JsonElement value)
    {
        if (owner.ValueKind == JsonValueKind.Object
            && owner.TryGetProperty(name, out value)
            && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement owner, string name)
    {
        if (owner.ValueKind != JsonValueKind.Object
            || !owner.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static int ReadSeconds(JsonElement owner)
    {
        if (!owner.TryGetProperty("duration", out var value))
        {
            return 0;
        }

        if (value.TryGetInt32(out var seconds))
        {
            return Math.Max(0, seconds);
        }

        if (value.TryGetDouble(out var fractional))
        {
            return (int)Math.Max(0, fractional);
        }

        return 0;
    }

    private static long? ReadInt64(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.TryGetInt64(out var number))
        {
            return number;
        }

        return long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
