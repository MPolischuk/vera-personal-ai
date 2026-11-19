using Microsoft.Extensions.Logging;

namespace Assistant.Channels.Telegram.Inbound;

internal static partial class TelegramInboundLog
{
    [LoggerMessage(
        EventId = 833,
        Level = LogLevel.Warning,
        Message = "Telegram inbound reply failed ({ExceptionType}) for {PeerRef}")]
    public static partial void ReplyFailed(ILogger logger, string exceptionType, string peerRef);

    [LoggerMessage(
        EventId = 834,
        Level = LogLevel.Warning,
        Message = "Telegram voice download failed ({ExceptionType}) for {PeerRef}")]
    public static partial void VoiceFailed(ILogger logger, string exceptionType, string peerRef);
}
