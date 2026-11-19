namespace Assistant.Abstractions.Channels;

/// <summary>Peer row safe to show in Ajustes. No raw external id and no code hash.</summary>
/// <param name="Id">Peer id.</param>
/// <param name="Channel">Channel.</param>
/// <param name="Status">Status.</param>
/// <param name="DisplayName">Visible name.</param>
/// <param name="Username">Username without <c>@</c>.</param>
/// <param name="MaskedExternalId">Last four characters, prefixed with an ellipsis.</param>
/// <param name="CreatedAt">When the request arrived.</param>
/// <param name="LastInboundAt">Last time the peer wrote.</param>
public sealed record PeerSummary(
    Guid Id,
    ChannelId Channel,
    ChannelPeerStatus Status,
    string? DisplayName,
    string? Username,
    string MaskedExternalId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastInboundAt);

/// <summary>Result of approving with a typed code.</summary>
public abstract record PeerApprovalResult
{
    /// <summary>The peer is approved.</summary>
    /// <param name="Peer">Approved peer. The external id must not be written to the response or the logs.</param>
    public sealed record Approved(ChannelPeer Peer) : PeerApprovalResult;

    /// <summary>The code does not match.</summary>
    public sealed record InvalidCode : PeerApprovalResult;

    /// <summary>The code is past its TTL.</summary>
    public sealed record Expired : PeerApprovalResult;

    /// <summary>The peer is blocked after too many failures, or was already blocked.</summary>
    public sealed record Blocked : PeerApprovalResult;

    /// <summary>The channel already has an approved peer.</summary>
    public sealed record LimitReached : PeerApprovalResult;

    /// <summary>No pending peer with that id.</summary>
    public sealed record NotFound : PeerApprovalResult;
}

/// <summary>Result of revoke or block.</summary>
public abstract record PeerMutationResult
{
    /// <summary>The change was applied.</summary>
    public sealed record Ok : PeerMutationResult;

    /// <summary>The peer does not exist or does not belong to the user.</summary>
    public sealed record NotFound : PeerMutationResult;
}

/// <summary>Deep-link token in the clear, returned once to the authenticated caller.</summary>
/// <param name="Token">Single-use token. Do not log it.</param>
/// <param name="ExpiresAt">Expiry.</param>
public sealed record IssuedDeepLink(string Token, DateTimeOffset ExpiresAt);

/// <summary>What the expiry job should do with a pending code.</summary>
public abstract record CodeRotation
{
    /// <summary>A new code replaced an expired one.</summary>
    /// <param name="Channel">Channel.</param>
    /// <param name="ExternalPeerId">External peer id. Do not log it.</param>
    /// <param name="Code">Display form. Do not log it.</param>
    /// <param name="ExpiresAt">New expiry.</param>
    public sealed record Rotated(
        ChannelId Channel,
        string ExternalPeerId,
        string Code,
        DateTimeOffset ExpiresAt) : CodeRotation;

    /// <summary>The code is still valid.</summary>
    /// <param name="ExpiresAt">Current expiry.</param>
    public sealed record Waiting(DateTimeOffset ExpiresAt) : CodeRotation;

    /// <summary>The peer is missing or no longer pending.</summary>
    public sealed record Skipped : CodeRotation;
}

/// <summary>Web side of pairing: list, approve with a code, revoke, block, and deep links.</summary>
public interface IChannelPairingAdmin
{
    /// <summary>Lists peers without raw ids or code hashes.</summary>
    public ValueTask<IReadOnlyList<PeerSummary>> ListPeersAsync(CancellationToken ct);

    /// <summary>Approves a pending peer when the typed code matches.</summary>
    public ValueTask<PeerApprovalResult> ApproveWithCodeAsync(
        Guid peerId,
        string code,
        Guid userId,
        Guid deviceId,
        CancellationToken ct);

    /// <summary>Unlinks an approved peer.</summary>
    public ValueTask<PeerMutationResult> RevokeAsync(Guid peerId, Guid userId, CancellationToken ct);

    /// <summary>Blocks a peer. Further messages stay silent.</summary>
    public ValueTask<PeerMutationResult> BlockAsync(Guid peerId, CancellationToken ct);

    /// <summary>Creates a single-use deep-link token.</summary>
    public ValueTask<IssuedDeepLink> IssueDeepLinkAsync(Guid userId, ChannelId channel, CancellationToken ct);

    /// <summary>Replaces an expired pending code. Called by the expiry job.</summary>
    public ValueTask<CodeRotation> RotateExpiredAsync(Guid peerId, CancellationToken ct);
}
