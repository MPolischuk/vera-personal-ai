using System.Text.Json;
using System.Text.Json.Serialization;
using Assistant.Core.Memory;
using Assistant.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Assistant.Extensions.Memory;

/// <summary>
/// One utility call per user. Reads live user and model memories, fills missing
/// <c>index_terms</c>, and inserts at most three inferred rows. Does not use the
/// near-duplicate check from <c>remember</c>.
/// </summary>
internal sealed class MemoryConsolidator(
    IMemoryStore store,
    IMemoryEmbedder embeddings,
    IModelRouter router,
    ILogger<MemoryConsolidator> logger)
{
    internal const int MaxInferences = 3;

    private const int PageSize = 100;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const string Instructions =
        "Leés solo los recuerdos que te paso. No leés el chat. Respondé únicamente con un JSON de esta forma: "
        + "{\"terms\":[{\"id\":\"…\",\"index_terms\":\"…\"}],\"inferences\":[{\"content\":\"…\",\"kind\":\"fact\",\"derived_from\":[\"…\"]}]}. "
        + "terms: solo para recuerdos cuyo index_terms es null. Si no aportan, omitilos. "
        + "inferences: como máximo 3 abstracciones. kind es fact, episode, context o task. "
        + "derived_from son ids de la entrada. No inventes ids ni repitas un recuerdo de la entrada.";

    public async ValueTask RunAsync(MemoryConsolidationDue due, CancellationToken ct)
    {
        MemoryLog.ConsolidationDue(logger, due.Daily, due.Users.Count);
        foreach (var userId in await UserIdsAsync(due, ct))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await SweepUserAsync(userId, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                MemoryLog.ConsolidationFailed(logger, exception);
            }
        }
    }

    private async Task<IReadOnlyList<Guid>> UserIdsAsync(MemoryConsolidationDue due, CancellationToken ct)
        => due.Daily ? await store.ListUserIdsAsync(ct) : due.Users;

    private async Task SweepUserAsync(Guid userId, CancellationToken ct)
    {
        var sources = await LoadSourcesAsync(userId, ct);
        if (sources.Count == 0)
        {
            return;
        }

        var client = router.GetClientFor(
            TaskKind.Utility, new ModelCallContext(Guid.Empty, Guid.Empty));
        var response = await client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, Instructions),
                new ChatMessage(ChatRole.User, JsonSerializer.Serialize(sources.Select(ToPayload), Json)),
            ],
            cancellationToken: ct);

        if (!TryRead(response.Text, out var reply))
        {
            MemoryLog.ConsolidationUnreadable(logger, userId);
            return;
        }

        var byId = sources.ToDictionary(item => item.Id);
        await ApplyTermsAsync(userId, byId, reply.Terms, ct);
        await ApplyInferencesAsync(userId, byId, reply.Inferences, ct);
    }

    private async Task<IReadOnlyList<MemoryItem>> LoadSourcesAsync(Guid userId, CancellationToken ct)
    {
        var sources = new List<MemoryItem>();
        var skip = 0;
        while (true)
        {
            var page = await store.ListAsync(userId, skip, PageSize, ct);
            if (page.Count == 0)
            {
                return sources;
            }

            sources.AddRange(page.Where(item => IsSource(item.WriteProvenance)));
            if (page.Count < PageSize)
            {
                return sources;
            }

            skip += page.Count;
        }
    }

    private async Task ApplyTermsAsync(
        Guid userId,
        Dictionary<Guid, MemoryItem> byId,
        IReadOnlyList<TermReply>? terms,
        CancellationToken ct)
    {
        if (terms is null)
        {
            return;
        }

        foreach (var term in terms)
        {
            if (!byId.TryGetValue(term.Id, out var item)
                || item.IndexTerms is not null
                || string.IsNullOrWhiteSpace(term.IndexTerms))
            {
                continue;
            }

            var indexTerms = term.IndexTerms.Trim();
            var embedding = await EmbedAsync(MemoryRecorder.IndexText(item.Content, indexTerms), ct);
            await store.UpsertAsync(
                new UpsertMemoryRequest(
                    item.Id,
                    userId,
                    item.Kind,
                    item.Content,
                    item.SourceConversationId,
                    item.DueAt,
                    embedding.Vector.ToArray(),
                    ModelId(embedding),
                    item.WriteProvenance,
                    item.Pinned,
                    indexTerms,
                    item.DerivedFrom),
                ct);
        }
    }

    private async Task ApplyInferencesAsync(
        Guid userId,
        Dictionary<Guid, MemoryItem> byId,
        IReadOnlyList<InferenceReply>? inferences,
        CancellationToken ct)
    {
        if (inferences is null)
        {
            return;
        }

        var written = 0;
        foreach (var inference in inferences)
        {
            if (written == MaxInferences)
            {
                return;
            }

            if (!TryAccept(inference, byId, out var content, out var kind, out var evidence))
            {
                continue;
            }

            var embedding = await EmbedAsync(MemoryRecorder.IndexText(content, null), ct);
            await store.UpsertAsync(
                new UpsertMemoryRequest(
                    Id: null,
                    userId,
                    kind,
                    content,
                    SourceConversationId: null,
                    DueAt: null,
                    embedding.Vector.ToArray(),
                    ModelId(embedding),
                    WriteProvenance.Inferred,
                    Pinned: false,
                    IndexTerms: null,
                    evidence),
                ct);
            written++;
        }
    }

    private static bool TryAccept(
        InferenceReply inference,
        Dictionary<Guid, MemoryItem> byId,
        out string content,
        out MemoryKind kind,
        out IReadOnlyList<Guid> evidence)
    {
        content = inference.Content?.Trim() ?? "";
        kind = default;
        var ids = inference.DerivedFrom?
            .Where(byId.ContainsKey)
            .Distinct()
            .ToArray() ?? [];
        evidence = ids;
        return content.Length > 0
            && ids.Length > 0
            && Enum.TryParse(inference.Kind, ignoreCase: true, out kind)
            && Enum.IsDefined(kind);
    }

    private Task<Embedding<float>> EmbedAsync(string text, CancellationToken ct)
        => embeddings.EmbedDocumentAsync(
            text,
            EmbeddingCallOptions.For(new ModelCallContext(Guid.Empty, Guid.Empty)),
            ct);

    private static bool TryRead(string? text, out ConsolidationReply reply)
    {
        reply = new ConsolidationReply(null, null);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ConsolidationReply>(Unwrap(text), Json);
            if (parsed is null)
            {
                return false;
            }

            reply = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Unwrap(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewline < 0 || lastFence <= firstNewline)
        {
            return trimmed;
        }

        return trimmed[(firstNewline + 1)..lastFence].Trim();
    }

    private static SourcePayload ToPayload(MemoryItem item)
        => new(
            item.Id,
            item.Kind.ToString().ToLowerInvariant(),
            item.Content,
            item.IndexTerms);

    private static string ModelId(Embedding<float> embedding)
        => string.IsNullOrWhiteSpace(embedding.ModelId)
            ? MemoryToolDefaults.UnspecifiedEmbeddingModel
            : embedding.ModelId;

    private static bool IsSource(WriteProvenance provenance)
        => provenance is WriteProvenance.User
            or WriteProvenance.ModelClean
            or WriteProvenance.ModelContaminatedConfirmed;

    private sealed record SourcePayload(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("index_terms")] string? IndexTerms);

    private sealed record ConsolidationReply(
        [property: JsonPropertyName("terms")] IReadOnlyList<TermReply>? Terms,
        [property: JsonPropertyName("inferences")] IReadOnlyList<InferenceReply>? Inferences);

    private sealed record TermReply(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("index_terms")] string? IndexTerms);

    private sealed record InferenceReply(
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("kind")] string? Kind,
        [property: JsonPropertyName("derived_from")] IReadOnlyList<Guid>? DerivedFrom);
}
