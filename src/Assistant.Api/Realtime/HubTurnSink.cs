using System.Diagnostics.CodeAnalysis;
using Assistant.Api.Channels;
using Assistant.Api.Protocol;
using Assistant.Api.Speech;
using Assistant.Core.Conversations;
using Microsoft.AspNetCore.SignalR;

namespace Assistant.Api.Realtime;

/// <summary>
/// The hub path extracted from <see cref="TurnRunner"/>: SignalR delivery, avatar cues,
/// and the voice pipeline. An empty connection id keeps the turn (bus events) and
/// skips the client, which is how confirmation expiry behaves today.
/// </summary>
internal sealed class HubTurnSink(
    IHubContext<AssistantHub> hub,
    TurnConnections connections,
    VoiceTurnPipeline voice,
    TimeProvider clock,
    ILogger log,
    string connectionId) : IInternalTurnSink
{
    private readonly bool _deliverToClient = !string.IsNullOrEmpty(connectionId);
    private Guid? _turnId;
    private Guid? _finishedMessageId;
    private bool _sendFailureLogged;
    private IDisposable? _origin;

    public async ValueTask HandleAsync(TurnUpdate update, TurnRequest request, CancellationToken ct)
    {
        if (update is TurnAccepted accepted)
        {
            _turnId = accepted.TurnId;
            if (_deliverToClient)
            {
                _origin = connections.Register(
                    new TurnOrigin(
                        accepted.TurnId,
                        request.ConversationId,
                        request.UserId,
                        connectionId));
                voice.AttachTurn(
                    accepted.TurnId,
                    request.ConversationId,
                    request.UserId,
                    connectionId,
                    ct);
            }
        }

        if (_turnId is Guid voiceTurnId)
        {
            switch (update)
            {
                case TurnToken token:
                    // Do not await TTS — enqueue and keep streaming tokens (E4 / D4).
                    await voice.OnTokenAsync(voiceTurnId, token.Text, ct);
                    break;
                case TurnToolStarted:
                case TurnConfirmationRequired:
                    await voice.FlushAsync(voiceTurnId, ct);
                    break;
                case TurnFinished finished:
                    await voice.FlushAsync(voiceTurnId, ct);
                    _finishedMessageId = finished.MessageId;
                    voice.MarkStreamingEnded(voiceTurnId);
                    break;
            }
        }

        if (update is TurnCharacterCue characterCue)
        {
            if (_deliverToClient)
            {
                var cueCorrelation = _turnId?.ToString("D");
                var cueEnvelope = AvatarCues.FromCharacterCue(
                    characterCue.Expression,
                    cueCorrelation,
                    clock);
                if (cueEnvelope is not null)
                {
                    _sendFailureLogged = await SendOrLogAsync(
                        _turnId ?? Guid.Empty,
                        cueEnvelope,
                        _sendFailureLogged,
                        ct);
                }

                var gestureEnvelope = AvatarCues.Gesture(
                    characterCue.Gesture,
                    cueCorrelation,
                    clock);
                if (gestureEnvelope is not null)
                {
                    _sendFailureLogged = await SendOrLogAsync(
                        _turnId ?? Guid.Empty,
                        gestureEnvelope,
                        _sendFailureLogged,
                        ct);
                }
            }

            return;
        }

        if (!_deliverToClient)
        {
            return;
        }

        var envelope = update is TurnAccepted turnAccepted
            ? TurnUpdateMapper.TurnStarted(turnAccepted, request.ConversationId, clock)
            : TurnUpdateMapper.ToEnvelope(update, request.ConversationId, _turnId, clock);

        _sendFailureLogged = await SendOrLogAsync(
            _turnId ?? Guid.Empty,
            envelope,
            _sendFailureLogged,
            ct);

        if (update is TurnAccepted)
        {
            var cueCorrelation = _turnId?.ToString("D");
            foreach (var cue in AvatarCues.ThinkingPair(cueCorrelation, clock))
            {
                _sendFailureLogged = await SendOrLogAsync(
                    _turnId ?? Guid.Empty,
                    cue,
                    _sendFailureLogged,
                    ct);
            }
        }
        else if (update is TurnFinished)
        {
            var cueCorrelation = _turnId?.ToString("D");
            foreach (var cue in AvatarCues.IdlePair(cueCorrelation, clock))
            {
                _sendFailureLogged = await SendOrLogAsync(
                    _turnId ?? Guid.Empty,
                    cue,
                    _sendFailureLogged,
                    ct);
            }
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "TTS completion must not fail the detached turn.")]
    public async ValueTask CompleteAsync(CancellationToken ct)
    {
        if (_turnId is Guid completedTurnId)
        {
            try
            {
                await voice.CompleteAsync(completedTurnId, _finishedMessageId, ct);
            }
            catch (Exception e)
            {
                ApiLog.TtsFailed(log, e, completedTurnId);
            }
        }

        _origin?.Dispose();
        _origin = null;
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A dropped SignalR client must not fail the detached turn.")]
    private async Task<bool> SendOrLogAsync(
        Guid turnId,
        MessageEnvelope envelope,
        bool alreadyLogged,
        CancellationToken ct)
    {
        try
        {
            await hub.Clients.Client(connectionId).SendAsync("Receive", envelope, ct);
        }
        catch (Exception e)
        {
            if (!alreadyLogged)
            {
                ApiLog.SendFailed(log, e, turnId, connectionId);
            }

            return true;
        }

        return alreadyLogged;
    }
}
