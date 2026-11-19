using Microsoft.Extensions.AI;

namespace Assistant.Providers.Llm;

/// <summary>
/// In-process embedder. It does not open an HTTP client. Prefixes are applied
/// by <see cref="DelegatingEmbeddingGenerator"/> before this generator runs.
/// </summary>
internal sealed class LocalEmbeddingGenerator(
    ILocalEmbeddingModel model,
    string modelId,
    int dimensions) : IEmbeddingGenerator<string, Embedding<float>>
{
    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        var inputs = values as IReadOnlyList<string> ?? values.ToList();
        if (inputs.Count == 0)
        {
            return [];
        }

        var vectors = await model.EmbedAsync(inputs, cancellationToken).ConfigureAwait(false);
        if (vectors.Length != inputs.Count)
        {
            throw new InvalidOperationException("The local embedding generator returned no vectors.");
        }

        var embeddings = new GeneratedEmbeddings<Embedding<float>>();
        foreach (var vector in vectors)
        {
            EmbeddingVectors.EnsureFinite(vector, dimensions);
            embeddings.Add(new Embedding<float>(vector) { ModelId = modelId });
        }

        return embeddings;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
        if (model is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
