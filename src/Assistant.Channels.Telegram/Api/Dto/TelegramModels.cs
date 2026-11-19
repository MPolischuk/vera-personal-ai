using System.Text.Json;

namespace Assistant.Channels.Telegram.Api.Dto;

public sealed class TelegramUser
{
    public long Id { get; set; }

    public bool IsBot { get; set; }

    public string FirstName { get; set; } = "";

    public string? Username { get; set; }
}

public sealed class TelegramMessage
{
    public long MessageId { get; set; }

    public string? Text { get; set; }
}

public sealed class TelegramFile
{
    public string FileId { get; set; } = "";

    public string? FilePath { get; set; }

    public long? FileSize { get; set; }
}

/// <summary>Minimal update. The inbox parser owns the rest of the payload.</summary>
public sealed class TelegramChat
{
    public long Id { get; set; }

    public string? Type { get; set; }
}

/// <summary>Bot API <c>MessageGenerationStopped</c>, field <c>stopped_message_generation</c>.</summary>
public sealed class MessageGenerationStopped
{
    public TelegramChat Chat { get; set; } = new();

    public int? MessageThreadId { get; set; }

    public int DraftId { get; set; }
}

public sealed class TelegramUpdate
{
    public long UpdateId { get; set; }

    public JsonElement Message { get; set; }

    public JsonElement EditedMessage { get; set; }

    public JsonElement CallbackQuery { get; set; }

    public JsonElement MyChatMember { get; set; }

    public MessageGenerationStopped? StoppedMessageGeneration { get; set; }
}

public sealed class WebhookInfo
{
    public string Url { get; set; } = "";

    public bool HasCustomCertificate { get; set; }

    public int PendingUpdateCount { get; set; }

    public int? MaxConnections { get; set; }

    /// <summary>Unix time. Absent when Telegram has not reported an error.</summary>
    public long? LastErrorDate { get; set; }

    public string? LastErrorMessage { get; set; }

    public IReadOnlyList<string>? AllowedUpdates { get; set; }
}

public sealed class BotCommand
{
    public string Command { get; set; } = "";

    public string Description { get; set; } = "";
}

public sealed class InlineKeyboardButton
{
    public string Text { get; set; } = "";

    public string? CallbackData { get; set; }
}

public sealed class InlineKeyboardMarkup
{
    public InlineKeyboardButton[][] InlineKeyboard { get; set; } = [];
}
