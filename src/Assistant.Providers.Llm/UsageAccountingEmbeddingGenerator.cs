using System.Text;
using Assistant.Abstractions;
using Assistant.Core.Events;
using Assistant.Core.Memory;
using Assistant.Core.Models;
using Assistant.Core.Usage;
using Assistant.Providers.Llm.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Assistant.Providers.Llm;

/// <summary>
/// Singleton decorator. Reads <see cref="ModelCallContext"/> from
/// <see cref="EmbeddingGenerationOptions"/>; without it, still embeds and does not
/// publish usage (PT-36 D5, D9).
/// </summary>
internal sealed class UsageAccountingEmbeddingGenerator(
    IEmbeddingGenerator<string, Embedding<float>> inner,
    EmbeddingProfileOptions profile,
    CostOptions costs,
    IEventBus events,
    ITokenEstimator estimator,
    TimeProvider clock,
    ILogger logger) : IEmbeddingGenerator<string, Embedding<float>>
{
    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        var inputs = values as IReadOnlyList<string> ?? values.ToList();
        var start = clock.GetTimestamp();
        var result = await inner.GenerateAsync(inputs, options, cancellationToken).ConfigureAwait(false);

        var context = EmbeddingCallOptions.TryGetCallContext(options);
        if (context is not null)
        {
            await PublishSafelyAsync(context, inputs, result, start).ConfigureAwait(false);
        }

        return result;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => inner.GetService(serviceType, serviceKey);

    public void Dispose() => inner.Dispose();

    private async Task PublishSafelyAsync(
        ModelCallContext context,
        IReadOnlyList<string> inputs,
        GeneratedEmbeddings<Embedding<float>> result,
        long start)
    {
        try
        {
            await PublishAsync(context, inputs, result, start).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LlmLog.UsagePublishFailed(logger, e, profile.Provider, profile.Model);
        }
    }

    private async Task PublishAsync(
        ModelCallContext context,
        IReadOnlyList<string> inputs,
        GeneratedEmbeddings<Embedding<float>> result,
        long start)
    {
        var usage = result.Usage;
        var reported = usage?.InputTokenCount is not null;
        int input;
        if (reported)
        {
            input = (int)usage!.InputTokenCount!.Value;
        }
        else
        {
            input = EstimateInput(inputs);
            LlmLog.UsageEstimated(logger, profile.Provider, profile.Model);
        }

        var model = result.FirstOrDefault()?.ModelId ?? profile.Model;
        var price = CostCalculator.ResolvePrice(costs, model, profile.Model);
        var cost = CostCalculator.Calculate(input, output: 0, price);
        var duration = clock.GetElapsedTime(start);

        await events.PublishAsync(
            new UsageRecorded(
                context.ConversationId,
                context.TurnId,
                TaskKind.Embedding,
                profile.Provider,
                model,
                input,
                OutputTokens: 0,
                cost,
                reported,
                duration)
            {
                EmbeddingTokens = input,
            },
            CancellationToken.None).ConfigureAwait(false);

        LlmLog.UsageRecorded(logger, profile.Provider, model, input, output: 0, cost, reported);
    }

    private int EstimateInput(IReadOnlyList<string> inputs)
    {
        var text = new StringBuilder();
        foreach (var value in inputs)
        {
            text.Append(value);
        }

        return estimator.Estimate(text.ToString());
    }
}
