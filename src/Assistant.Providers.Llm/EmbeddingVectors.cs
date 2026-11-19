namespace Assistant.Providers.Llm;

internal static class EmbeddingVectors
{
    public static void EnsureFinite(float[]? vector, int dimensions)
    {
        if (vector is null || vector.Length != dimensions)
        {
            throw new InvalidOperationException(
                $"The local embedding length must be {dimensions}.");
        }

        foreach (var value in vector)
        {
            if (!float.IsFinite(value))
            {
                throw new InvalidOperationException(
                    "The local embedding contains a non-finite value.");
            }
        }
    }
}
