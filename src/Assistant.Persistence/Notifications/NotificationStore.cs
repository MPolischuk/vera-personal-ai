using System.Text.Json;
using Assistant.Abstractions.Channels;
using Assistant.Abstractions.Notifications;
using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Assistant.Persistence.Notifications;

internal sealed class NotificationStore(AssistantDbContext db, TimeProvider clock) : INotificationStore
{
    private static readonly JsonSerializerOptions ActionJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async ValueTask<NotificationReceipt> CreateOrGetAsync(
        NotificationRequest request,
        CancellationToken ct)
    {
        var existing = await db.Notifications
            .FirstOrDefaultAsync(
                n => n.UserId == request.UserId && n.DedupKey == request.DedupKey,
                ct);

        if (existing is not null)
        {
            return new NotificationReceipt(existing.Id, Created: false);
        }

        var now = clock.GetUtcNow();
        var entity = new NotificationEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            Kind = NotificationKindNames.ToWire(request.Kind),
            DedupKey = request.DedupKey,
            Title = request.Title,
            Body = request.Body,
            Trust = request.Trust.ToString().ToLowerInvariant(),
            DueAt = request.DueAt,
            PreferredChannel = request.PreferredChannel?.Value,
            ConversationId = request.ConversationId,
            Actions = request.Actions is { Count: > 0 }
                ? JsonSerializer.Serialize(request.Actions, ActionJson)
                : null,
            SourceType = request.SourceType,
            SourceId = request.SourceId,
            Status = NotificationStatuses.Pending,
            CreatedAt = now,
        };

        db.Notifications.Add(entity);

        try
        {
            await db.SaveChangesAsync(ct);
            return new NotificationReceipt(entity.Id, Created: true);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            db.Entry(entity).State = EntityState.Detached;

            var race = await db.Notifications
                .Where(n => n.UserId == request.UserId && n.DedupKey == request.DedupKey)
                .Select(n => (Guid?)n.Id)
                .FirstOrDefaultAsync(ct);

            if (race is null)
            {
                throw;
            }

            return new NotificationReceipt(race.Value, Created: false);
        }
    }

    public async ValueTask<StoredNotification?> FindAsync(Guid notificationId, CancellationToken ct)
    {
        var entity = await db.Notifications
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == notificationId, ct);

        return entity is null ? null : ToStored(entity);
    }

    public async ValueTask<bool> UpdateStatusAsync(
        Guid notificationId,
        string status,
        string? deliveredChannel,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        var updated = await db.Notifications
            .Where(n => n.Id == notificationId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(n => n.Status, status)
                    .SetProperty(n => n.DeliveredChannel, deliveredChannel)
                    .SetProperty(
                        n => n.DeliveredAt,
                        n => status == NotificationStatuses.Delivered ? now : n.DeliveredAt),
                ct);

        return updated > 0;
    }

    public async ValueTask<IReadOnlyList<DeliveredNotificationNotice>> ListRecentDeliveredAsync(
        Guid userId, DateTimeOffset since, int limit, CancellationToken ct)
    {
        if (limit < 1)
        {
            return [];
        }

        var rows = await db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId
                && n.DeliveredAt != null
                && n.DeliveredAt >= since
                && (n.Status == NotificationStatuses.Delivered || n.Status == NotificationStatuses.Acted))
            .OrderByDescending(n => n.DeliveredAt)
            .ThenByDescending(n => n.Id)
            .Take(limit)
            .Select(n => new
            {
                DeliveredAt = n.DeliveredAt!.Value,
                n.DeliveredChannel,
                n.Title,
                n.Body,
                n.ActionTaken,
                n.Trust,
            })
            .ToListAsync(ct);

        return rows.Select(row => new DeliveredNotificationNotice(
            row.DeliveredAt,
            row.DeliveredChannel,
            row.Title,
            row.Body ?? "",
            row.ActionTaken,
            row.Trust == "untrusted"
                ? NotificationContentTrust.Untrusted
                : NotificationContentTrust.Trusted)).ToList();
    }

    public async ValueTask<IReadOnlyList<NotificationLogItem>> ListRecentAsync(
        Guid userId, int limit, CancellationToken ct)
    {
        if (limit < 1)
        {
            return [];
        }

        var rows = await db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Take(limit)
            .Select(n => new
            {
                n.Id,
                n.Title,
                n.Body,
                n.Status,
                n.DeliveredChannel,
                n.ActionTaken,
                n.CreatedAt,
            })
            .ToListAsync(ct);

        return rows.Select(row => new NotificationLogItem(
            row.Id,
            row.Title,
            row.Body ?? "",
            row.Status,
            row.DeliveredChannel,
            row.ActionTaken,
            row.CreatedAt)).ToList();
    }

    public async ValueTask<int> CountDeliveredSinceAsync(
        Guid userId,
        DateTimeOffset since,
        CancellationToken ct)
    {
        return await db.Notifications.CountAsync(
            n => n.UserId == userId
                && n.Status == NotificationStatuses.Delivered
                && n.DeliveredAt >= since,
            ct);
    }

    public async ValueTask<IReadOnlyList<Guid>> ListPendingAsync(int limit, CancellationToken ct)
    {
        return await db.Notifications
            .Where(n => n.Status == NotificationStatuses.Pending)
            .OrderBy(n => n.CreatedAt)
            .Take(limit)
            .Select(n => n.Id)
            .ToListAsync(ct);
    }

    private static StoredNotification ToStored(NotificationEntity entity)
        => new(
            entity.Id,
            entity.UserId,
            NotificationKindNames.Parse(entity.Kind),
            entity.Title,
            entity.Body ?? "",
            entity.DueAt,
            string.IsNullOrEmpty(entity.PreferredChannel) ? null : new ChannelId(entity.PreferredChannel),
            entity.ConversationId,
            ReadActions(entity.Actions),
            entity.Status,
            entity.CreatedAt);

    private static List<OutboundAction>? ReadActions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<OutboundAction>>(json, ActionJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsUniqueViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return true;
            }
        }

        return false;
    }
}
