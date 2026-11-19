using Assistant.Abstractions.Notifications;
using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Persistence.Notifications;

internal sealed class NotificationDeliveryStore(AssistantDbContext db, TimeProvider clock) : INotificationDeliveryStore
{
    public async ValueTask CreateAsync(
        Guid notificationId,
        string channel,
        string targetRefHash,
        int attempt,
        CancellationToken ct)
    {
        await BeginAsync(Guid.CreateVersion7(), notificationId, channel, targetRefHash, attempt, ct);
    }

    public async ValueTask BeginAsync(
        Guid deliveryId,
        Guid notificationId,
        string channel,
        string targetRefHash,
        int attempt,
        CancellationToken ct)
    {
        var entity = new NotificationDeliveryEntity
        {
            Id = deliveryId,
            NotificationId = notificationId,
            Channel = channel,
            TargetRefHash = targetRefHash,
            Attempt = attempt,
            Result = DeliveryResults.Pending,
            CreatedAt = clock.GetUtcNow(),
        };

        db.NotificationDeliveries.Add(entity);
        await db.SaveChangesAsync(ct);
    }

    public Task UpdateResultAsync(
        Guid deliveryId,
        string result,
        string? errorCode,
        string? externalId,
        CancellationToken ct)
        => CompleteAsync(deliveryId, result, errorCode, externalId, ct).AsTask();

    public async ValueTask CompleteAsync(
        Guid deliveryId,
        string result,
        string? errorCode,
        string? externalId,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        await db.NotificationDeliveries
            .Where(d => d.Id == deliveryId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(d => d.Result, result)
                    .SetProperty(d => d.ErrorCode, errorCode)
                    .SetProperty(d => d.ExternalId, externalId)
                    .SetProperty(d => d.CompletedAt, now),
                ct);
    }

    public async ValueTask<int> CountByNotificationAsync(Guid notificationId, CancellationToken ct)
    {
        return await db.NotificationDeliveries
            .Where(d => d.NotificationId == notificationId)
            .CountAsync(ct);
    }

    public async ValueTask<IReadOnlyList<StoredDelivery>> ListAsync(Guid notificationId, CancellationToken ct)
    {
        var rows = await db.NotificationDeliveries
            .AsNoTracking()
            .Where(d => d.NotificationId == notificationId)
            .OrderBy(d => d.CreatedAt)
            .Select(d => new StoredDelivery(
                d.Id,
                d.NotificationId,
                d.Channel,
                d.TargetRefHash,
                d.Attempt,
                d.Result,
                d.ErrorCode,
                d.ExternalId))
            .ToListAsync(ct);

        return rows;
    }
}
