using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Assistant.Core.Memory;
using Microsoft.Extensions.AI;

namespace Assistant.Providers.Llm;

/// <summary>
/// SHA256 → unit vector of the dimension configured on the test profile.
/// Same text and dimension always yield the same vector; no network (PT-36 D7).
/// </summary>
internal sealed class DeterministicEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public const int MinDimensions = 8;

    public const int MaxDimensions = 4096;

    private readonly int _dimensions;

    public DeterministicEmbeddingGenerator(int dimensions = MemorySchema.EmbeddingDimensions)
    {
        if (dimensions is < MinDimensions or > MaxDimensions)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dimensions),
                dimensions,
                $"Test embedding dimensions must be between {MinDimensions} and {MaxDimensions}.");
        }

        _dimensions = dimensions;
    }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        var embeddings = new GeneratedEmbeddings<Embedding<float>>();
        foreach (var value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            embeddings.Add(new Embedding<float>(Embed(value, _dimensions)) { ModelId = options?.ModelId });
        }

        return Task.FromResult(embeddings);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    internal static float[] Embed(string value, int dimensions)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var vector = new float[dimensions];
        Span<byte> block = stackalloc byte[36];
        Span<byte> next = stackalloc byte[32];
        hash.CopyTo(block);

        var offset = 0;
        var round = 0;
        while (offset < vector.Length)
        {
            BinaryPrimitives.WriteInt32LittleEndian(block[32..], round++);
            SHA256.HashData(block, next);
            for (var b = 0; b + 3 < next.Length && offset < vector.Length; b += 4)
            {
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(next[b..]);
                vector[offset++] = (bits / (float)uint.MaxValue) * 2f - 1f;
            }
        }

        double sumSquares = 0;
        foreach (var component in vector)
        {
            sumSquares += component * (double)component;
        }

        var norm = (float)Math.Sqrt(sumSquares);
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }

        return vector;
    }
}
