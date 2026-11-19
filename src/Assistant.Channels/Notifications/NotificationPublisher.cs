using Assistant.Abstractions.Notifications;
using Assistant.Abstractions.Scheduling;
using Assistant.Channels.Delivery;

namespace Assistant.Channels.Notifications;

/// <summary>
/// Inserts the notification and schedules <c>notification.dispatch</c>.
/// The idempotency key is <c>notification:{id}:dispatch</c>, so a repeated publish does not enqueue a second job.
/// </summary>
public sealed class NotificationPublisher(
    INotificationStore store,
    IJobScheduler scheduler,
    TimeProvider clock) : INotificationPublisher
{
    public const int MaxTitleLength = 60;

    public const int MaxBodyLength = 1000;

    public const int MaxDedupKeyLength = 200;

    public async ValueTask<NotificationReceipt> PublishAsync(NotificationRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.DedupKey) || request.DedupKey.Length > MaxDedupKeyLength)
        {
            throw new ArgumentException("DedupKey is required and must be at most 200 characters.", nameof(request));
        }

        if (request.Title.Length > MaxTitleLength)
        {
            throw new ArgumentException("Title must be at most 60 characters.", nameof(request));
        }

        if (request.Body.Length > MaxBodyLength)
        {
            throw new ArgumentException("Body must be at most 1000 characters.", nameof(request));
        }

        var receipt = await store.CreateOrGetAsync(request, ct);
        var spec = new JobSpec(
            NotificationDispatchHandler.KindName,
            clock.GetUtcNow(),
            NotificationDispatchHandler.SerializePayload(receipt.NotificationId, request.AllowDuringQuietHours),
            NotificationDispatchHandler.IdempotencyKey(receipt.NotificationId),
            NotificationDispatchHandler.MaxAttempts);
        await scheduler.ScheduleAsync(spec, ct);
        return receipt;
    }
}
