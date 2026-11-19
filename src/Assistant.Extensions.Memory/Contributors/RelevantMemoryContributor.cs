using System.Diagnostics;
using System.Text;
using Assistant.Abstractions;
using Assistant.Abstractions.Context;
using Assistant.Core.Memory;
using Assistant.Core.Models;
using Assistant.Core.Observability;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Assistant.Extensions.Memory.Contributors;

internal sealed class RelevantMemoryContributor(
    IMemoryEmbedder embeddings,
    IMemoryStore store,
    ITokenEstimator estimator,
    ILogger<RelevantMemoryContributor> logger) : IContextContributor
{
    public string Name => "memory.relevant";

    public int Priority => 40;

    public bool IsEnabled => true;

    public async ValueTask<ContextFragment?> ContributeAsync(ContextRequest request, CancellationToken ct)
    {
        using var activity = MemoryTelemetry.ActivitySource.StartActivity(Name);

        var query = BuildQuery(request);
        if (query is null)
        {
            return null;
        }

        Embedding<float> embedding;
        try
        {
            using var embedActivity = MemoryTelemetry.ActivitySource.StartActivity("memory.embed");
            var generated = await embeddings.EmbedQueryAsync(
                [query],
                EmbeddingCallOptions.For(new ModelCallContext(request.ConversationId, request.TurnId)),
                ct);
            embedding = generated[0];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MemoryLog.EmbedFailed(logger, ex, request.TurnId);
            return null;
        }

        IReadOnlyList<ScoredMemory> hits;
        using (var searchActivity = MemoryTelemetry.ActivitySource.StartActivity(GenAiSpans.SearchMemory))
        {
            hits = await store.SearchAsync(
                new SearchMemoryRequest(
                    request.UserId,
                    embedding.Vector.ToArray(),
                    MemoryToolDefaults.RecallTopK,
                    MemoryToolDefaults.RecallMinScore,
                    Kind: null,
                    ExcludePinned: true),
                ct);

            searchActivity?.SetTag(GenAiSpans.MemoryStoreId, GenAiSpans.StoreMemories);
            searchActivity?.SetTag(GenAiSpans.MemoryRecordCount, hits.Count);
            if (hits.Count > 0)
            {
                searchActivity?.SetTag("memory.worst_score", hits.Min(hit => hit.Score));
            }
        }

        if (hits.Count == 0)
        {
            return null;
        }

        var content = BuildFragmentText(hits);
        var estimated = estimator.Estimate(content);
        if (estimated > MemoryContextDefaults.MaxRelevantMemoryTokens)
        {
            MemoryTelemetry.RelevantDropped.Add(1);
            return null;
        }

        MemoryTelemetry.RelevantHits.Add(hits.Count);
        if (logger.IsEnabled(LogLevel.Information))
        {
            var items = string.Join(", ", hits.Select(hit => $"{hit.Item.Id}:{Kind(hit.Item.Kind)}"));
            MemoryLog.RelevantMemoriesIncluded(logger, items);
        }

        activity?.SetTag("memory.ids", string.Join(",", hits.Select(hit => hit.Item.Id)));

        return ContextFragment.Text(Name, ContextPlacement.AppendedMessage, content, estimated);
    }

    private static string? BuildQuery(ContextRequest request)
    {
        var parts = request.RecentHistory
            .Where(message => message.Role is MessageRole.User)
            .TakeLast(MemoryContextDefaults.RecentUserMessages)
            .Select(message => message.Content)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        if (!string.IsNullOrWhiteSpace(request.UserMessage))
        {
            parts.Add(request.UserMessage);
        }

        return parts.Count == 0 ? null : string.Join("\n", parts);
    }

    private static string BuildFragmentText(IReadOnlyList<ScoredMemory> hits)
    {
        var sb = new StringBuilder();
        sb.Append("Recuerdos pertinentes a este turno:\n");
        foreach (var hit in hits)
        {
            sb.Append("- [");
            sb.Append(Kind(hit.Item.Kind));
            sb.Append("] ");
            sb.Append(hit.Item.Content);
            if (hit.Item.WriteProvenance == WriteProvenance.Inferred)
            {
                sb.Append(" [inferido]");
            }

            sb.Append('\n');
        }

        sb.Append("No recites la lista. Si no pega, no lo menciones. No inventes recuerdos. ");
        sb.Append("Anotá qué recuerdo usás y por qué. Si unís dos, decí el vínculo. ");
        sb.Append("Si el usuario afirma un hecho suyo o pide que te acuerdes, llamá remember. ");
        sb.Append("Si pregunta un dato suyo, llamá recall. ");
        sb.Append("Si una tool necesita un lugar de la persona, usá el nombre de estos recuerdos y no otra ciudad. ");
        sb.Append("Hablá de la persona en segunda persona.");
        return sb.ToString();
    }

    private static string Kind(MemoryKind kind) => kind.ToString().ToLowerInvariant();
}
