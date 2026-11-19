using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Persistence.Auth;

internal sealed class DeviceStore(AssistantDbContext db, TimeProvider clock) : IDeviceStore
{
    public async Task<DeviceRow> AddAsync(
        string name, byte[] refreshTokenHash, CancellationToken cancellationToken)
    {
        var entity = new DeviceEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            RefreshTokenHash = refreshTokenHash.ToArray(),
            LastSeenAt = clock.GetUtcNow(),
        };
        db.Devices.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return ToRow(entity);
    }

    public async Task<DeviceRow?> RotateAsync(
        byte[] currentHash,
        byte[] newHash,
        DateTimeOffset now,
        TimeSpan maxAge,
        CancellationToken cancellationToken)
    {
        var cutoff = now - maxAge;
        var hash = currentHash.ToArray();
        var replacement = newHash.ToArray();

        var affected = await db.Devices
            .Where(d => d.RevokedAt == null && d.LastSeenAt >= cutoff)
            .Where(d => d.RefreshTokenHash != null && d.RefreshTokenHash.SequenceEqual(hash))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(d => d.RefreshTokenHash, replacement)
                    .SetProperty(d => d.LastSeenAt, now),
                cancellationToken);

        if (affected == 0)
        {
            return null;
        }

        db.ChangeTracker.Clear();
        var entity = await db.Devices.AsNoTracking().SingleAsync(
            d => d.RefreshTokenHash != null && d.RefreshTokenHash.SequenceEqual(replacement),
            cancellationToken);
        return ToRow(entity);
    }

    public async Task<bool> RevokeAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var entity = await db.Devices.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        if (entity.RevokedAt is not null)
        {
            return true;
        }

        entity.RevokedAt = now;
        entity.RefreshTokenHash = null;

        var subscriptions = await db.PushSubscriptions
            .Where(s => s.DeviceId == id && s.Status != "revoked")
            .ToListAsync(cancellationToken);
        foreach (var subscription in subscriptions)
        {
            subscription.Status = "revoked";
            subscription.RevokedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static DeviceRow ToRow(DeviceEntity entity)
        => new(entity.Id, entity.Name, entity.CreatedAt, entity.LastSeenAt, entity.RevokedAt);
}
