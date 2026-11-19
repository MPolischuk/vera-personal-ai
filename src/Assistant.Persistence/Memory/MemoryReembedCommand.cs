using Assistant.Core.Memory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Persistence.Memory;

/// <summary>
/// One manual pass. It does not run at startup. Rows with <c>deleted_at</c> or
/// null content are left alone. A row whose <c>embedding_model</c> is already
/// the target model is not embedded again.
/// </summary>
public static class MemoryReembedCommand
{
    public const string Argument = "reembed-memories";

    public static async Task<int> RunAsync(
        IServiceProvider services,
        string embeddingModel,
        TextWriter output,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModel);
        ArgumentNullException.ThrowIfNull(output);

        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        return await ExecuteAsync(
            provider.GetRequiredService<MemoryReembedStore>(),
            provider.GetRequiredService<IMemoryEmbedder>(),
            embeddingModel,
            output,
            ct).ConfigureAwait(false);
    }

    internal static async Task<int> ExecuteAsync(
        IMemoryReembedSource source,
        IMemoryEmbedder embeddings,
        string embeddingModel,
        TextWriter output,
        CancellationToken ct)
    {
        var version = await source.ReadPgvectorVersionAsync(ct).ConfigureAwait(false);
        PgvectorVersions.EnsureSupported(version);
        await source.EnsurePartialHnswIndexAsync(ct).ConfigureAwait(false);

        var pending = await source.ListPendingAsync(embeddingModel, ct).ConfigureAwait(false);
        var updated = 0;
        foreach (var row in pending)
        {
            var embedding = await embeddings.EmbedDocumentAsync(
                IndexText(row.Content, row.IndexTerms),
                options: null,
                ct).ConfigureAwait(false);
            await source.SaveEmbeddingAsync(
                row.Id,
                embedding.Vector.ToArray(),
                embeddingModel,
                ct).ConfigureAwait(false);
            updated++;
        }

        await output.WriteLineAsync($"Re-embedded {updated} memories.").ConfigureAwait(false);
        return updated;
    }

    /// <summary>Same document text <c>MemoryRecorder</c> stores. A null term list adds nothing.</summary>
    internal static string IndexText(string content, string? indexTerms)
        => indexTerms is null ? content : content + "\n" + indexTerms;
}
