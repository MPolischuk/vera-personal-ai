using Microsoft.Extensions.Logging;

namespace Assistant.Extensions.Reminders.Firing;

internal static partial class ReminderFireLog
{
    [LoggerMessage(EventId = 870, Level = LogLevel.Warning, Message = "Reminder fire job {JobId} payload is invalid.")]
    public static partial void PayloadInvalid(ILogger logger, Guid jobId);

    [LoggerMessage(EventId = 871, Level = LogLevel.Information, Message = "Reminder {ReminderId} fire skipped because it is stale.")]
    public static partial void Stale(ILogger logger, Guid reminderId);

    [LoggerMessage(EventId = 872, Level = LogLevel.Warning, Message = "Reminder {ReminderId} recurrence cannot be scheduled.")]
    public static partial void RecurrenceInvalid(ILogger logger, Guid reminderId);

    [LoggerMessage(EventId = 873, Level = LogLevel.Information, Message = "Reminder {ReminderId} missed.")]
    public static partial void Missed(ILogger logger, Guid reminderId);

    [LoggerMessage(EventId = 874, Level = LogLevel.Information, Message = "Reminder {ReminderId} fired.")]
    public static partial void Fired(ILogger logger, Guid reminderId);

    [LoggerMessage(EventId = 875, Level = LogLevel.Warning, Message = "Reminder {ReminderId} audit write failed.")]
    public static partial void AuditFailed(ILogger logger, Exception exception, Guid reminderId);

    [LoggerMessage(EventId = 876, Level = LogLevel.Warning, Message = "Reminder {ReminderId} due event was not published.")]
    public static partial void PublishFailed(ILogger logger, Exception exception, Guid reminderId);
}
