using Microsoft.Extensions.Logging;

namespace Assistant.Channels.Telegram.Outbound;

internal static partial class TelegramOutboundLog
{
    [LoggerMessage(
        EventId = 835,
        Level = LogLevel.Critical,
        Message = "Telegram rejected the bot token (401). The channel stays disabled until the process restarts.")]
    public static partial void Unauthorized(ILogger logger);

    [LoggerMessage(
        EventId = 836,
        Level = LogLevel.Warning,
        Message = "Telegram outbound send failed ({ExceptionType}) for {PeerRef}")]
    public static partial void SendFailed(ILogger logger, string exceptionType, string peerRef);

    [LoggerMessage(
        EventId = 837,
        Level = LogLevel.Error,
        Message = "Telegram outbound rejected invalid actions for {PeerRef}")]
    public static partial void ActionsInvalid(ILogger logger, string peerRef);
}
