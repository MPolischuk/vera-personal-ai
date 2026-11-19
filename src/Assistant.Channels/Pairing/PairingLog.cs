using Microsoft.Extensions.Logging;

namespace Assistant.Channels.Pairing;

internal static partial class PairingLog
{
    [LoggerMessage(EventId = 810, Level = LogLevel.Information, Message = "Pairing {Decision} for {PeerRef}")]
    public static partial void Decided(ILogger logger, string decision, string peerRef);

    [LoggerMessage(EventId = 811, Level = LogLevel.Warning, Message = "Pairing reply skipped; no messenger for {Channel}")]
    public static partial void ReplySkipped(ILogger logger, string channel);

    [LoggerMessage(EventId = 812, Level = LogLevel.Warning, Message = "Pairing reply failed ({ExceptionType})")]
    public static partial void ReplyFailed(ILogger logger, string exceptionType);
}
