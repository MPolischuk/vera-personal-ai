using Assistant.Abstractions.Channels;
using Assistant.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Persistence.Channels;

internal sealed class ChannelPeerStore(AssistantDbContext db) : IChannelPeerDirectory
{
    public async ValueTask<ChannelPeer?> FindAsync(
        ChannelId channel,
        string externalPeerId,
        CancellationToken ct)
    {
        var entity = await db.ChannelPeers
            .FirstOrDefaultAsync(
                p => p.Channel == channel.Value && p.ExternalPeerId == externalPeerId,
                ct);

        return entity is null ? null : ToDomain(entity);
    }

    public async ValueTask<ChannelPeer?> FindByConversationAsync(
        Guid conversationId,
        CancellationToken ct)
    {
        var entity = await db.ChannelPeers
            .FirstOrDefaultAsync(p => p.ConversationId == conversationId, ct);

        return entity is null ? null : ToDomain(entity);
    }

    public async ValueTask<ChannelPeer?> FindApprovedForUserAsync(
        ChannelId channel,
        Guid userId,
        CancellationToken ct)
    {
        var entity = await db.ChannelPeers
            .FirstOrDefaultAsync(
                p => p.Channel == channel.Value
                     && p.UserId == userId
                     && p.Status == "approved",
                ct);

        return entity is null ? null : ToDomain(entity);
    }

    public async ValueTask SetDeliveryBlockedAsync(Guid peerId, bool blocked, CancellationToken ct)
    {
        await db.ChannelPeers
            .Where(p => p.Id == peerId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(p => p.DeliveryBlocked, blocked),
                ct);
    }

    public async ValueTask SetConversationAsync(Guid peerId, Guid conversationId, CancellationToken ct)
    {
        await db.ChannelPeers
            .Where(p => p.Id == peerId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(p => p.ConversationId, conversationId),
                ct);
    }

    private static ChannelPeer ToDomain(ChannelPeerEntity entity)
        => new(
            entity.Id,
            new ChannelId(entity.Channel),
            entity.ExternalPeerId,
            entity.UserId,
            Enum.Parse<ChannelPeerStatus>(entity.Status, ignoreCase: true),
            entity.DisplayName,
            entity.ConversationId,
            entity.DeliveryBlocked,
            entity.LastInboundAt);
}
