using SentenceTransformers.Harrier.Small;

namespace Assistant.Providers.Llm;

/// <summary>
/// Harrier-OSS-v1 270M, fp32 ONNX, loaded in-process. The default build of the
/// package is quantized; this type asks for the full-precision files instead.
/// Missing files throw before any download, so a turn never calls the network.
/// </summary>
internal sealed class HarrierFp32EmbeddingModel : ILocalEmbeddingModel, IDisposable
{
    private readonly string _modelPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SentenceEncoder? _encoder;

    public HarrierFp32EmbeddingModel(string? modelPath)
    {
        _modelPath = modelPath ?? string.Empty;
    }

    public async Task<float[][]> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        var encoder = await GetEncoderAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await encoder.EncodeAsync(texts.ToArray(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _encoder?.Dispose();
        _gate.Dispose();
    }

    internal static string ResolveOnnxPath(string? modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new InvalidOperationException(
                "Assistant:Models:Embedding:ModelPath is required when Provider is Local.");
        }

        var onnx = modelPath.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)
            ? modelPath
            : Path.Combine(modelPath, "model.onnx");
        var directory = Path.GetDirectoryName(onnx);
        var data = Path.Combine(directory ?? string.Empty, "model.onnx_data");
        if (!File.Exists(onnx) || !File.Exists(data))
        {
            throw new InvalidOperationException(
                $"The local Harrier fp32 model was not found at '{onnx}'. " +
                "Expected model.onnx and model.onnx_data beside it. The embedder does not call OpenRouter.");
        }

        return onnx;
    }

    private async Task<SentenceEncoder> GetEncoderAsync(CancellationToken cancellationToken)
    {
        if (_encoder is not null)
        {
            return _encoder;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_encoder is not null)
            {
                return _encoder;
            }

            var onnx = ResolveOnnxPath(_modelPath);
            _encoder = new SentenceEncoder(modelOnnxPath: onnx);
            return _encoder;
        }
        finally
        {
            _gate.Release();
        }
    }
}
