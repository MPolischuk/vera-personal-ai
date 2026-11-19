using Microsoft.Extensions.Logging;

namespace Assistant.Channels.Delivery;

internal static partial class DeliveryLog
{
    [LoggerMessage(
        EventId = 780,
        Level = LogLevel.Information,
        Message = "Notification {NotificationId} dropped during quiet hours")]
    public static partial void QuietHoursDropped(ILogger logger, Guid notificationId);

    [LoggerMessage(
        EventId = 781,
        Level = LogLevel.Information,
        Message = "Notification {NotificationId} deferred; {Count} delivered in the last hour")]
    public static partial void RateLimited(ILogger logger, Guid notificationId, int count);

    [LoggerMessage(
        EventId = 782,
        Level = LogLevel.Warning,
        Message = "Notification {NotificationId} send on {Channel} threw {ExceptionType}")]
    public static partial void SendThrew(ILogger logger, Guid notificationId, string channel, string exceptionType);

    [LoggerMessage(
        EventId = 783,
        Level = LogLevel.Warning,
        Message = "Notification {NotificationId} channel {Channel} asked to deactivate the target")]
    public static partial void DeactivateTarget(ILogger logger, Guid notificationId, string channel);

    [LoggerMessage(
        EventId = 784,
        Level = LogLevel.Warning,
        Message = "Job {JobId} payload for notification.dispatch is invalid")]
    public static partial void PayloadInvalid(ILogger logger, Guid jobId);
}
