namespace Assistant.Abstractions.Channels;

/// <summary>Peer row the pairing flow reads and writes. The code hash is never a plaintext code.</summary>
/// <param name="Id">Peer id.</param>
/// <param name="Channel">Channel.</param>
/// <param name="ExternalPeerId">External peer id. Callers must not log or return it.</param>
/// <param name="UserId">Owning user. Null while pending.</param>
/// <param name="Status">Current status.</param>
/// <param name="DisplayName">Visible name.</param>
/// <param name="Username">Username without <c>@</c>.</param>
/// <param name="CodeHash">SHA-256 hex of the normalized pairing code.</param>
/// <param name="CodeExpiresAt">When the code expires.</param>
/// <param name="PairingAttempts">Failed approval attempts.</param>
/// <param name="LastCodeReminderAt">Last time the code was told to the sender.</param>
/// <param name="LastInboundAt">Last inbound time.</param>
/// <param name="CreatedAt">When the row was created.</param>
/// <param name="ConversationId">Current conversation.</param>
/// <param name="DeliveryBlocked">Whether the user blocked the bot.</param>
/// <param name="ApprovedByDeviceId">Device that approved the peer.</param>
public sealed record StoredChannelPeer(
    Guid Id,
    ChannelId Channel,
    string ExternalPeerId,
    Guid? UserId,
    ChannelPeerStatus Status,
    string? DisplayName,
    string? Username,
    string? CodeHash,
    DateTimeOffset? CodeExpiresAt,
    int PairingAttempts,
    DateTimeOffset? LastCodeReminderAt,
    DateTimeOffset? LastInboundAt,
    DateTimeOffset CreatedAt,
    Guid? ConversationId,
    bool DeliveryBlocked,
    Guid? ApprovedByDeviceId);

/// <summary>Values for a new peer row.</summary>
/// <param name="Channel">Channel.</param>
/// <param name="ExternalPeerId">External peer id.</param>
/// <param name="DisplayName">Visible name.</param>
/// <param name="Username">Username without <c>@</c>.</param>
/// <param name="Status">Initial status.</param>
/// <param name="UserId">Owning user.</param>
/// <param name="CodeHash">SHA-256 hex of the code, when the row starts pending.</param>
/// <param name="CodeExpiresAt">Code expiry.</param>
/// <param name="ApprovedByDeviceId">Approving device, when the row starts approved.</param>
/// <param name="Now">Clock time for the inbound and approval stamps.</param>
public sealed record NewChannelPeer(
    ChannelId Channel,
    string ExternalPeerId,
    string? DisplayName,
    string? Username,
    ChannelPeerStatus Status,
    Guid? UserId,
    string? CodeHash,
    DateTimeOffset? CodeExpiresAt,
    Guid? ApprovedByDeviceId,
    DateTimeOffset Now);

/// <summary>Result of an approve write that also enforces the approved-peer cap.</summary>
public abstract record ApproveWriteResult
{
    /// <summary>The peer is now approved.</summary>
    /// <param name="Peer">Updated peer.</param>
    public sealed record Approved(StoredChannelPeer Peer) : ApproveWriteResult;

    /// <summary>The channel is already at the approved-peer cap.</summary>
    public sealed record LimitReached : ApproveWriteResult;

    /// <summary>The row is missing or is not pending.</summary>
    public sealed record NotPending : ApproveWriteResult;
}

/// <summary>Lookup of a deep-link token by its hash.</summary>
public abstract record LinkTokenLookup
{
    /// <summary>No row for that hash.</summary>
    public sealed record Missing : LinkTokenLookup;

    /// <summary>The token row.</summary>
    /// <param name="UserId">User who created the link.</param>
    /// <param name="Channel">Channel the link belongs to.</param>
    /// <param name="ExpiresAt">Expiry.</param>
    /// <param name="UsedAt">When it was consumed, if it was.</param>
    public sealed record Found(
        Guid UserId,
        ChannelId Channel,
        DateTimeOffset ExpiresAt,
        DateTimeOffset? UsedAt) : LinkTokenLookup;
}

/// <summary>
/// Writes pairing state on peers and deep-link tokens.
/// <see cref="IChannelPeerDirectory"/> stays read-only.
/// </summary>
public interface IChannelPairingStore
{
    /// <summary>Finds a peer by channel and external id.</summary>
    public ValueTask<StoredChannelPeer?> FindAsync(ChannelId channel, string externalPeerId, CancellationToken ct);

    /// <summary>Finds a peer by id.</summary>
    public ValueTask<StoredChannelPeer?> FindByIdAsync(Guid peerId, CancellationToken ct);

    /// <summary>Lists peers, pending first.</summary>
    public ValueTask<IReadOnlyList<StoredChannelPeer>> ListAsync(CancellationToken ct);

    /// <summary>Counts peers of one status on a channel.</summary>
    public ValueTask<int> CountAsync(ChannelId channel, ChannelPeerStatus status, CancellationToken ct);

    /// <summary>Inserts a peer.</summary>
    public ValueTask<StoredChannelPeer> InsertAsync(NewChannelPeer peer, CancellationToken ct);

    /// <summary>Updates the visible profile and the last inbound time.</summary>
    public ValueTask UpdateProfileAsync(
        Guid peerId,
        string? displayName,
        string? username,
        DateTimeOffset inboundAt,
        CancellationToken ct);

    /// <summary>Replaces the pending code and moves a revoked peer back to pending.</summary>
    public ValueTask SetPendingCodeAsync(
        Guid peerId,
        string codeHash,
        DateTimeOffset expiresAt,
        string? displayName,
        string? username,
        DateTimeOffset inboundAt,
        CancellationToken ct);

    /// <summary>Records that the current code was told to the sender.</summary>
    public ValueTask SetReminderAsync(Guid peerId, DateTimeOffset at, CancellationToken ct);

    /// <summary>Approves a pending peer when the channel is under the cap.</summary>
    public ValueTask<ApproveWriteResult> TryApproveAsync(
        Guid peerId,
        Guid userId,
        Guid? deviceId,
        int maxApproved,
        DateTimeOffset now,
        CancellationToken ct);

    /// <summary>Increments failed attempts and returns the new total.</summary>
    public ValueTask<int> IncrementAttemptsAsync(Guid peerId, CancellationToken ct);

    /// <summary>Marks the peer blocked and clears the code.</summary>
    public ValueTask MarkBlockedAsync(Guid peerId, CancellationToken ct);

    /// <summary>Marks an approved peer revoked and clears the code.</summary>
    public ValueTask MarkRevokedAsync(Guid peerId, DateTimeOffset now, CancellationToken ct);

    /// <summary>Inserts a deep-link token hash.</summary>
    public ValueTask InsertLinkTokenAsync(
        string tokenHash,
        Guid userId,
        ChannelId channel,
        DateTimeOffset expiresAt,
        CancellationToken ct);

    /// <summary>Finds a deep-link token by its hash.</summary>
    public ValueTask<LinkTokenLookup> FindLinkTokenAsync(string tokenHash, CancellationToken ct);

    /// <summary>Consumes a token that is still unused and unexpired. False when another caller won.</summary>
    public ValueTask<bool> TryConsumeLinkTokenAsync(string tokenHash, DateTimeOffset now, CancellationToken ct);
}
