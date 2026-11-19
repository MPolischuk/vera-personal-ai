namespace Assistant.Persistence.Memory;

internal sealed record MemoryReembedRow(Guid Id, string Content, string? IndexTerms);

internal interface IMemoryReembedSource
{
    public ValueTask<string?> ReadPgvectorVersionAsync(CancellationToken ct);

    public ValueTask EnsurePartialHnswIndexAsync(CancellationToken ct);

    public ValueTask<IReadOnlyList<MemoryReembedRow>> ListPendingAsync(
        string embeddingModel, CancellationToken ct);

    public ValueTask SaveEmbeddingAsync(
        Guid id, float[] embedding, string embeddingModel, CancellationToken ct);
}
