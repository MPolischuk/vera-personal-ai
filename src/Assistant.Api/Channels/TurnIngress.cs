using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Assistant.Abstractions.Channels;
using Assistant.Core.Confirmations;
using Assistant.Core.Conversations;
using Assistant.Core.Personality;
using Assistant.Core.Tools;
using Assistant.Api.Realtime;
using ChannelTurnOrigin = Assistant.Abstractions.Channels.TurnOrigin;

namespace Assistant.Api.Channels;

[SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Transcription failures are returned as stt_failed. The audio and the transcript are not logged.")]
internal sealed class TurnIngress(
    IChannelPeerDirectory peers,
    IConversationStore conversations,
    IPendingConfirmationStore confirmations,
    IPersonalityService personality,
    IInboundAudioTranscriber transcriber,
    IActiveTurns activeTurns,
    TurnRunner runner,
    TimeProvider clock,
    IChannelAuditSink audit,
    ILogger<TurnIngress> log) : ITurnIngress
{
    public async ValueTask<TurnIngressAck> SubmitAsync(
        ChannelTurnRequest request, ITurnSink sink, CancellationToken ct)
    {
        var peer = await FindPairedPeerAsync(request.Channel, request.PeerId, request.UserId, ct);
        if (peer is null)
        {
            return Reject(request.Channel, request.PeerId, "peer_not_paired");
        }

        var conversationId = await EnsureConversationAsync(peer, request.UserId, request.Channel, ct);

        var open = await confirmations.FindOpenAsync(conversationId, request.UserId, ct);
        if (open is not null && open.ExpiresAt > clock.GetUtcNow())
        {
            return Reject(request.Channel, request.PeerId, "confirmation_pending");
        }

        string message;
        try
        {
            message = await ComposeUserMessageAsync(request, conversationId, sink, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            ApiLog.InboundAudioFailed(log, e, request.PeerId);
            return Reject(request.Channel, request.PeerId, "stt_failed");
        }

        if (message.Length == 0)
        {
            var code = request.Parts.Count > 0 && request.Parts.All(static p => p is InboundPart.Unsupported)
                ? "unsupported_content"
                : "empty_message";
            return Reject(request.Channel, request.PeerId, code);
        }

        ApiLog.ChannelTurnAccepted(log, request.Channel.Value, request.PeerId, conversationId);
        await runner.Start(
            new TurnRequest(conversationId, request.UserId, message),
            new ChannelTurnOrigin(request.Channel, request.PeerId),
            new ChannelTurnSinkAdapter(sink));
        return new TurnIngressAck(true, null);
    }

    public async ValueTask<TurnIngressAck> ResolveConfirmationAsync(
        ChannelConfirmationResponse response, ITurnSink sink, CancellationToken ct)
    {
        var peer = await FindPairedPeerAsync(response.Channel, response.PeerId, response.UserId, ct);
        if (peer is null)
        {
            return Reject(response.Channel, response.PeerId, "peer_not_paired");
        }

        var pending = await confirmations.FindOwnedAsync(response.ConfirmationId, response.UserId, ct);
        if (pending is null
            || pending.ExpiresAt <= clock.GetUtcNow()
            || pending.ConversationId != peer.ConversationId)
        {
            return Reject(response.Channel, response.PeerId, TurnErrorCodes.ConfirmationInvalid);
        }

        if (pending.ResolvedAt is not null)
        {
            return Reject(response.Channel, response.PeerId, ConfirmationAlreadyResolved);
        }

        var probe = new ProbeSink(sink);
        await runner.ResumeAsync(
            TurnRequest.Resume(pending.ConversationId, response.UserId, pending.Id, response.Approved),
            new ChannelTurnOrigin(response.Channel, response.PeerId),
            new ChannelTurnSinkAdapter(probe));
        if (probe.Result is { Outcome: ChannelTurnOutcome.Failed, ErrorCode: TurnErrorCodes.ConfirmationInvalid })
        {
            return Reject(response.Channel, response.PeerId, ConfirmationAlreadyResolved);
        }

        await audit.RecordAsync(
            new ChannelAuditEntry(
                "channel.confirmation.resolved",
                response.Channel,
                response.PeerId,
                pending.ConversationId,
                response.ExternalMessageId,
                response.Approved ? "approved" : "denied"),
            ct);
        return new TurnIngressAck(true, null);
    }

    public async ValueTask<Guid> StartNewConversationAsync(
        ChannelId channel, Guid peerId, Guid userId, CancellationToken ct)
    {
        var peer = await FindPairedPeerAsync(channel, peerId, userId, ct);
        if (peer is null)
        {
            throw new InvalidOperationException("The peer is not paired.");
        }

        var created = await conversations.CreateAsync(userId, await FormatTitleAsync(channel, ct), ct);
        await peers.SetConversationAsync(peer.Id, created.Id, ct);
        return created.Id;
    }

    public async ValueTask<TurnIngressAck> InterruptAsync(
        ChannelId channel, Guid peerId, Guid userId, CancellationToken ct)
    {
        var peer = await FindPairedPeerAsync(channel, peerId, userId, ct);
        if (peer is null)
        {
            return Reject(channel, peerId, "peer_not_paired");
        }

        if (peer.ConversationId is not Guid conversationId || !activeTurns.TryInterrupt(conversationId))
        {
            return Reject(channel, peerId, "no_active_turn");
        }

        return new TurnIngressAck(true, null);
    }

    private async ValueTask<ChannelPeer?> FindPairedPeerAsync(
        ChannelId channel, Guid peerId, Guid userId, CancellationToken ct)
    {
        var peer = await peers.FindApprovedForUserAsync(channel, userId, ct);
        if (peer is null || peer.Id != peerId || peer.UserId != userId)
        {
            return null;
        }

        return peer;
    }

    private async ValueTask<Guid> EnsureConversationAsync(
        ChannelPeer peer, Guid userId, ChannelId channel, CancellationToken ct)
    {
        if (peer.ConversationId is Guid existing
            && await conversations.FindAsync(existing, userId, ct) is not null)
        {
            return existing;
        }

        var created = await conversations.CreateAsync(userId, await FormatTitleAsync(channel, ct), ct);
        await peers.SetConversationAsync(peer.Id, created.Id, ct);
        return created.Id;
    }

    private async ValueTask<string> FormatTitleAsync(ChannelId channel, CancellationToken ct)
    {
        var profile = (await personality.GetActiveAsync(ct)).Profile;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(profile.TimeZone);
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone);
        var label = channel == ChannelId.Telegram ? "Telegram"
            : channel == ChannelId.WhatsApp ? "WhatsApp"
            : channel.Value;
        return $"{label} · {local.ToString("dd/MM", CultureInfo.InvariantCulture)}";
    }

    private async ValueTask<string> ComposeUserMessageAsync(
        ChannelTurnRequest request, Guid conversationId, ITurnSink sink, CancellationToken ct)
    {
        var pieces = new List<string>();
        foreach (var part in request.Parts)
        {
            switch (part)
            {
                case InboundPart.Text text when text.Value.Length > 0:
                    pieces.Add(Render(request.Channel, text.Trust, text.OriginLabel, text.Value));
                    break;

                case InboundPart.Audio audio:
                    var transcript = await transcriber.TranscribeAsync(
                        audio.Data, audio.MimeType, audio.Duration, request.UserId, conversationId, ct);
                    await sink.OnTranscriptionAsync(transcript, ct);
                    if (transcript.Length > 0)
                    {
                        pieces.Add(Render(request.Channel, audio.Trust, audio.OriginLabel, transcript));
                    }

                    break;

                case InboundPart.Unsupported:
                    break;
            }
        }

        return string.Join('\n', pieces);
    }

    private static string Render(ChannelId channel, ContentTrust trust, string? origin, string text)
        => trust == ContentTrust.OwnerAuthored
            ? text
            : ExternalContent.Wrap(channel.Value, "forwarded_message", origin, text);

    /// <summary>Second tap of a confirmation the claim already closed.</summary>
    internal const string ConfirmationAlreadyResolved = "confirmation_already_resolved";

    private TurnIngressAck Reject(ChannelId channel, Guid peerId, string code)
    {
        ApiLog.ChannelTurnRejected(log, channel.Value, peerId, code);
        return new TurnIngressAck(false, code);
    }

    private sealed class ProbeSink(ITurnSink inner) : ITurnSink
    {
        public ChannelTurnResult? Result { get; private set; }

        public ValueTask OnTurnStartedAsync(Guid turnId, CancellationToken ct)
            => inner.OnTurnStartedAsync(turnId, ct);

        public ValueTask OnTranscriptionAsync(string text, CancellationToken ct)
            => inner.OnTranscriptionAsync(text, ct);

        public ValueTask OnTextDeltaAsync(string text, CancellationToken ct)
            => inner.OnTextDeltaAsync(text, ct);

        public ValueTask OnToolActivityAsync(string toolName, bool completed, CancellationToken ct)
            => inner.OnToolActivityAsync(toolName, completed, ct);

        public ValueTask OnConfirmationRequestedAsync(ChannelConfirmationPrompt prompt, CancellationToken ct)
            => inner.OnConfirmationRequestedAsync(prompt, ct);

        public ValueTask OnTurnFinishedAsync(ChannelTurnResult result, CancellationToken ct)
        {
            Result = result;
            return inner.OnTurnFinishedAsync(result, ct);
        }
    }
}
