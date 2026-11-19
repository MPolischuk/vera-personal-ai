using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Assistant.Api.Protocol;
using Assistant.Api.Realtime;
using Assistant.Core.Conversations;
using Assistant.Core.Speech;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Assistant.Api.Speech;

/// <summary>
/// One inbound STT session per SignalR connection.
/// </summary>
/// <summary>
/// Timestamps for a voice-originated turn: F4 from <see cref="AudioEndTimestamp"/>,
/// F2 from <see cref="FinalTranscriptTimestamp"/> (IsFinal).
/// </summary>
internal readonly record struct SpeechOriginTimestamps(
    long AudioEndTimestamp,
    long FinalTranscriptTimestamp);

internal sealed class VoiceSessionRegistry
{
    private readonly ConcurrentDictionary<string, SttSession> _sessions = new();
    private readonly ConcurrentDictionary<Guid, SpeechOriginTimestamps> _pendingSpeechOrigin = new();

    public bool TryGet(string connectionId, out SttSession session)
        => _sessions.TryGetValue(connectionId, out session!);

    public bool TryAdd(string connectionId, SttSession session)
        => _sessions.TryAdd(connectionId, session);

    public bool TryRemove(string connectionId, out SttSession? session)
        => _sessions.TryRemove(connectionId, out session);

    public void NoteSpeechOrigin(
        Guid conversationId, long audioEndTimestamp, long finalTranscriptTimestamp)
        => _pendingSpeechOrigin[conversationId] =
            new SpeechOriginTimestamps(audioEndTimestamp, finalTranscriptTimestamp);

    public bool TryTakeSpeechOrigin(Guid conversationId, out SpeechOriginTimestamps origin)
        => _pendingSpeechOrigin.TryRemove(conversationId, out origin);
}

internal sealed class SttSession
{
    private int _nextSequence;

    public required Guid ConversationId { get; init; }

    public required Guid UserId { get; init; }

    public required Channel<AudioChunk> Channel { get; init; }

    public required CancellationTokenSource Lifetime { get; init; }

    public Task? Pump { get; set; }

    public long? AudioEndTimestamp { get; set; }

    public AudioChunk NextChunk(byte[] pcm)
        => new(Interlocked.Increment(ref _nextSequence) - 1, pcm);
}

/// <summary>
/// Opens the STT stream, forwards transcriptions, and starts a text turn when the stream ends.
/// </summary>
internal static class SttSessionPump
{
    public static async Task RunAsync(
        SttSession session,
        ISpeechToTextProvider stt,
        string language,
        string connectionId,
        IHubContext<AssistantHub> hub,
        VoiceSessionRegistry registry,
        TurnRunner runner,
        TimeProvider clock,
        ILogger log,
        CancellationToken ct)
    {
        using var sttActivity = VoiceTelemetry.ActivitySource.StartActivity("speech.stt");
        try
        {
            var listening = AvatarCues.ListeningOnce(correlationId: null, clock);
            if (listening is not null)
            {
                await hub.Clients.Client(connectionId).SendAsync("Receive", listening, ct)
                    .ConfigureAwait(false);
            }

            var options = new TranscriptionOptions(
                new SpeechCallContext(session.ConversationId, Guid.Empty),
                language);

            var finals = new List<string>();
            string? lastInterim = null;
            long? lastFinalAt = null;

            using var dump = SttAudioDump.CreateIfEnabled(session.ConversationId, clock.GetUtcNow());
            IAsyncEnumerable<AudioChunk> audio = session.Channel.Reader.ReadAllAsync(ct);
            if (dump is not null)
            {
                audio = dump.TeeAsync(audio, ct);
            }

            await foreach (var evt in stt.TranscribeAsync(audio, options, ct)
                               .ConfigureAwait(false))
            {
                await hub.Clients.Client(connectionId).SendAsync(
                    "Receive",
                    MessageEnvelope.Create(
                        ProtocolMessageTypes.Transcription,
                        correlationId: null,
                        new TranscriptionPayload(evt.Text, evt.IsFinal),
                        clock),
                    ct).ConfigureAwait(false);

                if (evt.IsFinal)
                {
                    lastFinalAt = Stopwatch.GetTimestamp();
                    finals.Add(evt.Text);
                }
                else
                {
                    lastInterim = evt.Text;
                }
            }

            var text = SttUtterance.Join(finals, lastInterim);
            if (dump is not null)
            {
                await dump.SaveAsync(text, log).ConfigureAwait(false);
            }

            var finalAt = lastFinalAt ?? Stopwatch.GetTimestamp();
            if (session.AudioEndTimestamp is long audioEnd)
            {
                VoiceTelemetry.SttFinalMs.Record(Stopwatch.GetElapsedTime(audioEnd).TotalMilliseconds);
            }

            if (text.Length == 0)
            {
                await hub.Clients.Client(connectionId).SendAsync(
                    "Receive",
                    MessageEnvelope.Create(
                        ProtocolMessageTypes.Error,
                        correlationId: null,
                        new ErrorPayload(
                            ProtocolErrorCodes.SpeechUnrecognized,
                            "Speech was not recognized."),
                        clock),
                    ct).ConfigureAwait(false);

                foreach (var cue in AvatarCues.IdlePair(correlationId: null, clock))
                {
                    await hub.Clients.Client(connectionId).SendAsync("Receive", cue, ct)
                        .ConfigureAwait(false);
                }
            }
            else
            {
                if (session.AudioEndTimestamp is long audioEndOrigin)
                {
                    registry.NoteSpeechOrigin(
                        session.ConversationId, audioEndOrigin, finalAt);
                }

                runner.Start(
                    new TurnRequest(session.ConversationId, session.UserId, text),
                    connectionId);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Connection or session cancelled.
        }
        catch (Exception e)
        {
            ApiLog.SttFailed(log, e, session.ConversationId);
            try
            {
                await hub.Clients.Client(connectionId).SendAsync(
                    "Receive",
                    MessageEnvelope.Create(
                        ProtocolMessageTypes.Error,
                        correlationId: null,
                        new ErrorPayload(
                            ProtocolErrorCodes.SttUnavailable,
                            "Speech recognition is unavailable."),
                        clock),
                    CancellationToken.None).ConfigureAwait(false);

                foreach (var cue in AvatarCues.IdlePair(correlationId: null, clock))
                {
                    await hub.Clients.Client(connectionId).SendAsync(
                            "Receive", cue, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception sendEx)
            {
                ApiLog.SttFailed(log, sendEx, session.ConversationId);
            }
        }
        finally
        {
            registry.TryRemove(connectionId, out _);
            session.Lifetime.Dispose();
        }
    }
}
