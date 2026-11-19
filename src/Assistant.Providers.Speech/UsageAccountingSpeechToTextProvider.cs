using System.Diagnostics;
using System.Runtime.CompilerServices;
using Assistant.Core.Events;
using Assistant.Core.Models;
using Assistant.Core.Speech;
using Assistant.Core.Usage;
using Microsoft.Extensions.Logging;

namespace Assistant.Providers.Speech;

/// <summary>
/// Decorator. Does not own the inner provider: <see cref="Dispose"/> is a no-op if ever added.
/// Publishes usage on completion or cancellation with bytes accumulated so far (PT-05 D11 spirit).
/// </summary>
internal sealed class UsageAccountingSpeechToTextProvider(
    ISpeechToTextProvider inner,
    string provider,
    string model,
    decimal pricePerMinuteUsd,
    IEventBus events,
    TimeProvider clock,
    ILogger logger) : ISpeechToTextProvider
{
    public async IAsyncEnumerable<TranscriptionEvent> TranscribeAsync(
        IAsyncEnumerable<AudioChunk> audio,
        TranscriptionOptions options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var start = clock.GetTimestamp();
        long pcmBytes = 0;

        try
        {
            await foreach (var evt in inner.TranscribeAsync(
                               CountBytes(audio, b => pcmBytes += b, ct), options, ct).ConfigureAwait(false))
            {
                yield return evt;
            }
        }
        finally
        {
            await PublishSafelyAsync(options.Context, pcmBytes, start).ConfigureAwait(false);
        }
    }

    private static async IAsyncEnumerable<AudioChunk> CountBytes(
        IAsyncEnumerable<AudioChunk> audio,
        Action<int> onBytes,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var chunk in audio.WithCancellation(ct).ConfigureAwait(false))
        {
            onBytes(chunk.Data.Length);
            yield return chunk;
        }
    }

    private async Task PublishSafelyAsync(SpeechCallContext context, long pcmBytes, long start)
    {
        try
        {
            await PublishAsync(context, pcmBytes, start).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            SpeechLog.UsagePublishFailed(logger, e, provider, model);
        }
    }

    private async Task PublishAsync(SpeechCallContext context, long pcmBytes, long start)
    {
        var seconds = SpeechCost.PcmBytesToSeconds(pcmBytes);
        var cost = SpeechCost.ForStt(seconds, pricePerMinuteUsd);
        var duration = clock.GetElapsedTime(start);
        var tags = Tags();

        SpeechTelemetry.Duration.Record(duration.TotalMilliseconds, tags);
        SpeechTelemetry.SttSeconds.Add(seconds, tags);
        SpeechTelemetry.Cost.Add(decimal.ToDouble(cost), tags);

        await events.PublishAsync(
            new UsageRecorded(
                context.ConversationId,
                context.TurnId,
                TaskKind.SpeechToText,
                provider,
                model,
                InputTokens: 0,
                OutputTokens: 0,
                cost,
                TokensReported: false,
                duration)
            {
                SttSeconds = seconds,
            },
            CancellationToken.None).ConfigureAwait(false);

        SpeechLog.UsageRecorded(logger, provider, model, nameof(TaskKind.SpeechToText), seconds, 0, cost);
    }

    private TagList Tags()
        => new()
        {
            { "provider", provider },
            { "model", model },
            { "kind", TaskKind.SpeechToText.ToString() },
        };
}
