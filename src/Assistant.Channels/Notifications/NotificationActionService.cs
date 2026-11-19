using Assistant.Abstractions.Channels;
using Assistant.Abstractions.Notifications;
using Assistant.Abstractions.Reminders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Channels.Notifications;

/// <summary>Limits for notification buttons. Bound from the <c>Reminders</c> section.</summary>
public sealed class NotificationActionOptions
{
    public const string SectionName = "Reminders";

    public int MaxSnoozesPerOccurrence { get; set; } = 6;
}

/// <summary>
/// Applies <c>done</c>, <c>snooze:10m</c>, and <c>opened</c>. Does not call a language model.
/// </summary>
public sealed class NotificationActionService(
    IReminderFiringStore reminders,
    IChannelAuditSink audit,
    IOptions<NotificationActionOptions> options,
    ILogger<NotificationActionService> logger) : INotificationActionService
{
    public static readonly TimeSpan SnoozeDelay = TimeSpan.FromMinutes(10);

    public async ValueTask<bool> TryExecuteAsync(
        Guid userId, Guid notificationId, string actionId, CancellationToken ct)
        => (await ExecuteAsync(userId, notificationId, actionId, ct)).Status
            == NotificationActionStatus.Applied;

    public async ValueTask<NotificationActionResult> ExecuteAsync(
        Guid userId, Guid notificationId, string actionId, CancellationToken ct)
    {
        var max = options.Value.MaxSnoozesPerOccurrence;
        if (max < 0)
        {
            max = 0;
        }

        var result = await reminders.ApplyNotificationActionAsync(
            userId, notificationId, actionId, max, SnoozeDelay, ct);
        if (result.Status != NotificationActionStatus.Applied)
        {
            return result;
        }

        try
        {
            var channel = string.IsNullOrWhiteSpace(result.Channel)
                ? ChannelId.Hub
                : new ChannelId(result.Channel);
            await audit.RecordAsync(
                new ChannelAuditEntry(
                    "notification.action",
                    channel,
                    null,
                    null,
                    null,
                    actionId),
                ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            NotificationActionLog.AuditFailed(logger, exception, notificationId);
        }

        return result;
    }
}
