using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Assistant.Api.Protocol;
using Assistant.Api.Realtime;
using Assistant.Core.Conversations;
using Assistant.Core.Personality;
using Assistant.Core.Speech;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Assistant.Api.Speech;

[SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "TTS failures must not fail the text turn (D8).")]
internal sealed class VoiceTurnPipeline(
    IServiceProvider services,
    IHubContext<AssistantHub> hub,
    IPersonalityService personality,
    IOptions<SentenceSegmenterOptions> segmenterOptions,
    IConversationStore conversations,
    VoicePlaybackLedger ledger,
    VoiceSessionRegistry sessions,
    TimeProvider clock,
    ILogger<VoiceTurnPipeline> log)
{
    private readonly ConcurrentDictionary<Guid, TurnVoiceSession> _sessions = new();
    private readonly ConcurrentDictionary<Guid, Guid> _streamingByConversation = new();

    public bool IsStreaming(Guid conversationId)
        => _streamingByConversation.ContainsKey(conversationId);

    public void AttachTurn(
        Guid turnId,
        Guid conversationId,
        Guid userId,
        string connectionId,
        CancellationToken turnCt)
    {
        var tts = services.GetService<ITextToSpeechProvider>();
        if (tts is null)
        {
            return;
        }

        CancelConversationSynthesis(conversationId, exceptTurnId: turnId);
        ledger.DropConversationExcept(conversationId, turnId);
        _streamingByConversation[conversationId] = turnId;

        var turnLedger = ledger.GetOrCreate(turnId, conversationId, userId);
        long? f4Origin = null;
        long? f2Origin = null;
        double? f1Ms = null;
        if (sessions.TryTakeSpeechOrigin(conversationId, out var speechOrigin))
        {
            f4Origin = speechOrigin.AudioEndTimestamp;
            f2Origin = speechOrigin.FinalTranscriptTimestamp;
            f1Ms = Stopwatch.GetElapsedTime(
                speechOrigin.AudioEndTimestamp,
                speechOrigin.FinalTranscriptTimestamp).TotalMilliseconds;
        }

        var linked = CancellationTokenSource.CreateLinkedTokenSource(turnCt);
        var queue = Channel.CreateUnbounded<QueuedSentence>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        var session = new TurnVoiceSession(
            turnId,
            conversationId,
            userId,
            connectionId,
            tts,
            new SentenceSegmenter(segmenterOptions.Value),
            queue,
            linked,
            turnLedger,
            f4Origin,
            f2Origin,
            f1Ms);

        if (!_sessions.TryAdd(turnId, session))
        {
            linked.Dispose();
            return;
        }

        // Provisional options so the TTS pump never blocks on personality (counts in F3).
        // WarmSynthesisOptionsAsync overwrites with the active voice when ready.
        session.Options = new SynthesisOptions(
            new SpeechCallContext(conversationId, turnId),
            VoiceId: string.Empty,
            Speed: 1.0,
            Language: "es-AR");

        session.Pump = PumpAsync(session);
        _ = WarmSynthesisOptionsAsync(session);
    }

    public void MarkStreamingEnded(Guid turnId)
    {
        if (_sessions.TryGetValue(turnId, out var session)
            && _streamingByConversation.TryGetValue(session.ConversationId, out var current)
            && current == turnId)
        {
            _streamingByConversation.TryRemove(session.ConversationId, out _);
        }
    }

    public async ValueTask OnTokenAsync(Guid turnId, string text, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(turnId, out var session))
        {
            return;
        }

        if (session.FirstTokenTimestamp is null)
        {
            session.FirstTokenTimestamp = Stopwatch.GetTimestamp();
            if (session.FinalTranscriptTimestamp is long finalAt)
            {
                session.F2Ms = Stopwatch.GetElapsedTime(finalAt).TotalMilliseconds;
                VoiceTelemetry.SttToFirstTokenMs.Record(session.F2Ms.Value);
            }
        }

        foreach (var sentence in session.Segmenter.Push(text))
        {
            await EnqueueAsync(session, sentence.Text, ct).ConfigureAwait(false);
        }
    }

    public async ValueTask FlushAsync(Guid turnId, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(turnId, out var session))
        {
            return;
        }

        // New model round after tools: fresh aggressive first-sentence threshold.
        foreach (var sentence in session.Segmenter.Flush())
        {
            await EnqueueAsync(session, sentence.Text, ct).ConfigureAwait(false);
        }

        session.Segmenter = new SentenceSegmenter(segmenterOptions.Value);
    }

    public async Task CompleteAsync(Guid turnId, Guid? messageId, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(turnId, out var session))
        {
            return;
        }

        MarkStreamingEnded(turnId);

        if (messageId is Guid id)
        {
            session.Ledger.MessageId = id;
        }

        try
        {
            foreach (var sentence in session.Segmenter.Flush())
            {
                await EnqueueAsync(session, sentence.Text, ct).ConfigureAwait(false);
            }

            session.Queue.Writer.TryComplete();
            if (session.Pump is not null)
            {
                try
                {
                    await session.Pump.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Synthesis cancelled.
                }
            }

            await ApplyPendingTrimAsync(session, ct).ConfigureAwait(false);
        }
        finally
        {
            _sessions.TryRemove(turnId, out _);
            session.SynthesisCts.Dispose();
        }
    }

    public void CancelSynthesis(Guid turnId)
    {
        if (_sessions.TryGetValue(turnId, out var session))
        {
            try
            {
                session.SynthesisCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already completed.
            }

            // Do not complete the queue writer here: TurnFinished still Flushes
            // remaining sentences before CompleteAsync closes the channel.
        }
    }

    public bool TryGetOwner(Guid turnId, out Guid conversationId, out Guid userId)
    {
        if (ledger.TryGet(turnId, out var turnLedger))
        {
            conversationId = turnLedger.ConversationId;
            userId = turnLedger.UserId;
            return true;
        }

        conversationId = default;
        userId = default;
        return false;
    }

    public bool TryTrimSpokenText(Guid turnId, int playedChunks, out string spoken)
    {
        if (!ledger.TryGet(turnId, out var turnLedger))
        {
            spoken = string.Empty;
            return false;
        }

        turnLedger.PendingPlayedChunks = playedChunks;
        return turnLedger.TryBuildSpokenText(playedChunks, out spoken);
    }

    public async Task ApplyTrimAsync(
        Guid turnId, Guid conversationId, Guid userId, int playedChunks, CancellationToken ct)
    {
        if (!TryTrimSpokenText(turnId, playedChunks, out var spoken))
        {
            return;
        }

        if (!ledger.TryGet(turnId, out var turnLedger))
        {
            return;
        }

        if (turnLedger.MessageId is Guid messageId)
        {
            await conversations.TryUpdateMessageContentAsync(
                conversationId, userId, messageId, spoken, wasInterrupted: true, ct)
                .ConfigureAwait(false);
            turnLedger.PendingPlayedChunks = null;
        }
    }

    private async Task ApplyPendingTrimAsync(TurnVoiceSession session, CancellationToken ct)
    {
        if (session.Ledger.PendingPlayedChunks is not int played
            || session.Ledger.MessageId is not Guid messageId)
        {
            return;
        }

        if (!session.Ledger.TryBuildSpokenText(played, out var spoken))
        {
            return;
        }

        await conversations.TryUpdateMessageContentAsync(
                session.ConversationId,
                session.UserId,
                messageId,
                spoken,
                wasInterrupted: true,
                ct)
            .ConfigureAwait(false);
        session.Ledger.PendingPlayedChunks = null;
    }

    private static async ValueTask EnqueueAsync(TurnVoiceSession session, string text, CancellationToken ct)
    {
        var readyAt = Stopwatch.GetTimestamp();
        if (session.FirstSentenceWaitMs is null && session.FirstTokenTimestamp is long firstToken)
        {
            session.FirstSentenceWaitMs = Stopwatch.GetElapsedTime(firstToken).TotalMilliseconds;
            VoiceTelemetry.FirstSentenceWaitMs.Record(session.FirstSentenceWaitMs.Value);
        }

        try
        {
            await session.Queue.Writer.WriteAsync(new QueuedSentence(text, readyAt), ct)
                .ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            // Synthesis cancelled and the channel already completed.
        }
    }

    private async Task WarmSynthesisOptionsAsync(TurnVoiceSession session)
    {
        try
        {
            var active = await personality.GetActiveAsync(session.SynthesisCts.Token)
                .ConfigureAwait(false);
            session.Options = new SynthesisOptions(
                new SpeechCallContext(session.ConversationId, session.TurnId),
                active.Profile.Voice.VoiceId,
                active.Profile.Voice.Speed,
                active.Profile.Language);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            ApiLog.TtsFailed(log, e, session.TurnId);
            session.Options = new SynthesisOptions(
                new SpeechCallContext(session.ConversationId, session.TurnId),
                VoiceId: string.Empty,
                Speed: 1.0,
                Language: "es-AR");
        }
    }

    private async Task PumpAsync(TurnVoiceSession session)
    {
        var ct = session.SynthesisCts.Token;
        try
        {
            // Wait until personality options are ready (best-effort).
            var spin = 0;
            while (session.Options is null && !ct.IsCancellationRequested && spin++ < 200)
            {
                await Task.Delay(5, ct).ConfigureAwait(false);
            }

            session.Options ??= new SynthesisOptions(
                new SpeechCallContext(session.ConversationId, session.TurnId),
                VoiceId: string.Empty,
                Speed: 1.0,
                Language: "es-AR");

            await foreach (var item in session.Queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                await SynthesizeSentenceAsync(session, item, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected on interrupt / new turn.
        }
        catch (Exception e)
        {
            ApiLog.TtsFailed(log, e, session.TurnId);
        }
    }

    private async Task SynthesizeSentenceAsync(
        TurnVoiceSession session, QueuedSentence item, CancellationToken ct)
    {
        using var activity = VoiceTelemetry.ActivitySource.StartActivity("speech.tts");
        var seqStart = session.NextSequence;
        var emitted = 0;
        var firstByteRecorded = false;

        try
        {
            await foreach (var chunk in session.Tts
                               .SynthesizeAsync(item.Text, session.Options!, ct)
                               .ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();

                var sequence = session.NextSequence++;
                emitted++;

                if (!firstByteRecorded)
                {
                    firstByteRecorded = true;
                    var f3Ms = Stopwatch.GetElapsedTime(item.ReadyAt).TotalMilliseconds;
                    VoiceTelemetry.TtsFirstByteMs.Record(f3Ms);

                    if (!session.FirstAudioSent)
                    {
                        session.FirstAudioSent = true;
                        session.F3Ms = f3Ms;
                        var f4Ms = Stopwatch.GetElapsedTime(session.F4Origin).TotalMilliseconds;
                        VoiceTelemetry.TurnToFirstAudioMs.Record(f4Ms);
                        ApiLog.VoiceTurnLatency(
                            log,
                            session.TurnId,
                            session.F1Ms ?? 0,
                            session.F2Ms ?? 0,
                            session.FirstSentenceWaitMs ?? 0,
                            f3Ms,
                            f4Ms);

                        var correlation = session.TurnId.ToString("D");
                        var speaking = AvatarCues.SpeakingOnce(correlation, clock);
                        if (speaking is not null)
                        {
                            await hub.Clients.Client(session.ConnectionId)
                                .SendAsync("Receive", speaking, ct)
                                .ConfigureAwait(false);
                        }
                    }
                }

                var header = new AudioChunkHeader(
                    MessageEnvelope.CurrentVersion,
                    ProtocolMessageTypes.AudioChunk,
                    session.TurnId.ToString("D"),
                    clock.GetUtcNow(),
                    sequence);

                await hub.Clients.Client(session.ConnectionId)
                    .SendAsync("ReceiveAudio", header, chunk.Data.ToArray(), ct)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (emitted > 0)
            {
                session.Ledger.AddSentence(item.Text, seqStart, seqStart + emitted);
            }

            throw;
        }
        catch (Exception e)
        {
            ApiLog.TtsFailed(log, e, session.TurnId);
            session.Queue.Writer.TryComplete();
            return;
        }

        if (emitted > 0)
        {
            session.Ledger.AddSentence(item.Text, seqStart, seqStart + emitted);
        }
    }

    private void CancelConversationSynthesis(Guid conversationId, Guid exceptTurnId)
    {
        foreach (var pair in _sessions)
        {
            if (pair.Value.ConversationId == conversationId && pair.Key != exceptTurnId)
            {
                CancelSynthesis(pair.Key);
                _sessions.TryRemove(pair.Key, out _);
                pair.Value.SynthesisCts.Dispose();
            }
        }
    }

    private sealed class TurnVoiceSession(
        Guid turnId,
        Guid conversationId,
        Guid userId,
        string connectionId,
        ITextToSpeechProvider tts,
        SentenceSegmenter segmenter,
        Channel<QueuedSentence> queue,
        CancellationTokenSource synthesisCts,
        TurnLedger ledger,
        long? f4OriginTimestamp,
        long? finalTranscriptTimestamp,
        double? f1Ms)
    {
        public Guid TurnId { get; } = turnId;

        public Guid ConversationId { get; } = conversationId;

        public Guid UserId { get; } = userId;

        public string ConnectionId { get; } = connectionId;

        public ITextToSpeechProvider Tts { get; } = tts;

        public SentenceSegmenter Segmenter { get; set; } = segmenter;

        public Channel<QueuedSentence> Queue { get; } = queue;

        public CancellationTokenSource SynthesisCts { get; } = synthesisCts;

        public TurnLedger Ledger { get; } = ledger;

        public long AttachedAt { get; } = Stopwatch.GetTimestamp();

        /// <summary>Set only when the turn was started from STT (<c>user.audio_end</c>).</summary>
        public long? AudioEndTimestamp { get; } = f4OriginTimestamp;

        /// <summary>IsFinal timestamp; origin of F2.</summary>
        public long? FinalTranscriptTimestamp { get; } = finalTranscriptTimestamp;

        public long F4Origin => AudioEndTimestamp ?? AttachedAt;

        public double? F1Ms { get; } = f1Ms;

        public double? F2Ms { get; set; }

        public double? FirstSentenceWaitMs { get; set; }

        public double? F3Ms { get; set; }

        public long? FirstTokenTimestamp { get; set; }

        public bool FirstAudioSent { get; set; }

        public SynthesisOptions? Options { get; set; }

        public Task? Pump { get; set; }

        public int NextSequence;
    }

    private sealed record QueuedSentence(string Text, long ReadyAt);
}
