using Microsoft.Extensions.Logging;

namespace Assistant.Channels.Telegram.Outbound;

internal static partial class TelegramTurnLog
{
    [LoggerMessage(
        EventId = 831,
        Level = LogLevel.Warning,
        Message = "Telegram typing failed ({ExceptionType}) for {PeerRef}")]
    public static partial void TypingFailed(ILogger logger, string exceptionType, string peerRef);

    [LoggerMessage(
        EventId = 832,
        Level = LogLevel.Warning,
        Message = "Telegram sendMessage failed ({ExceptionType}) for {PeerRef}")]
    public static partial void SendFailed(ILogger logger, string exceptionType, string peerRef);

    [LoggerMessage(
        EventId = 833,
        Level = LogLevel.Warning,
        Message = "Telegram sendRichMessage rejected ({ErrorCode}) for {PeerRef}; falling back to HTML")]
    public static partial void RichMessageRejected(ILogger logger, int errorCode, string peerRef);

    [LoggerMessage(
        EventId = 834,
        Level = LogLevel.Warning,
        Message = "Telegram sendRichMessageDraft failed ({ErrorCode}) for {PeerRef}")]
    public static partial void DraftFailed(ILogger logger, int errorCode, string peerRef);
}
