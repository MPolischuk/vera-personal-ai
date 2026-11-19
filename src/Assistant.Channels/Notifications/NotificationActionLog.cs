using Microsoft.Extensions.Logging;

namespace Assistant.Channels.Notifications;

internal static partial class NotificationActionLog
{
    [LoggerMessage(EventId = 830, Level = LogLevel.Warning, Message = "Notification {NotificationId} action audit failed.")]
    public static partial void AuditFailed(ILogger logger, Exception exception, Guid notificationId);
}
