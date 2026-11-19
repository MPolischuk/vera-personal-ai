namespace Assistant.Channels.Telegram;

/// <summary><c>Telegram</c> section. Defaults keep the channel off and hold no secrets.</summary>
public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    public bool Enabled { get; set; }

    /// <summary><see cref="TelegramReceiveMode.Webhook"/> or development <see cref="TelegramReceiveMode.Polling"/>.</summary>
    public TelegramReceiveMode Mode { get; set; } = TelegramReceiveMode.Webhook;

    public string BotToken { get; set; } = "";

    /// <summary>Bot username without <c>@</c>.</summary>
    public string BotUsername { get; set; } = "";

    public string WebhookSecret { get; set; } = "";

    public bool ManageWebhook { get; set; } = true;

    public int MaxConnections { get; set; } = 5;

    public int MaxVoiceSeconds { get; set; } = 120;

    public TelegramVoiceReplies VoiceReplies { get; set; } = TelegramVoiceReplies.Never;

    /// <summary>Send the final reply with <c>sendRichMessage</c>. A 400 falls back to HTML.</summary>
    public bool UseRichMessages { get; set; } = true;

    /// <summary>Stream the reply with <c>sendRichMessageDraft</c>. Off restores the typing indicator.</summary>
    public bool UseDraftStreaming { get; set; } = true;

    /// <summary>Minimum gap between draft updates. The preview is also resent every 20 seconds.</summary>
    public TimeSpan DraftUpdateInterval { get; set; } = TimeSpan.FromSeconds(1);
}

/// <summary>How the process receives Telegram updates. Polling is for development only.</summary>
public enum TelegramReceiveMode
{
    Webhook,
    Polling,
}

/// <summary>When a turn reply may go out as a voice note.</summary>
public enum TelegramVoiceReplies
{
    Never,
    WhenUserSentVoice,
    Always,
}
