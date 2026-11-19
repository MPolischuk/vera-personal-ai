using System.Diagnostics;
using System.Text;
using Assistant.Abstractions.Tools;
using Assistant.Core.Events;
using Assistant.Core.Memory;
using Assistant.Core.Models;
using Assistant.Core.Observability;
using Assistant.Core.Security;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Assistant.Extensions.Memory;

internal abstract record RememberResult;

internal sealed record RememberedMemory(MemoryItem Item, bool Updated) : RememberResult;

internal sealed record RememberRejected(string Error) : RememberResult;

internal sealed class MemoryRecorder(
    IMemoryEmbedder embeddings,
    IMemoryStore store,
    IEventBus events,
    ILogger<MemoryRecorder> logger)
{
    public async Task<RememberResult> RememberAsync(
        string content,
        MemoryKind kind,
        DateTimeOffset? dueAt,
        bool? pinned,
        Guid? replacesId,
        string? indexTerms,
        ToolExecutionContext context,
        CancellationToken ct)
    {
        if (PromptInjectionScanner.Inspect(content).IsRejected)
        {
            return new RememberRejected(PromptInjectionScanner.RejectionMessage);
        }

        var terms = NormalizeTerms(indexTerms);
        if (replacesId is { } previousId)
        {
            return await ReplaceAsync(content, kind, dueAt, pinned, terms, previousId, context, ct);
        }

        var embedding = await EmbedAsync(IndexText(content, terms), context, ct);
        var vector = embedding.Vector.ToArray();
        var model = ModelId(embedding);

        IReadOnlyList<ScoredMemory> duplicates;
        using (var activity = MemoryTelemetry.ActivitySource.StartActivity(GenAiSpans.SearchMemory))
        {
            duplicates = await store.SearchAsync(
                new SearchMemoryRequest(
                    context.UserId,
                    vector,
                    MemoryToolDefaults.DuplicateTopK,
                    MemoryToolDefaults.DuplicateMinScore,
                    Kind: null),
                ct);
            activity?.SetTag(GenAiSpans.MemoryStoreId, GenAiSpans.StoreMemories);
            activity?.SetTag(GenAiSpans.MemoryRecordCount, duplicates.Count);
        }

        var duplicate = duplicates.Count > 0 ? duplicates[0] : null;
        var nextPinned = pinned ?? duplicate?.Item.Pinned ?? false;
        var rejection = await RejectIfNewPinAsync(
            nextPinned, duplicate?.Item.Pinned ?? false, context.UserId, ct);
        if (rejection is not null)
        {
            return new RememberRejected(rejection);
        }

        var updated = duplicate is not null;
        var saved = await store.UpsertAsync(
            new UpsertMemoryRequest(
                duplicate?.Item.Id,
                context.UserId,
                kind,
                content,
                duplicate is { } hit ? hit.Item.SourceConversationId : context.ConversationId,
                dueAt,
                vector,
                model,
                WriteProvenance.FromModelTurn(context.TurnContaminated),
                nextPinned,
                terms),
            ct);

        await PublishStoredAsync(saved, updated, context, ct);
        return new RememberedMemory(saved, updated);
    }

    private async Task<RememberResult> ReplaceAsync(
        string content,
        MemoryKind kind,
        DateTimeOffset? dueAt,
        bool? pinned,
        string? indexTerms,
        Guid previousId,
        ToolExecutionContext context,
        CancellationToken ct)
    {
        if (PromptInjectionScanner.Inspect(content).IsRejected)
        {
            return new RememberRejected(PromptInjectionScanner.RejectionMessage);
        }

        var previous = await store.FindAsync(previousId, context.UserId, ct);
        if (previous is null)
        {
            return new RememberRejected(ReplaceNotFound);
        }

        var nextPinned = pinned ?? previous.Pinned;
        var rejection = await RejectIfNewPinAsync(nextPinned, previous.Pinned, context.UserId, ct);
        if (rejection is not null)
        {
            return new RememberRejected(rejection);
        }

        var embedding = await EmbedAsync(IndexText(content, indexTerms), context, ct);
        var saved = await store.ReplaceAsync(
            previousId,
            new UpsertMemoryRequest(
                Id: null,
                context.UserId,
                kind,
                content,
                context.ConversationId,
                dueAt,
                embedding.Vector.ToArray(),
                ModelId(embedding),
                WriteProvenance.FromModelTurn(context.TurnContaminated),
                nextPinned,
                indexTerms),
            ct);

        if (saved is null)
        {
            return new RememberRejected(ReplaceNotFound);
        }

        await PublishStoredAsync(saved, updated: false, context, ct, replacement: true);
        return new RememberedMemory(saved, Updated: false);
    }

    private async Task<string?> RejectIfNewPinAsync(
        bool nextPinned, bool alreadyPinned, Guid userId, CancellationToken ct)
    {
        if (!nextPinned || alreadyPinned)
        {
            return null;
        }

        var pinned = await store.ListPinnedAsync(userId, ct);
        if (pinned.Count < MemoryToolDefaults.MaxPinned)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.Append("Hay 8 hechos fijados. Reemplazá uno con replaces_id.");
        foreach (var item in pinned)
        {
            sb.Append("\n- ");
            sb.Append(item.Id);
            sb.Append(": ");
            sb.Append(item.Content);
        }

        return sb.ToString();
    }

    private async Task PublishStoredAsync(
        MemoryItem saved,
        bool updated,
        ToolExecutionContext context,
        CancellationToken ct,
        bool replacement = false)
    {
        // replaces_id writes a new row (updated stays false for the insert counter).
        // The GenAI operation is still an update of the previous record.
        var operation = updated || replacement ? GenAiSpans.UpdateMemory : GenAiSpans.CreateMemory;
        using var activity = MemoryTelemetry.ActivitySource.StartActivity(operation);
        activity?.SetTag(GenAiSpans.MemoryStoreId, GenAiSpans.StoreMemories);
        activity?.SetTag(GenAiSpans.MemoryRecordId, saved.Id.ToString());

        await events.PublishAsync(
            new MemoryStored(
                context.UserId,
                saved.Id,
                saved.Kind,
                context.ConversationId,
                context.TurnId),
            ct);

        if (updated)
        {
            MemoryTelemetry.Updates.Add(1);
        }
        else
        {
            MemoryTelemetry.Inserts.Add(1);
        }

        MemoryLog.Stored(logger, saved.Id, saved.Kind, updated);
    }

    private const string ReplaceNotFound =
        "No encontré ese recuerdo para reemplazar. Tiene que ser un recuerdo activo de esta persona.";

    private static string ModelId(Embedding<float> embedding)
        => string.IsNullOrWhiteSpace(embedding.ModelId)
            ? MemoryToolDefaults.UnspecifiedEmbeddingModel
            : embedding.ModelId;

    public Task<IReadOnlyList<ScoredMemory>> SearchAsync(
        string query,
        MemoryKind? kind,
        DateTimeOffset? since,
        ToolExecutionContext context,
        int topK,
        float minScore,
        CancellationToken ct,
        bool includeHistory = false)
        => SearchAsync([query], kind, since, context, topK, minScore, ct, includeHistory);

    public async Task<IReadOnlyList<ScoredMemory>> SearchAsync(
        IReadOnlyList<string> queries,
        MemoryKind? kind,
        DateTimeOffset? since,
        ToolExecutionContext context,
        int topK,
        float minScore,
        CancellationToken ct,
        bool includeHistory = false)
    {
        using var activity = MemoryTelemetry.ActivitySource.StartActivity(GenAiSpans.SearchMemory);
        IReadOnlyList<Embedding<float>> vectors;
        try
        {
            using (MemoryTelemetry.ActivitySource.StartActivity("memory.embed"))
            {
                vectors = await embeddings.EmbedQueryAsync(
                    queries,
                    EmbeddingCallOptions.For(new ModelCallContext(context.ConversationId, context.TurnId)),
                    ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Hybrid search was no-go. A local embedder failure returns no hits
            // and does not call a remote embedder.
            MemoryLog.EmbedFailed(logger, ex, context.TurnId);
            activity?.SetTag(GenAiSpans.MemoryRecordCount, 0);
            return [];
        }
        var best = new Dictionary<Guid, ScoredMemory>();
        foreach (var embedding in vectors)
        {
            var hits = await store.SearchAsync(
                new SearchMemoryRequest(
                    context.UserId,
                    embedding.Vector.ToArray(),
                    topK,
                    minScore,
                    kind,
                    since,
                    IncludeHistory: includeHistory),
                ct);
            foreach (var hit in hits)
            {
                if (!best.TryGetValue(hit.Item.Id, out var current) || hit.Score > current.Score)
                {
                    best[hit.Item.Id] = hit;
                }
            }
        }

        var results = best.Values
            .OrderByDescending(hit => hit.Score)
            .Take(topK)
            .ToList();
        activity?.SetTag(GenAiSpans.MemoryStoreId, GenAiSpans.StoreMemories);
        activity?.SetTag(GenAiSpans.MemoryRecordCount, results.Count);
        return results;
    }

    private async Task<Embedding<float>> EmbedAsync(
        string text, ToolExecutionContext context, CancellationToken ct)
    {
        using var activity = MemoryTelemetry.ActivitySource.StartActivity("memory.embed");
        return await embeddings.EmbedDocumentAsync(
            text,
            EmbeddingCallOptions.For(new ModelCallContext(context.ConversationId, context.TurnId)),
            ct);
    }

    private static string? NormalizeTerms(string? indexTerms)
        => string.IsNullOrWhiteSpace(indexTerms) ? null : indexTerms.Trim();

    internal static string IndexText(string content, string? indexTerms)
        => indexTerms is null ? content : content + "\n" + indexTerms;
}
