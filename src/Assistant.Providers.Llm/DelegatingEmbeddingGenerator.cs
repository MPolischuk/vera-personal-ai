using Assistant.Core.Memory;
using Microsoft.Extensions.AI;

namespace Assistant.Providers.Llm;

/// <summary>
/// Outer decorator. Prepends the query or document prefix, then calls the
/// generator that already accounts for tokens. An empty prefix sends the
/// original string.
/// </summary>
internal sealed class DelegatingEmbeddingGenerator(
    IEmbeddingGenerator<string, Embedding<float>> inner,
    string? queryPrefix,
    string? documentPrefix) : IMemoryEmbedder
{
    public Task<IReadOnlyList<Embedding<float>>> EmbedQueryAsync(
        IReadOnlyList<string> texts,
        EmbeddingGenerationOptions? options,
        CancellationToken ct)
        => EmbedAsync(texts, queryPrefix, options, ct);

    public async Task<Embedding<float>> EmbedDocumentAsync(
        string text,
        EmbeddingGenerationOptions? options,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        var generated = await EmbedAsync([text], documentPrefix, options, ct).ConfigureAwait(false);
        return generated[0];
    }

    private async Task<IReadOnlyList<Embedding<float>>> EmbedAsync(
        IReadOnlyList<string> texts,
        string? prefix,
        EmbeddingGenerationOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var inputs = ApplyPrefix(prefix, texts);
        var generated = await inner.GenerateAsync(inputs, options, cancellationToken).ConfigureAwait(false);
        if (generated.Count != texts.Count)
        {
            throw new InvalidOperationException("The embedding generator returned no vectors.");
        }

        return generated.ToList();
    }

    private static IReadOnlyList<string> ApplyPrefix(string? prefix, IReadOnlyList<string> texts)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return texts;
        }

        var prefixed = new string[texts.Count];
        for (var i = 0; i < texts.Count; i++)
        {
            prefixed[i] = prefix + texts[i];
        }

        return prefixed;
    }
}
