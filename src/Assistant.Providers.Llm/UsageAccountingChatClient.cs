using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Assistant.Abstractions;
using Assistant.Core.Events;
using Assistant.Core.Models;
using Assistant.Core.Usage;
using Assistant.Providers.Llm.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Assistant.Providers.Llm;

/// <summary>
/// Per-turn decorator. Does not own the inner client (cached and shared):
/// <see cref="Dispose"/> releases nothing (D5).
/// </summary>
internal sealed class UsageAccountingChatClient(
    IChatClient innerClient,
    ModelProfileOptions profile,
    TaskKind kind,
    ModelCallContext context,
    CostOptions costs,
    IEventBus events,
    ITokenEstimator estimator,
    TimeProvider clock,
    ILogger logger) : IChatClient
{
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messageList = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        var start = clock.GetTimestamp();
        var firstToken = false;
        var text = new StringBuilder();
        UsageDetails? usage = null;
        string? reportedModel = null;

        try
        {
            await foreach (var update in innerClient.GetStreamingResponseAsync(
                               messageList, options, cancellationToken).ConfigureAwait(false))
            {
                if (!firstToken && !string.IsNullOrEmpty(update.Text))
                {
                    firstToken = true;

                    // RNF-01 and D1: measure here or nowhere.
                    LlmTelemetry.TimeToFirstToken.Record(
                        clock.GetElapsedTime(start).TotalMilliseconds, Tags());
                }

                text.Append(update.Text);
                reportedModel ??= update.ModelId;
                usage ??= update.Contents.OfType<UsageContent>().FirstOrDefault()?.Details;

                yield return update;
            }
        }
        finally
        {
            // Always publish, even if the user interrupted or the provider failed:
            // tokens generated so far are still billed (D11).
            // Accounting must not hide the stream's original exception.
            await PublishSafelyAsync(messageList, text.ToString(), usage, reportedModel, start)
                .ConfigureAwait(false);
        }
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messageList = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        var start = clock.GetTimestamp();
        ChatResponse? response = null;

        try
        {
            response = await innerClient.GetResponseAsync(messageList, options, cancellationToken)
                .ConfigureAwait(false);
            return response;
        }
        finally
        {
            var text = response?.Text ?? string.Empty;
            var usage = response?.Usage;
            var model = response?.ModelId;
            await PublishSafelyAsync(messageList, text, usage, model, start).ConfigureAwait(false);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => innerClient.GetService(serviceType, serviceKey);

    public void Dispose()
    {
        // The inner client is shared and owned by the cache.
    }

    private async Task PublishSafelyAsync(
        IReadOnlyList<ChatMessage> messages,
        string outputText,
        UsageDetails? usage,
        string? reportedModel,
        long start)
    {
        try
        {
            await PublishAsync(messages, outputText, usage, reportedModel, start)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LlmLog.UsagePublishFailed(logger, e, profile.Provider, profile.Model);
        }
    }

    private async Task PublishAsync(
        IReadOnlyList<ChatMessage> messages,
        string outputText,
        UsageDetails? usage,
        string? reportedModel,
        long start)
    {
        var reported = usage?.InputTokenCount is not null || usage?.OutputTokenCount is not null;
        int input;
        int output;
        var cached = 0;
        var cacheWrite = 0;

        if (reported)
        {
            input = (int)(usage!.InputTokenCount ?? 0);
            output = (int)(usage.OutputTokenCount ?? 0);
            (cached, cacheWrite) = CacheUsage.Read(usage);
            cached = Math.Clamp(cached, 0, Math.Max(input, 0));
        }
        else
        {
            input = EstimateInput(messages);
            output = estimator.Estimate(outputText);
            LlmLog.UsageEstimated(logger, profile.Provider, profile.Model);
        }

        var model = reportedModel ?? profile.Model;
        if (input > 0)
        {
            LlmTelemetry.CacheHitRatio.Record((double)cached / input, Tags(model));
        }

        var price = CostCalculator.ResolvePrice(costs, reportedModel, profile.Model);
        var cost = CostCalculator.Calculate(input, output, price, cached, cacheWrite);
        var duration = clock.GetElapsedTime(start);
        var source = reported ? "provider" : "estimated";

        LlmTelemetry.Duration.Record(duration.TotalMilliseconds, Tags());
        LlmTelemetry.Tokens.Add(
            input,
            new TagList
            {
                { "model", model },
                { "direction", "input" },
                { "source", source },
            });
        LlmTelemetry.Tokens.Add(
            output,
            new TagList
            {
                { "model", model },
                { "direction", "output" },
                { "source", source },
            });
        LlmTelemetry.Cost.Add(decimal.ToDouble(cost), Tags(model));

        if (reported)
        {
            var estimated = EstimateInput(messages) + estimator.Estimate(outputText);
            var reportedTotal = input + output;
            if (reportedTotal > 0)
            {
                LlmTelemetry.EstimationError.Record(
                    (double)estimated / reportedTotal, new TagList { { "model", model } });
            }
        }

        await events.PublishAsync(
            new UsageRecorded(
                context.ConversationId,
                context.TurnId,
                kind,
                profile.Provider,
                model,
                input,
                output,
                cost,
                reported,
                duration)
            {
                CachedInputTokens = cached,
                CacheWriteTokens = cacheWrite,
            },
            CancellationToken.None).ConfigureAwait(false);

        LlmLog.UsageRecorded(logger, profile.Provider, model, input, output, cost, reported);
    }

    private int EstimateInput(IReadOnlyList<ChatMessage> messages)
    {
        var text = new StringBuilder();
        foreach (var message in messages)
        {
            text.Append(message.Text);
        }

        return estimator.Estimate(text.ToString());
    }

    private TagList Tags(string? model = null)
        => new()
        {
            { "provider", profile.Provider },
            { "model", model ?? profile.Model },
            { "kind", kind.ToString() },
        };
}
