using Microsoft.Extensions.AI;

namespace Assistant.Core.Memory;

/// <summary>
/// Embeds memory text on the query side or the document side of one generator.
/// Callers pass the raw text. The implementation may prefix what it sends and
/// does not write that prefix into stored content.
/// </summary>
public interface IMemoryEmbedder
{
    /// <summary>Embeds a search batch in one generator call.</summary>
    public Task<IReadOnlyList<Embedding<float>>> EmbedQueryAsync(
        IReadOnlyList<string> texts,
        EmbeddingGenerationOptions? options,
        CancellationToken ct);

    /// <summary>Embeds text that will be stored.</summary>
    public Task<Embedding<float>> EmbedDocumentAsync(
        string text,
        EmbeddingGenerationOptions? options,
        CancellationToken ct);
}
