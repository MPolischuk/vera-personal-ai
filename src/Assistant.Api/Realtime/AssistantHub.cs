using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Assistant.Abstractions.Notifications;
using Assistant.Api.Authentication;
using Assistant.Api.Channels;
using Assistant.Api.Protocol;
using Assistant.Channels.Delivery;
using Assistant.Api.Speech;
using Assistant.Core.Confirmations;
using Assistant.Core.Conversations;
using Assistant.Core.Personality;
using Assistant.Core.Speech;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Channels;

namespace Assistant.Api.Realtime;

[Authorize]
internal sealed class AssistantHub(
    TurnRunner runner,
    TurnConnections connections,
    IConversationStore conversations,
    IPendingConfirmationStore confirmations,
    IActiveTurns activeTurns,
    VoiceTurnPipeline voice,
    VoiceSessionRegistry sttSessions,
    IServiceProvider services,
    IPersonalityService personality,
    IHubContext<AssistantHub> hubContext,
    HubPresence presence,
    IDeliveryAckWait acks,
    INotificationActionService notificationActions,
    TimeProvider clock,
    ILogger<AssistantHub> log) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User!.UserId();
        presence.Connected(userId, Context.ConnectionId);
        await Groups.AddToGroupAsync(
            Context.ConnectionId, UserGroups.For(userId), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        presence.Disconnected(Context.User!.UserId(), Context.ConnectionId);
        if (sttSessions.TryRemove(Context.ConnectionId, out var session) && session is not null)
        {
            session.Lifetime.Cancel();
            session.Channel.Writer.TryComplete();
        }

        return base.OnDisconnectedAsync(exception);
    }

    public Task Send(MessageEnvelope envelope)
        => envelope switch
        {
            { V: not MessageEnvelope.CurrentVersion } => ErrorAsync(
                envelope.CorrelationId,
                ProtocolErrorCodes.UnsupportedProtocolVersion,
                "Unsupported protocol version."),
            { Type: ProtocolMessageTypes.UserMessage } => StartTurnAsync(envelope),
            { Type: ProtocolMessageTypes.UserConfirmationResponse } => ResumeConfirmationAsync(envelope),
            { Type: ProtocolMessageTypes.UserInterrupt } => InterruptAsync(envelope),
            { Type: ProtocolMessageTypes.UserAudioEnd } => AudioEndAsync(envelope),
            { Type: ProtocolMessageTypes.NotificationAck } => AckNotificationAsync(envelope),
            { Type: ProtocolMessageTypes.NotificationAction } => ActOnNotificationAsync(envelope),
            _ => ErrorAsync(
                envelope.CorrelationId,
                ProtocolErrorCodes.UnsupportedMessageType,
                "Unsupported message type."),
        };

    public async Task SendAudio(Guid conversationId, byte[] pcm)
    {
        if (pcm is null || pcm.Length == 0)
        {
            return;
        }

        var stt = services.GetService<ISpeechToTextProvider>();
        if (stt is null)
        {
            await ErrorAsync(
                correlationId: null,
                ProtocolErrorCodes.VoiceUnavailable,
                "Voice input is not available.");
            return;
        }

        var userId = Context.User!.UserId();
        if (await conversations.FindAsync(conversationId, userId, Context.ConnectionAborted) is null)
        {
            await ErrorAsync(
                correlationId: null,
                TurnErrorCodes.ConversationNotFound,
                "The conversation was not found.");
            return;
        }

        if (voice.IsStreaming(conversationId))
        {
            await ErrorAsync(
                correlationId: null,
                TurnErrorCodes.TurnInProgress,
                "A turn is already in progress for this conversation.");
            return;
        }

        var open = await confirmations.FindOpenAsync(
            conversationId, userId, Context.ConnectionAborted);
        if (open is not null && open.ExpiresAt > clock.GetUtcNow())
        {
            await ErrorAsync(
                correlationId: null,
                ProtocolErrorCodes.ConfirmationPending,
                "There is a pending confirmation for this conversation.");
            return;
        }

        if (sttSessions.TryGet(Context.ConnectionId, out var session))
        {
            if (session.AudioEndTimestamp is not null || session.ConversationId != conversationId)
            {
                await ErrorAsync(
                    correlationId: null,
                    ProtocolErrorCodes.SttInProgress,
                    "Speech recognition is already in progress.");
                return;
            }
        }
        else
        {
            session = await OpenSttSessionAsync(conversationId, userId, stt);
            if (session is null)
            {
                return;
            }
        }

        await session.Channel.Writer.WriteAsync(
            session.NextChunk(pcm), Context.ConnectionAborted);
    }

    private async Task<SttSession?> OpenSttSessionAsync(
        Guid conversationId, Guid userId, ISpeechToTextProvider stt)
    {
        var channel = Channel.CreateUnbounded<AudioChunk>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(Context.ConnectionAborted);
        var session = new SttSession
        {
            ConversationId = conversationId,
            UserId = userId,
            Channel = channel,
            Lifetime = lifetime,
        };

        if (!sttSessions.TryAdd(Context.ConnectionId, session))
        {
            lifetime.Dispose();
            await ErrorAsync(
                correlationId: null,
                ProtocolErrorCodes.SttInProgress,
                "Speech recognition is already in progress.");
            return null;
        }

        var language = "es-AR";
        try
        {
            var active = await personality.GetActiveAsync(Context.ConnectionAborted);
            language = active.Profile.Language;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            ApiLog.SttFailed(log, e, conversationId);
        }

        var connectionId = Context.ConnectionId;
        session.Pump = SttSessionPump.RunAsync(
            session,
            stt,
            language,
            connectionId,
            hubContext,
            sttSessions,
            runner,
            clock,
            log,
            lifetime.Token);

        return session;
    }

    private async Task AudioEndAsync(MessageEnvelope envelope)
    {
        if (!sttSessions.TryGet(Context.ConnectionId, out var session))
        {
            // Idempotent hang-up: the client may send audio_end after a rejected
            // SendAudio (e.g. confirmation pending) or after the pump already finished.
            return;
        }

        session.AudioEndTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        session.Channel.Writer.TryComplete();
    }

    private async Task StartTurnAsync(MessageEnvelope envelope)
    {
        UserMessagePayload? payload;
        try
        {
            payload = envelope.Payload.Deserialize<UserMessagePayload>(ProtocolJson.Options);
        }
        catch (JsonException)
        {
            await ErrorAsync(
                envelope.CorrelationId, ProtocolErrorCodes.InvalidPayload, "Invalid payload.");
            return;
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Text))
        {
            await ErrorAsync(
                envelope.CorrelationId, ProtocolErrorCodes.InvalidPayload, "Invalid payload.");
            return;
        }

        var userId = Context.User!.UserId();
        if (await conversations.FindAsync(
                payload.ConversationId, userId, Context.ConnectionAborted) is null)
        {
            await ErrorAsync(
                envelope.CorrelationId,
                TurnErrorCodes.ConversationNotFound,
                "The conversation was not found.");
            return;
        }

        var open = await confirmations.FindOpenAsync(
            payload.ConversationId, userId, Context.ConnectionAborted);
        if (open is not null && open.ExpiresAt > clock.GetUtcNow())
        {
            await ErrorAsync(
                envelope.CorrelationId,
                ProtocolErrorCodes.ConfirmationPending,
                "There is a pending confirmation for this conversation.");
            return;
        }

        runner.Start(
            new TurnRequest(payload.ConversationId, userId, payload.Text),
            Context.ConnectionId);
    }

    private async Task ResumeConfirmationAsync(MessageEnvelope envelope)
    {
        ConfirmationResponsePayload? payload;
        try
        {
            payload = envelope.Payload.Deserialize<ConfirmationResponsePayload>(ProtocolJson.Options);
        }
        catch (JsonException)
        {
            await ErrorAsync(
                envelope.CorrelationId, ProtocolErrorCodes.InvalidPayload, "Invalid payload.");
            return;
        }

        if (payload is null || payload.ConfirmationId == Guid.Empty)
        {
            await ErrorAsync(
                envelope.CorrelationId, ProtocolErrorCodes.InvalidPayload, "Invalid payload.");
            return;
        }

        var userId = Context.User!.UserId();
        var pending = await confirmations.FindOwnedAsync(
            payload.ConfirmationId, userId, Context.ConnectionAborted);

        if (pending is null
            || pending.ResolvedAt is not null
            || pending.ExpiresAt <= clock.GetUtcNow())
        {
            await ErrorAsync(
                envelope.CorrelationId,
                TurnErrorCodes.ConfirmationInvalid,
                "The confirmation is invalid or no longer available.");
            return;
        }

        runner.Resume(
            TurnRequest.Resume(pending.ConversationId, userId, pending.Id, payload.Approved),
            Context.ConnectionId);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Interrupt must stay silent on payload quirks; trim failures are logged.")]
    private async Task InterruptAsync(MessageEnvelope envelope)
    {
        if (!Guid.TryParse(envelope.CorrelationId, out var turnId))
        {
            return;
        }

        int? playedChunks = null;
        try
        {
            var payload = envelope.Payload.Deserialize<InterruptPayload>(ProtocolJson.Options);
            playedChunks = payload?.PlayedChunks;
        }
        catch (JsonException)
        {
            // Unknown / malformed payload: still cancel like 1a with no trim.
        }

        var userId = Context.User!.UserId();
        voice.CancelSynthesis(turnId);

        if (connections.TryGetActive(turnId, out var origin))
        {
            if (origin.UserId != userId)
            {
                ApiLog.InterruptIgnored(log, turnId);
                return;
            }

            activeTurns.TryInterrupt(origin.ConversationId);

            if (playedChunks is int played)
            {
                await voice.ApplyTrimAsync(
                    turnId, origin.ConversationId, userId, played, Context.ConnectionAborted);
            }

            return;
        }

        // Late interrupt: ledger survives ActiveTurns (D6).
        if (playedChunks is int latePlayed
            && voice.TryGetOwner(turnId, out var conversationId, out var ownerId)
            && ownerId == userId)
        {
            await voice.ApplyTrimAsync(
                turnId, conversationId, userId, latePlayed, Context.ConnectionAborted);
            return;
        }

        ApiLog.InterruptIgnored(log, turnId);
    }

    private async Task AckNotificationAsync(MessageEnvelope envelope)
    {
        NotificationAckPayload? payload;
        try
        {
            payload = envelope.Payload.Deserialize<NotificationAckPayload>(ProtocolJson.Options);
        }
        catch (JsonException)
        {
            await ErrorAsync(
                envelope.CorrelationId, ProtocolErrorCodes.InvalidPayload, "Invalid payload.");
            return;
        }

        if (payload is null || payload.DeliveryId == Guid.Empty)
        {
            await ErrorAsync(
                envelope.CorrelationId, ProtocolErrorCodes.InvalidPayload, "Invalid payload.");
            return;
        }

        acks.TryCompleteDelivery(payload.DeliveryId);
    }

    private async Task ActOnNotificationAsync(MessageEnvelope envelope)
    {
        NotificationActionRequestPayload? payload;
        try
        {
            payload = envelope.Payload.Deserialize<NotificationActionRequestPayload>(ProtocolJson.Options);
        }
        catch (JsonException)
        {
            await ErrorAsync(
                envelope.CorrelationId, ProtocolErrorCodes.InvalidPayload, "Invalid payload.");
            return;
        }

        if (payload is null || payload.NotificationId == Guid.Empty || string.IsNullOrWhiteSpace(payload.Action))
        {
            await ErrorAsync(
                envelope.CorrelationId, ProtocolErrorCodes.InvalidPayload, "Invalid payload.");
            return;
        }

        var result = await notificationActions.ExecuteAsync(
            Context.User!.UserId(),
            payload.NotificationId,
            payload.Action,
            Context.ConnectionAborted);
        if (result.Status == NotificationActionStatus.Applied)
        {
            return;
        }

        var (code, message) = result.Status switch
        {
            NotificationActionStatus.SnoozeLimit => ("snooze_limit", "Ya lo pospusiste bastante."),
            NotificationActionStatus.AlreadyActed => ("already_acted", "Ese aviso ya tuvo una respuesta."),
            NotificationActionStatus.Unsupported => ("unsupported_action", "Esa acción no está disponible."),
            _ => ("not_found", "No encontré ese aviso."),
        };
        await ErrorAsync(envelope.CorrelationId, code, message);
    }

    private Task ErrorAsync(string? correlationId, string code, string message)
        => Clients.Caller.SendAsync(
            "Receive",
            MessageEnvelope.Create(
                ProtocolMessageTypes.Error,
                correlationId,
                new ErrorPayload(code, message),
                clock),
            Context.ConnectionAborted);
}
