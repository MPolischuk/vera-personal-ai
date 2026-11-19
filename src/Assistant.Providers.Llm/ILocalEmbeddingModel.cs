namespace Assistant.Providers.Llm;

internal interface ILocalEmbeddingModel
{
    public Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}
