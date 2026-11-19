namespace Assistant.Abstractions.Channels;

/// <summary>
/// Peer status on the channel.
/// </summary>
public enum ChannelPeerStatus
{
    /// <summary>Approval request still pending.</summary>
    Pending,

    /// <summary>Approved by the user.</summary>
    Approved,

    /// <summary>Unlinked by the user.</summary>
    Revoked,

    /// <summary>Blocked (after failed attempts, or manually).</summary>
    Blocked
}

/// <summary>
/// Peer (sender) on an external channel.
/// </summary>
/// <param name="Id">Peer id.</param>
/// <param name="Channel">Channel the peer belongs to.</param>
/// <param name="ExternalPeerId">External peer id (chat_id, wa_id, and so on).</param>
/// <param name="UserId">Owning user (null while status is <see cref="ChannelPeerStatus.Pending"/>).</param>
/// <param name="Status">Current peer status.</param>
/// <param name="DisplayName">Visible name of the peer.</param>
/// <param name="ConversationId">Current conversation of the channel.</param>
/// <param name="DeliveryBlocked">Whether the user blocked the bot.</param>
/// <param name="LastInboundAt">Last time the peer wrote.</param>
public sealed record ChannelPeer(
    Guid Id,
    ChannelId Channel,
    string ExternalPeerId,
    Guid? UserId,
    ChannelPeerStatus Status,
    string? DisplayName,
    Guid? ConversationId,
    bool DeliveryBlocked,
    DateTimeOffset? LastInboundAt);

/// <summary>
/// Directory of channel peers.
/// </summary>
public interface IChannelPeerDirectory
{
    /// <summary>
    /// Finds a peer by channel and external id.
    /// </summary>
    public ValueTask<ChannelPeer?> FindAsync(ChannelId channel, string externalPeerId, CancellationToken ct);

    /// <summary>
    /// Finds the peer linked to a conversation.
    /// </summary>
    public ValueTask<ChannelPeer?> FindByConversationAsync(Guid conversationId, CancellationToken ct);

    /// <summary>
    /// Finds the approved peer of a user on a channel.
    /// </summary>
    public ValueTask<ChannelPeer?> FindApprovedForUserAsync(ChannelId channel, Guid userId, CancellationToken ct);

    /// <summary>
    /// Records whether the user blocked the bot.
    /// </summary>
    public ValueTask SetDeliveryBlockedAsync(Guid peerId, bool blocked, CancellationToken ct);

    /// <summary>
    /// Sets the current conversation of a peer.
    /// </summary>
    public ValueTask SetConversationAsync(Guid peerId, Guid conversationId, CancellationToken ct);
}
