namespace Assistant.Channels.Telegram.Webhook;

/// <summary>
/// The only list of updates Telegram is asked to deliver.
/// Webhook, polling, and the CLI all read this.
/// </summary>
public static class TelegramAllowedUpdates
{
    public const string Message = "message";

    public const string CallbackQuery = "callback_query";

    public const string MyChatMember = "my_chat_member";

    public const string StoppedMessageGeneration = "stopped_message_generation";

    public static readonly IReadOnlyList<string> All = [Message, CallbackQuery, MyChatMember, StoppedMessageGeneration];
}
