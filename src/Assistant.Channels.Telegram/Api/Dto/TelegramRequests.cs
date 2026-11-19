namespace Assistant.Channels.Telegram.Api.Dto;

public sealed class SendMessageRequest
{
    public long ChatId { get; set; }

    public string Text { get; set; } = "";

    public string? ParseMode { get; set; } = "HTML";

    public bool? DisableNotification { get; set; }

    public InlineKeyboardMarkup? ReplyMarkup { get; set; }
}

/// <summary>Bot API <c>InputRichMessage</c>. Only <c>markdown</c> is sent.</summary>
public sealed class InputRichMessage
{
    public string Markdown { get; set; } = "";
}

public sealed class SendRichMessageRequest
{
    public long ChatId { get; set; }

    public InputRichMessage RichMessage { get; set; } = new();

    public bool? DisableNotification { get; set; }
}

public sealed class SendRichMessageDraftRequest
{
    public long ChatId { get; set; }

    public int DraftId { get; set; }

    public InputRichMessage RichMessage { get; set; } = new();

    public bool? CanStop { get; set; }

    public bool? KeepOnStop { get; set; }
}

public sealed class EditMessageReplyMarkupRequest
{
    public long ChatId { get; set; }

    public long MessageId { get; set; }

    public InlineKeyboardMarkup? ReplyMarkup { get; set; }
}

public sealed class AnswerCallbackQueryRequest
{
    public string CallbackQueryId { get; set; } = "";

    public string? Text { get; set; }

    public bool? ShowAlert { get; set; }
}

public sealed class SendChatActionRequest
{
    public long ChatId { get; set; }

    public string Action { get; set; } = "typing";
}

public sealed class GetFileRequest
{
    public string FileId { get; set; } = "";
}

public sealed class SetWebhookRequest
{
    public string Url { get; set; } = "";

    public string? SecretToken { get; set; }

    public int? MaxConnections { get; set; }

    public IReadOnlyList<string>? AllowedUpdates { get; set; }

    public bool? DropPendingUpdates { get; set; }
}

public sealed class DeleteWebhookRequest
{
    public bool DropPendingUpdates { get; set; }
}

public sealed class GetUpdatesRequest
{
    public long? Offset { get; set; }

    public int? Timeout { get; set; }

    public IReadOnlyList<string>? AllowedUpdates { get; set; }
}

public sealed class SetMyCommandsRequest
{
    public IReadOnlyList<BotCommand> Commands { get; set; } = [];
}

public sealed class SendVoiceRequest
{
    public long ChatId { get; set; }

    public required Stream Voice { get; init; }

    public string FileName { get; set; } = "voice.ogg";

    public string? ContentType { get; set; }

    public string? Caption { get; set; }

    public bool? DisableNotification { get; set; }
}
