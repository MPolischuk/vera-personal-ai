using Microsoft.Extensions.Logging;

namespace Assistant.Channels.Telegram.Webhook;

internal static partial class TelegramWebhookLog
{
    [LoggerMessage(
        EventId = 820,
        Level = LogLevel.Warning,
        Message = "Telegram update ignored because it could not be parsed")]
    public static partial void Unparseable(ILogger logger);

    [LoggerMessage(
        EventId = 821,
        Level = LogLevel.Information,
        Message = "Telegram update {InboxId} for {PeerRef} queued")]
    public static partial void Queued(ILogger logger, long inboxId, string peerRef);

    [LoggerMessage(
        EventId = 822,
        Level = LogLevel.Error,
        Message = "Telegram bot username does not match Telegram:BotUsername; the channel is disabled")]
    public static partial void UsernameMismatch(ILogger logger);

    [LoggerMessage(
        EventId = 823,
        Level = LogLevel.Critical,
        Message = "Telegram rejected the bot token ({ErrorCode}); the channel is disabled")]
    public static partial void TokenRejected(ILogger logger, int errorCode);

    [LoggerMessage(
        EventId = 824,
        Level = LogLevel.Warning,
        Message = "Telegram getMe failed ({ExceptionType}); webhook reconciliation skipped")]
    public static partial void GetMeFailed(ILogger logger, string exceptionType);

    [LoggerMessage(
        EventId = 825,
        Level = LogLevel.Information,
        Message = "Telegram webhook left unchanged (pending={PendingUpdateCount}, last_error={LastError})")]
    public static partial void Unchanged(ILogger logger, int pendingUpdateCount, string lastError);

    [LoggerMessage(
        EventId = 826,
        Level = LogLevel.Information,
        Message = "Telegram webhook registered (pending={PendingUpdateCount}, last_error={LastError})")]
    public static partial void Registered(ILogger logger, int pendingUpdateCount, string lastError);

    [LoggerMessage(
        EventId = 827,
        Level = LogLevel.Warning,
        Message = "Telegram webhook reconciliation failed ({ExceptionType})")]
    public static partial void ReconcileFailed(ILogger logger, string exceptionType);

    [LoggerMessage(
        EventId = 828,
        Level = LogLevel.Information,
        Message = "Telegram polling is deleting the webhook before getUpdates")]
    public static partial void PollingDeletesWebhook(ILogger logger);

    [LoggerMessage(
        EventId = 829,
        Level = LogLevel.Warning,
        Message = "Telegram polling failed ({ExceptionType})")]
    public static partial void PollingFailed(ILogger logger, string exceptionType);

    [LoggerMessage(
        EventId = 830,
        Level = LogLevel.Warning,
        Message = "Telegram setMyCommands failed ({ExceptionType})")]
    public static partial void CommandsFailed(ILogger logger, string exceptionType);
}
