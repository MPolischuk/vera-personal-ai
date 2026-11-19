using Assistant.Abstractions.Channels;
using Assistant.Abstractions.Scheduling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Channels.Pairing;

/// <summary>Pairing state machine shared by Telegram and later channels.</summary>
internal sealed class PairingService(
    IChannelPairingStore store,
    IChannelAuditSink audit,
    IJobScheduler scheduler,
    PairingCodeHold codes,
    TimeProvider clock,
    IOptions<PairingOptions> options,
    ILogger<PairingService> logger) : IPairingService, IChannelPairingAdmin
{
    private const int DisplayNameMax = 200;
    private const int UsernameMax = 100;

    public async ValueTask<PairingDecision> EvaluateAsync(
        ChannelId channel,
        string externalPeerId,
        string? displayName,
        string? username,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var name = Trim(displayName, DisplayNameMax, stripAt: false);
        var handle = Trim(username, UsernameMax, stripAt: true);
        var peer = await store.FindAsync(channel, externalPeerId, ct);
        var peerRef = ChannelPeerLogRef.Format(channel, externalPeerId);

        if (peer is null)
        {
            var pending = await store.CountAsync(channel, ChannelPeerStatus.Pending, ct);
            if (pending >= Math.Max(1, options.Value.MaxPendingPeers))
            {
                await AuditAsync(ChannelAuditActions.Flood, channel, null, "flood", null, ct);
                PairingLog.Decided(logger, PairingReasons.Flood, peerRef);
                return new PairingDecision.Silent(PairingReasons.Flood);
            }

            var issued = IssueCode(now);
            var created = await store.InsertAsync(
                new NewChannelPeer(
                    channel,
                    externalPeerId,
                    name,
                    handle,
                    ChannelPeerStatus.Pending,
                    null,
                    issued.Hash,
                    issued.ExpiresAt,
                    null,
                    now),
                ct);
            codes.Remember(created.Id, issued.Display, issued.ExpiresAt);
            await ScheduleExpiryAsync(created.Id, issued.ExpiresAt, null, ct);
            await AuditAsync(ChannelAuditActions.Pending, channel, created.Id, "code_issued", null, ct);
            PairingLog.Decided(logger, "new_code", peerRef);
            return new PairingDecision.NewCode(issued.Display, issued.ExpiresAt);
        }

        if (peer.Status == ChannelPeerStatus.Blocked)
        {
            PairingLog.Decided(logger, PairingReasons.Blocked, peerRef);
            return new PairingDecision.Silent(PairingReasons.Blocked);
        }

        if (peer.Status == ChannelPeerStatus.Approved)
        {
            await store.UpdateProfileAsync(peer.Id, name ?? peer.DisplayName, handle ?? peer.Username, now, ct);
            PairingLog.Decided(logger, "approved", peerRef);
            return new PairingDecision.Approved(ToChannelPeer(peer, name, handle));
        }

        if (peer.Status == ChannelPeerStatus.Revoked || CodeExpired(peer, now))
        {
            var decision = await ReplaceCodeAsync(peer, name, handle, now, peerRef, ct);
            return decision;
        }

        await store.UpdateProfileAsync(peer.Id, name ?? peer.DisplayName, handle ?? peer.Username, now, ct);
        var interval = options.Value.PairingReminderInterval <= TimeSpan.Zero
            ? TimeSpan.FromMinutes(10)
            : options.Value.PairingReminderInterval;
        var due = peer.LastCodeReminderAt is null || now - peer.LastCodeReminderAt.Value >= interval;
        if (!due)
        {
            PairingLog.Decided(logger, "pending_quiet", peerRef);
            return new PairingDecision.StillPending(null);
        }

        var recalled = codes.Recall(peer.Id, now);
        if (recalled is null)
        {
            return await ReplaceCodeAsync(peer, name, handle, now, peerRef, ct);
        }

        PairingLog.Decided(logger, "reminder", peerRef);
        return new PairingDecision.StillPending(recalled);
    }

    public async ValueTask<PairingDecision> RedeemDeepLinkAsync(
        ChannelId channel,
        string externalPeerId,
        string? displayName,
        string? username,
        string token,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var name = Trim(displayName, DisplayNameMax, stripAt: false);
        var handle = Trim(username, UsernameMax, stripAt: true);
        var peerRef = ChannelPeerLogRef.Format(channel, externalPeerId);
        var peer = await store.FindAsync(channel, externalPeerId, ct);
        if (peer?.Status == ChannelPeerStatus.Blocked)
        {
            PairingLog.Decided(logger, PairingReasons.Blocked, peerRef);
            return new PairingDecision.Silent(PairingReasons.Blocked);
        }

        if (peer?.Status == ChannelPeerStatus.Approved)
        {
            PairingLog.Decided(logger, "approved", peerRef);
            return new PairingDecision.Approved(ToChannelPeer(peer, name, handle));
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return new PairingDecision.Silent(PairingReasons.Invalid);
        }

        var hash = SecretDigest.Sha256Hex(token.Trim());
        var lookup = await store.FindLinkTokenAsync(hash, ct);
        if (lookup is not LinkTokenLookup.Found found || found.Channel != channel)
        {
            PairingLog.Decided(logger, PairingReasons.Invalid, peerRef);
            return new PairingDecision.Silent(PairingReasons.Invalid);
        }

        if (found.UsedAt is not null)
        {
            PairingLog.Decided(logger, PairingReasons.Used, peerRef);
            return new PairingDecision.Silent(PairingReasons.Used);
        }

        if (found.ExpiresAt <= now)
        {
            PairingLog.Decided(logger, PairingReasons.Expired, peerRef);
            return new PairingDecision.Silent(PairingReasons.Expired);
        }

        var approved = await store.CountAsync(channel, ChannelPeerStatus.Approved, ct);
        if (approved >= Math.Max(1, options.Value.MaxApprovedPeersPerChannel))
        {
            PairingLog.Decided(logger, PairingReasons.Limit, peerRef);
            return new PairingDecision.Silent(PairingReasons.Limit);
        }

        if (!await store.TryConsumeLinkTokenAsync(hash, now, ct))
        {
            PairingLog.Decided(logger, PairingReasons.Used, peerRef);
            return new PairingDecision.Silent(PairingReasons.Used);
        }

        var pending = peer ?? await store.InsertAsync(
            new NewChannelPeer(channel, externalPeerId, name, handle, ChannelPeerStatus.Pending, null, null, null, null, now),
            ct);
        var result = await store.TryApproveAsync(
            pending.Id,
            found.UserId,
            null,
            Math.Max(1, options.Value.MaxApprovedPeersPerChannel),
            now,
            ct);
        if (result is not ApproveWriteResult.Approved approvedWrite)
        {
            PairingLog.Decided(logger, PairingReasons.Limit, peerRef);
            return new PairingDecision.Silent(PairingReasons.Limit);
        }

        var saved = approvedWrite.Peer;
        await CancelExpiryAsync(pending, ct);
        codes.Forget(pending.Id);

        await AuditAsync(ChannelAuditActions.Approved, channel, saved.Id, "deep_link", null, ct);
        PairingLog.Decided(logger, PairingReasons.Welcomed, peerRef);
        return new PairingDecision.Silent(PairingReasons.Welcomed);
    }

    public async ValueTask NoteReminderSentAsync(ChannelId channel, string externalPeerId, CancellationToken ct)
    {
        var peer = await store.FindAsync(channel, externalPeerId, ct);
        if (peer is null)
        {
            return;
        }

        await store.SetReminderAsync(peer.Id, clock.GetUtcNow(), ct);
    }

    public async ValueTask<IReadOnlyList<PeerSummary>> ListPeersAsync(CancellationToken ct)
    {
        var peers = await store.ListAsync(ct);
        return peers.Select(ToSummary).ToArray();
    }

    public async ValueTask<PeerApprovalResult> ApproveWithCodeAsync(
        Guid peerId,
        string code,
        Guid userId,
        Guid deviceId,
        CancellationToken ct)
    {
        var peer = await store.FindByIdAsync(peerId, ct);
        if (peer is null || peer.Status == ChannelPeerStatus.Revoked)
        {
            return new PeerApprovalResult.NotFound();
        }

        if (peer.Status == ChannelPeerStatus.Blocked)
        {
            return new PeerApprovalResult.Blocked();
        }

        if (peer.Status == ChannelPeerStatus.Approved)
        {
            return peer.UserId == userId
                ? new PeerApprovalResult.Approved(ToChannelPeer(peer, null, null))
                : new PeerApprovalResult.LimitReached();
        }

        var approvedCount = await store.CountAsync(peer.Channel, ChannelPeerStatus.Approved, ct);
        if (approvedCount >= Math.Max(1, options.Value.MaxApprovedPeersPerChannel))
        {
            return new PeerApprovalResult.LimitReached();
        }

        var now = clock.GetUtcNow();
        if (CodeExpired(peer, now))
        {
            return new PeerApprovalResult.Expired();
        }

        if (!PairingCode.TryNormalize(code, out var normalized)
            || peer.CodeHash is null
            || !SecretDigest.FixedTimeEquals(peer.CodeHash, SecretDigest.Sha256Hex(normalized)))
        {
            var blocked = await RegisterFailedAttemptAsync(peer, ct);
            return blocked ? new PeerApprovalResult.Blocked() : new PeerApprovalResult.InvalidCode();
        }

        var result = await store.TryApproveAsync(
            peer.Id,
            userId,
            deviceId,
            Math.Max(1, options.Value.MaxApprovedPeersPerChannel),
            now,
            ct);
        if (result is ApproveWriteResult.LimitReached)
        {
            return new PeerApprovalResult.LimitReached();
        }

        if (result is not ApproveWriteResult.Approved approved)
        {
            return new PeerApprovalResult.NotFound();
        }

        codes.Forget(peer.Id);
        await CancelExpiryAsync(peer, ct);
        await AuditAsync(ChannelAuditActions.Approved, peer.Channel, peer.Id, "code", deviceId, ct);
        var approvedRef = ChannelPeerLogRef.Format(peer.Channel, peer.ExternalPeerId);
        PairingLog.Decided(logger, "approved", approvedRef);
        return new PeerApprovalResult.Approved(ToChannelPeer(approved.Peer, null, null));
    }

    public async ValueTask<PeerMutationResult> RevokeAsync(Guid peerId, Guid userId, CancellationToken ct)
    {
        var peer = await store.FindByIdAsync(peerId, ct);
        if (peer is null || peer.Status != ChannelPeerStatus.Approved || peer.UserId != userId)
        {
            return new PeerMutationResult.NotFound();
        }

        await store.MarkRevokedAsync(peer.Id, clock.GetUtcNow(), ct);
        codes.Forget(peer.Id);
        await CancelExpiryAsync(peer, ct);
        await AuditAsync(ChannelAuditActions.Revoked, peer.Channel, peer.Id, "revoked", null, ct);
        return new PeerMutationResult.Ok();
    }

    public async ValueTask<PeerMutationResult> BlockAsync(Guid peerId, CancellationToken ct)
    {
        var peer = await store.FindByIdAsync(peerId, ct);
        if (peer is null)
        {
            return new PeerMutationResult.NotFound();
        }

        if (peer.Status == ChannelPeerStatus.Blocked)
        {
            return new PeerMutationResult.Ok();
        }

        await store.MarkBlockedAsync(peer.Id, ct);
        codes.Forget(peer.Id);
        await CancelExpiryAsync(peer, ct);
        await AuditAsync(ChannelAuditActions.Blocked, peer.Channel, peer.Id, "blocked", null, ct);
        return new PeerMutationResult.Ok();
    }

    public async ValueTask<IssuedDeepLink> IssueDeepLinkAsync(Guid userId, ChannelId channel, CancellationToken ct)
    {
        Span<byte> bytes = stackalloc byte[24];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        var token = System.Buffers.Text.Base64Url.EncodeToString(bytes);
        var now = clock.GetUtcNow();
        var ttl = options.Value.DeepLinkTokenTtl <= TimeSpan.Zero
            ? TimeSpan.FromMinutes(10)
            : options.Value.DeepLinkTokenTtl;
        var expiresAt = now.Add(ttl);
        await store.InsertLinkTokenAsync(SecretDigest.Sha256Hex(token), userId, channel, expiresAt, ct);
        return new IssuedDeepLink(token, expiresAt);
    }

    public async ValueTask<CodeRotation> RotateExpiredAsync(Guid peerId, CancellationToken ct)
    {
        var peer = await store.FindByIdAsync(peerId, ct);
        if (peer is null || peer.Status != ChannelPeerStatus.Pending)
        {
            return new CodeRotation.Skipped();
        }

        var now = clock.GetUtcNow();
        if (!CodeExpired(peer, now))
        {
            return new CodeRotation.Waiting(peer.CodeExpiresAt!.Value);
        }

        var issued = IssueCode(now);
        await store.SetPendingCodeAsync(peer.Id, issued.Hash, issued.ExpiresAt, peer.DisplayName, peer.Username, now, ct);
        codes.Remember(peer.Id, issued.Display, issued.ExpiresAt);
        await ScheduleExpiryAsync(peer.Id, issued.ExpiresAt, peer.CodeExpiresAt, ct);
        var rotatedRef = ChannelPeerLogRef.Format(peer.Channel, peer.ExternalPeerId);
        PairingLog.Decided(logger, "rotated", rotatedRef);
        return new CodeRotation.Rotated(peer.Channel, peer.ExternalPeerId, issued.Display, issued.ExpiresAt);
    }

    private async ValueTask<PairingDecision> ReplaceCodeAsync(
        StoredChannelPeer peer,
        string? name,
        string? handle,
        DateTimeOffset now,
        string peerRef,
        CancellationToken ct)
    {
        var issued = IssueCode(now);
        await store.SetPendingCodeAsync(
            peer.Id,
            issued.Hash,
            issued.ExpiresAt,
            name ?? peer.DisplayName,
            handle ?? peer.Username,
            now,
            ct);
        codes.Remember(peer.Id, issued.Display, issued.ExpiresAt);
        await ScheduleExpiryAsync(peer.Id, issued.ExpiresAt, peer.CodeExpiresAt, ct);
        await AuditAsync(ChannelAuditActions.Pending, peer.Channel, peer.Id, "code_issued", null, ct);
        PairingLog.Decided(logger, "new_code", peerRef);
        return new PairingDecision.NewCode(issued.Display, issued.ExpiresAt);
    }

    private async ValueTask<bool> RegisterFailedAttemptAsync(StoredChannelPeer peer, CancellationToken ct)
    {
        var attempts = await store.IncrementAttemptsAsync(peer.Id, ct);
        await AuditAsync(ChannelAuditActions.FailedAttempt, peer.Channel, peer.Id, "invalid_code", null, ct);
        if (attempts < Math.Max(1, options.Value.MaxPairingAttempts))
        {
            return false;
        }

        await store.MarkBlockedAsync(peer.Id, ct);
        codes.Forget(peer.Id);
        await CancelExpiryAsync(peer, ct);
        await AuditAsync(ChannelAuditActions.Blocked, peer.Channel, peer.Id, "attempts", null, ct);
        return true;
    }

    private IssuedCode IssueCode(DateTimeOffset now)
    {
        var display = PairingCode.Create();
        if (!PairingCode.TryNormalize(display, out var normalized))
        {
            throw new InvalidOperationException("The pairing code generator produced a code outside the alphabet.");
        }

        var ttl = options.Value.PairingCodeTtl <= TimeSpan.Zero
            ? TimeSpan.FromHours(1)
            : options.Value.PairingCodeTtl;
        return new IssuedCode(display, SecretDigest.Sha256Hex(normalized), now.Add(ttl));
    }

    private async ValueTask ScheduleExpiryAsync(
        Guid peerId,
        DateTimeOffset expiresAt,
        DateTimeOffset? previousExpiresAt,
        CancellationToken ct)
    {
        if (previousExpiresAt is not null)
        {
            await scheduler.CancelAsync(PairingExpireSchedule.Key(peerId, previousExpiresAt.Value), ct);
        }

        await scheduler.ScheduleAsync(
            new JobSpec(
                PairingExpireSchedule.Kind,
                expiresAt,
                PairingExpireSchedule.Payload(peerId),
                PairingExpireSchedule.Key(peerId, expiresAt)),
            ct);
    }

    private async ValueTask CancelExpiryAsync(StoredChannelPeer peer, CancellationToken ct)
    {
        if (peer.CodeExpiresAt is null)
        {
            return;
        }

        await scheduler.CancelAsync(PairingExpireSchedule.Key(peer.Id, peer.CodeExpiresAt.Value), ct);
    }

    private ValueTask AuditAsync(
        string action,
        ChannelId channel,
        Guid? peerId,
        string outcome,
        Guid? deviceId,
        CancellationToken ct)
        => audit.RecordAsync(
            new ChannelAuditEntry(action, channel, peerId, null, null, outcome, deviceId),
            ct);

    private static bool CodeExpired(StoredChannelPeer peer, DateTimeOffset now)
        => peer.CodeExpiresAt is null || peer.CodeExpiresAt <= now;

    private static PeerSummary ToSummary(StoredChannelPeer peer)
        => new(
            peer.Id,
            peer.Channel,
            peer.Status,
            peer.DisplayName,
            peer.Username,
            Mask(peer.ExternalPeerId),
            peer.CreatedAt,
            peer.LastInboundAt);

    internal static string Mask(string externalPeerId)
    {
        if (string.IsNullOrEmpty(externalPeerId))
        {
            return "…";
        }

        var tail = externalPeerId.Length <= 4 ? externalPeerId : externalPeerId[^4..];
        return "…" + tail;
    }

    private static ChannelPeer ToChannelPeer(StoredChannelPeer peer, string? displayName, string? username)
        => new(
            peer.Id,
            peer.Channel,
            peer.ExternalPeerId,
            peer.UserId,
            peer.Status,
            displayName ?? peer.DisplayName,
            peer.ConversationId,
            peer.DeliveryBlocked,
            peer.LastInboundAt);

    private static string? Trim(string? value, int max, bool stripAt)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (stripAt)
        {
            trimmed = trimmed.TrimStart('@');
        }

        if (trimmed.Length == 0)
        {
            return null;
        }

        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private readonly record struct IssuedCode(string Display, string Hash, DateTimeOffset ExpiresAt);
}

internal static class ChannelAuditActions
{
    public const string Pending = "channel.peer.pending";

    public const string Approved = "channel.peer.approved";

    public const string Revoked = "channel.peer.revoked";

    public const string Blocked = "channel.peer.blocked";

    public const string FailedAttempt = "channel.pairing.failed_attempt";

    public const string Flood = "channel.pairing.flood";
}
