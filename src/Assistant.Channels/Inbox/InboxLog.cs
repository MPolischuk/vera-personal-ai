using Microsoft.Extensions.Logging;

namespace Assistant.Channels.Inbox;

internal static partial class InboxLog
{
    [LoggerMessage(
        EventId = 800,
        Level = LogLevel.Information,
        Message = "Inbox {InboxId} for {PeerRef} finished as {Status}")]
    public static partial void Finished(ILogger logger, long inboxId, string peerRef, string status);

    [LoggerMessage(
        EventId = 801,
        Level = LogLevel.Warning,
        Message = "Inbox {InboxId} handler failed ({ExceptionType})")]
    public static partial void HandlerFailed(ILogger logger, long inboxId, string exceptionType);

    [LoggerMessage(
        EventId = 802,
        Level = LogLevel.Information,
        Message = "Recovered {Count} inbox rows")]
    public static partial void Recovered(ILogger logger, int count);

    [LoggerMessage(
        EventId = 803,
        Level = LogLevel.Warning,
        Message = "Inbox purge schedule failed ({ExceptionType})")]
    public static partial void PurgeSeedFailed(ILogger logger, string exceptionType);

    [LoggerMessage(
        EventId = 804,
        Level = LogLevel.Information,
        Message = "Purged {Count} {Channel} inbox rows")]
    public static partial void Purged(ILogger logger, int count, string channel);
}
