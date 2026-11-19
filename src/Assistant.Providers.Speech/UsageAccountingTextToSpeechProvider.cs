using System.Diagnostics;
using System.Runtime.CompilerServices;
using Assistant.Core.Events;
using Assistant.Core.Models;
using Assistant.Core.Speech;
using Assistant.Core.Usage;
using Microsoft.Extensions.Logging;

namespace Assistant.Providers.Speech;

/// <summary>
/// Decorator. Does not own the inner provider.
/// Publishes usage on completion or cancellation (PT-05 D11 spirit).
/// </summary>
internal sealed class UsageAccountingTextToSpeechProvider(
    ITextToSpeechProvider inner,
    string provider,
    string model,
    decimal pricePerMillionCharactersUsd,
    IEventBus events,
    TimeProvider clock,
    ILogger logger) : ITextToSpeechProvider
{
    public async IAsyncEnumerable<AudioChunk> SynthesizeAsync(
        string text,
        SynthesisOptions options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var start = clock.GetTimestamp();
        var firstByte = false;

        try
        {
            await foreach (var chunk in inner.SynthesizeAsync(text, options, ct).ConfigureAwait(false))
            {
                if (!firstByte && !chunk.Data.IsEmpty)
                {
                    firstByte = true;
                    SpeechTelemetry.TimeToFirstByte.Record(
                        clock.GetElapsedTime(start).TotalMilliseconds, Tags());
                }

                yield return chunk;
            }
        }
        finally
        {
            await PublishSafelyAsync(options.Context, text.Length, start).ConfigureAwait(false);
        }
    }

    private async Task PublishSafelyAsync(SpeechCallContext context, int characters, long start)
    {
        try
        {
            await PublishAsync(context, characters, start).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            SpeechLog.UsagePublishFailed(logger, e, provider, model);
        }
    }

    private async Task PublishAsync(SpeechCallContext context, int characters, long start)
    {
        var cost = SpeechCost.ForTts(characters, pricePerMillionCharactersUsd);
        var duration = clock.GetElapsedTime(start);
        var tags = Tags();

        SpeechTelemetry.Duration.Record(duration.TotalMilliseconds, tags);
        SpeechTelemetry.TtsCharacters.Add(characters, tags);
        SpeechTelemetry.Cost.Add(decimal.ToDouble(cost), tags);

        await events.PublishAsync(
            new UsageRecorded(
                context.ConversationId,
                context.TurnId,
                TaskKind.TextToSpeech,
                provider,
                model,
                InputTokens: 0,
                OutputTokens: 0,
                cost,
                TokensReported: false,
                duration)
            {
                TtsCharacters = characters,
            },
            CancellationToken.None).ConfigureAwait(false);

        SpeechLog.UsageRecorded(
            logger, provider, model, nameof(TaskKind.TextToSpeech), 0, characters, cost);
    }

    private TagList Tags()
        => new()
        {
            { "provider", provider },
            { "model", model },
            { "kind", TaskKind.TextToSpeech.ToString() },
        };
}
