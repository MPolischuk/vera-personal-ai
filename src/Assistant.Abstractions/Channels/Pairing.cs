namespace Assistant.Abstractions.Channels;

/// <summary>
/// Pairing decision. A closed record hierarchy.
/// </summary>
public abstract record PairingDecision
{
    /// <summary>Approved peer; interaction may continue.</summary>
    /// <param name="Peer">Approved peer.</param>
    public sealed record Approved(ChannelPeer Peer) : PairingDecision;

    /// <summary>A new code was generated; tell the sender.</summary>
    /// <param name="Code">Pairing code (format K7M2-QX4P).</param>
    /// <param name="ExpiresAt">When the code expires.</param>
    public sealed record NewCode(string Code, DateTimeOffset ExpiresAt) : PairingDecision;

    /// <summary>Still pending; remind the code when that applies.</summary>
    /// <param name="RemindCode">Code to remind (null means do not repeat, because of the rate limit).</param>
    public sealed record StillPending(string? RemindCode) : PairingDecision;

    /// <summary>Permanent silence (blocked / flood).</summary>
    /// <param name="Reason">Why the sender is silenced.</param>
    public sealed record Silent(string Reason) : PairingDecision;
}

/// <summary>
/// Pairing service for channel peers.
/// </summary>
public interface IPairingService
{
    /// <summary>
    /// Evaluates an interaction attempt from a peer.
    /// </summary>
    /// <param name="channel">Origin channel.</param>
    /// <param name="externalPeerId">External peer id.</param>
    /// <param name="displayName">Visible name of the peer.</param>
    /// <param name="username">Username without <c>@</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Pairing decision.</returns>
    /// <remarks>
    /// Approved continues; unknown becomes pending plus a code; pending reminds the code
    /// (with a rate limit); revoked pairs from scratch; blocked stays silent.
    /// </remarks>
    public ValueTask<PairingDecision> EvaluateAsync(
        ChannelId channel,
        string externalPeerId,
        string? displayName,
        string? username,
        CancellationToken ct);

    /// <summary>
    /// Redeems a deep-link token and approves the peer directly.
    /// </summary>
    /// <param name="channel">Origin channel.</param>
    /// <param name="externalPeerId">External peer id.</param>
    /// <param name="displayName">Visible name of the peer.</param>
    /// <param name="username">Username without <c>@</c>.</param>
    /// <param name="token">Single-use deep-link token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Pairing decision.</returns>
    public ValueTask<PairingDecision> RedeemDeepLinkAsync(
        ChannelId channel,
        string externalPeerId,
        string? displayName,
        string? username,
        string token,
        CancellationToken ct);

    /// <summary>
    /// Records that the current code was just sent, so the next messages stay inside the reminder interval.
    /// Called only after the send succeeds. A failed send leaves the stamp unset and the retry can repeat the code.
    /// </summary>
    public ValueTask NoteReminderSentAsync(ChannelId channel, string externalPeerId, CancellationToken ct);
}
