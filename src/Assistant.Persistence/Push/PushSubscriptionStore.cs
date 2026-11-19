using System.Security.Cryptography;
using System.Text;
using Assistant.Abstractions.Push;
using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Persistence.Push;

internal sealed class PushSubscriptionStore(AssistantDbContext db, TimeProvider clock) : IPushSubscriptionReader
{
    public ValueTask<Guid> UpsertAsync(
        Guid userId,
        Guid deviceId,
        string endpoint,
        string? p256dh,
        string? auth,
        CancellationToken ct)
        => UpsertAsync(userId, deviceId, endpoint, p256dh, auth, vapidPublicKeyHash: null, userAgentFamily: null, ct);

    public async ValueTask<Guid> UpsertAsync(
        Guid userId,
        Guid deviceId,
        string endpoint,
        string? p256dh,
        string? auth,
        string? vapidPublicKeyHash,
        string? userAgentFamily,
        CancellationToken ct)
    {
        var endpointHash = ComputeHash(endpoint);
        var family = TrimFamily(userAgentFamily);

        var existing = await db.PushSubscriptions
            .FirstOrDefaultAsync(s => s.EndpointHash == endpointHash, ct);

        if (existing is not null)
        {
            existing.UserId = userId;
            existing.DeviceId = deviceId;
            existing.Endpoint = endpoint;
            existing.P256dh = p256dh;
            existing.Auth = auth;
            existing.VapidPublicKeyHash = vapidPublicKeyHash;
            if (family is not null)
            {
                existing.UserAgentFamily = family;
            }

            existing.Status = "active";
            existing.RevokedAt = null;
            existing.FailureCount = 0;

            await db.SaveChangesAsync(ct);
            return existing.Id;
        }

        var entity = new PushSubscriptionEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            DeviceId = deviceId,
            EndpointHash = endpointHash,
            Endpoint = endpoint,
            P256dh = p256dh,
            Auth = auth,
            VapidPublicKeyHash = vapidPublicKeyHash,
            UserAgentFamily = family,
            Status = "active",
            CreatedAt = clock.GetUtcNow(),
        };

        db.PushSubscriptions.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async ValueTask<bool> HasActiveAsync(Guid userId, CancellationToken ct)
        => await db.PushSubscriptions.AnyAsync(s => s.UserId == userId && s.Status == "active", ct);

    public async ValueTask<IReadOnlyList<(Guid Id, string Endpoint, string? P256dh, string? Auth)>>
        ListActiveByUserAsync(Guid userId, CancellationToken ct)
    {
        return await db.PushSubscriptions
            .Where(s => s.UserId == userId && s.Status == "active")
            .Select(s => new ValueTuple<Guid, string, string?, string?>(
                s.Id, s.Endpoint, s.P256dh, s.Auth))
            .ToListAsync(ct);
    }

    public async ValueTask<IReadOnlyList<PushSubscriptionTarget>> ListActiveTargetsAsync(
        Guid userId,
        CancellationToken ct)
        => await ListTargetsAsync(userId, deviceId: null, ct);

    public async ValueTask<IReadOnlyList<PushSubscriptionTarget>> ListActiveTargetsByDeviceAsync(
        Guid userId,
        Guid deviceId,
        CancellationToken ct)
        => await ListTargetsAsync(userId, deviceId, ct);

    public async ValueTask<IReadOnlyList<PushSubscriptionSummary>> ListSummariesAsync(
        Guid userId,
        CancellationToken ct)
    {
        return await db.PushSubscriptions
            .Where(s => s.UserId == userId && s.Status != "revoked")
            .OrderBy(s => s.CreatedAt)
            .Select(s => new PushSubscriptionSummary(
                s.Id,
                s.UserAgentFamily,
                s.Status,
                s.CreatedAt,
                s.LastSuccessAt,
                s.LastFailureAt))
            .ToListAsync(ct);
    }

    public async ValueTask RevokeByDeviceAsync(Guid deviceId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        await db.PushSubscriptions
            .Where(s => s.DeviceId == deviceId && s.Status == "active")
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(s => s.Status, "revoked")
                    .SetProperty(s => s.RevokedAt, now),
                ct);
    }

    public async ValueTask<bool> RevokeCurrentAsync(Guid userId, Guid deviceId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var updated = await db.PushSubscriptions
            .Where(s => s.UserId == userId && s.DeviceId == deviceId && s.Status != "revoked")
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(s => s.Status, "revoked")
                    .SetProperty(s => s.RevokedAt, now),
                ct);
        return updated > 0;
    }

    public async ValueTask RecordDeliveryAsync(Guid subscriptionId, PushDeliveryEffect effect, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var query = db.PushSubscriptions.Where(s => s.Id == subscriptionId);
        switch (effect)
        {
            case PushDeliveryEffect.Succeeded:
                await query.Where(s => s.Status == "active").ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(s => s.LastSuccessAt, now)
                        .SetProperty(s => s.FailureCount, 0),
                    ct);
                break;
            case PushDeliveryEffect.Failed:
                await query.ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(s => s.LastFailureAt, now)
                        .SetProperty(s => s.FailureCount, s => s.FailureCount + 1),
                    ct);
                break;
            case PushDeliveryEffect.Disabled:
                await query.Where(s => s.Status != "revoked").ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(s => s.Status, "disabled")
                        .SetProperty(s => s.LastFailureAt, now)
                        .SetProperty(s => s.FailureCount, s => s.FailureCount + 1),
                    ct);
                break;
            case PushDeliveryEffect.Revoked:
                await query.ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(s => s.Status, "revoked")
                        .SetProperty(s => s.RevokedAt, now)
                        .SetProperty(s => s.LastFailureAt, now)
                        .SetProperty(s => s.FailureCount, s => s.FailureCount + 1),
                    ct);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(effect), effect, "Unknown push delivery effect.");
        }
    }

    public async ValueTask<int> PurgeRevokedAsync(TimeSpan retention, CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow() - retention;

        return await db.PushSubscriptions
            .Where(s => s.Status == "revoked" && s.RevokedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    private async Task<IReadOnlyList<PushSubscriptionTarget>> ListTargetsAsync(
        Guid userId,
        Guid? deviceId,
        CancellationToken ct)
    {
        var query = db.PushSubscriptions.Where(s =>
            s.UserId == userId
            && s.Status == "active"
            && s.P256dh != null
            && s.P256dh != ""
            && s.Auth != null
            && s.Auth != "");
        if (deviceId is Guid device)
        {
            query = query.Where(s => s.DeviceId == device);
        }

        return await query
            .Select(s => new PushSubscriptionTarget(s.Id, s.Endpoint, s.P256dh!, s.Auth!))
            .ToListAsync(ct);
    }

    private static string? TrimFamily(string? userAgentFamily)
    {
        if (string.IsNullOrWhiteSpace(userAgentFamily))
        {
            return null;
        }

        var trimmed = userAgentFamily.Trim();
        return trimmed.Length <= 50 ? trimmed : trimmed[..50];
    }

    private static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
