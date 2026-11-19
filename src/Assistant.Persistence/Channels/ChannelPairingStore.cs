using Assistant.Abstractions.Channels;
using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Persistence.Channels;

internal sealed class ChannelPairingStore(AssistantDbContext db) : IChannelPairingStore
{
    public async ValueTask<StoredChannelPeer?> FindAsync(
        ChannelId channel,
        string externalPeerId,
        CancellationToken ct)
    {
        var entity = await db.ChannelPeers
            .FirstOrDefaultAsync(p => p.Channel == channel.Value && p.ExternalPeerId == externalPeerId, ct);
        return entity is null ? null : ToStored(entity);
    }

    public async ValueTask<StoredChannelPeer?> FindByIdAsync(Guid peerId, CancellationToken ct)
    {
        var entity = await db.ChannelPeers.FirstOrDefaultAsync(p => p.Id == peerId, ct);
        return entity is null ? null : ToStored(entity);
    }

    public async ValueTask<IReadOnlyList<StoredChannelPeer>> ListAsync(CancellationToken ct)
    {
        var rows = await db.ChannelPeers.AsNoTracking().ToListAsync(ct);
        return rows
            .OrderBy(row => row.Status == "pending" ? 0 : 1)
            .ThenByDescending(row => row.CreatedAt)
            .Select(ToStored)
            .ToArray();
    }

    public async ValueTask<int> CountAsync(ChannelId channel, ChannelPeerStatus status, CancellationToken ct)
        => await db.ChannelPeers.CountAsync(p => p.Channel == channel.Value && p.Status == ToStatus(status), ct);

    public async ValueTask<StoredChannelPeer> InsertAsync(NewChannelPeer peer, CancellationToken ct)
    {
        var entity = new ChannelPeerEntity
        {
            Id = Guid.CreateVersion7(),
            Channel = peer.Channel.Value,
            ExternalPeerId = peer.ExternalPeerId,
            UserId = peer.UserId,
            Status = ToStatus(peer.Status),
            DisplayName = peer.DisplayName,
            Username = peer.Username,
            PairingCodeHash = peer.CodeHash,
            PairingCodeExpiresAt = peer.CodeExpiresAt,
            ApprovedByDeviceId = peer.ApprovedByDeviceId,
            ApprovedAt = peer.Status == ChannelPeerStatus.Approved ? peer.Now : null,
            CreatedAt = peer.Now,
            LastInboundAt = peer.Now,
        };
        db.ChannelPeers.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToStored(entity);
    }

    public async ValueTask UpdateProfileAsync(
        Guid peerId,
        string? displayName,
        string? username,
        DateTimeOffset inboundAt,
        CancellationToken ct)
    {
        await db.ChannelPeers
            .Where(p => p.Id == peerId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(p => p.DisplayName, displayName)
                    .SetProperty(p => p.Username, username)
                    .SetProperty(p => p.LastInboundAt, inboundAt),
                ct);
        db.ChangeTracker.Clear();
    }

    public async ValueTask SetPendingCodeAsync(
        Guid peerId,
        string codeHash,
        DateTimeOffset expiresAt,
        string? displayName,
        string? username,
        DateTimeOffset inboundAt,
        CancellationToken ct)
    {
        await db.ChannelPeers
            .Where(p => p.Id == peerId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(p => p.Status, "pending")
                    .SetProperty(p => p.UserId, (Guid?)null)
                    .SetProperty(p => p.PairingCodeHash, codeHash)
                    .SetProperty(p => p.PairingCodeExpiresAt, expiresAt)
                    .SetProperty(p => p.PairingAttempts, 0)
                    .SetProperty(p => p.LastCodeReminderAt, (DateTimeOffset?)null)
                    .SetProperty(p => p.ApprovedAt, (DateTimeOffset?)null)
                    .SetProperty(p => p.ApprovedByDeviceId, (Guid?)null)
                    .SetProperty(p => p.RevokedAt, (DateTimeOffset?)null)
                    .SetProperty(p => p.DisplayName, displayName)
                    .SetProperty(p => p.Username, username)
                    .SetProperty(p => p.LastInboundAt, inboundAt),
                ct);
        db.ChangeTracker.Clear();
    }

    public async ValueTask SetReminderAsync(Guid peerId, DateTimeOffset at, CancellationToken ct)
    {
        await db.ChannelPeers
            .Where(p => p.Id == peerId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.LastCodeReminderAt, at), ct);
        db.ChangeTracker.Clear();
    }

    public async ValueTask<ApproveWriteResult> TryApproveAsync(
        Guid peerId,
        Guid userId,
        Guid? deviceId,
        int maxApproved,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var entity = await db.ChannelPeers.FirstOrDefaultAsync(p => p.Id == peerId, ct);
        if (entity is null || entity.Status is not ("pending" or "revoked"))
        {
            return new ApproveWriteResult.NotPending();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(42, hashtext({entity.Channel}))",
            ct);
        var approved = await db.ChannelPeers.CountAsync(
            p => p.Channel == entity.Channel && p.Status == "approved" && p.Id != peerId,
            ct);
        if (approved >= maxApproved)
        {
            await transaction.RollbackAsync(ct);
            return new ApproveWriteResult.LimitReached();
        }

        entity.Status = "approved";
        entity.UserId = userId;
        entity.ApprovedByDeviceId = deviceId;
        entity.ApprovedAt = now;
        entity.RevokedAt = null;
        entity.PairingCodeHash = null;
        entity.PairingCodeExpiresAt = null;
        entity.PairingAttempts = 0;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new ApproveWriteResult.Approved(ToStored(entity));
    }

    public async ValueTask<int> IncrementAttemptsAsync(Guid peerId, CancellationToken ct)
    {
        var entity = await db.ChannelPeers.FirstAsync(p => p.Id == peerId, ct);
        entity.PairingAttempts += 1;
        await db.SaveChangesAsync(ct);
        return entity.PairingAttempts;
    }

    public async ValueTask MarkBlockedAsync(Guid peerId, CancellationToken ct)
    {
        await db.ChannelPeers
            .Where(p => p.Id == peerId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(p => p.Status, "blocked")
                    .SetProperty(p => p.PairingCodeHash, (string?)null)
                    .SetProperty(p => p.PairingCodeExpiresAt, (DateTimeOffset?)null),
                ct);
        db.ChangeTracker.Clear();
    }

    public async ValueTask MarkRevokedAsync(Guid peerId, DateTimeOffset now, CancellationToken ct)
    {
        await db.ChannelPeers
            .Where(p => p.Id == peerId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(p => p.Status, "revoked")
                    .SetProperty(p => p.RevokedAt, now)
                    .SetProperty(p => p.PairingCodeHash, (string?)null)
                    .SetProperty(p => p.PairingCodeExpiresAt, (DateTimeOffset?)null),
                ct);
        db.ChangeTracker.Clear();
    }

    public async ValueTask InsertLinkTokenAsync(
        string tokenHash,
        Guid userId,
        ChannelId channel,
        DateTimeOffset expiresAt,
        CancellationToken ct)
    {
        db.ChannelLinkTokens.Add(new ChannelLinkTokenEntity
        {
            TokenHash = tokenHash,
            UserId = userId,
            Channel = channel.Value,
            ExpiresAt = expiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    public async ValueTask<LinkTokenLookup> FindLinkTokenAsync(string tokenHash, CancellationToken ct)
    {
        var entity = await db.ChannelLinkTokens.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
        return entity is null
            ? new LinkTokenLookup.Missing()
            : new LinkTokenLookup.Found(entity.UserId, new ChannelId(entity.Channel), entity.ExpiresAt, entity.UsedAt);
    }

    public async ValueTask<bool> TryConsumeLinkTokenAsync(string tokenHash, DateTimeOffset now, CancellationToken ct)
    {
        var updated = await db.ChannelLinkTokens
            .Where(t => t.TokenHash == tokenHash && t.UsedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.UsedAt, now), ct);
        db.ChangeTracker.Clear();
        return updated == 1;
    }

    private static StoredChannelPeer ToStored(ChannelPeerEntity entity)
        => new(
            entity.Id,
            new ChannelId(entity.Channel),
            entity.ExternalPeerId,
            entity.UserId,
            Enum.Parse<ChannelPeerStatus>(entity.Status, ignoreCase: true),
            entity.DisplayName,
            entity.Username,
            entity.PairingCodeHash,
            entity.PairingCodeExpiresAt,
            entity.PairingAttempts,
            entity.LastCodeReminderAt,
            entity.LastInboundAt,
            entity.CreatedAt,
            entity.ConversationId,
            entity.DeliveryBlocked,
            entity.ApprovedByDeviceId);

    private static string ToStatus(ChannelPeerStatus status) => status switch
    {
        ChannelPeerStatus.Pending => "pending",
        ChannelPeerStatus.Approved => "approved",
        ChannelPeerStatus.Revoked => "revoked",
        ChannelPeerStatus.Blocked => "blocked",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}
