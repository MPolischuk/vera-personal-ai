namespace Assistant.Core.Memory;

public static class MemorySchema
{
    /// <summary>
    /// Dimension of <c>memories.embedding</c> (<c>vector(N)</c>). V2-05.3 set this to the
    /// Harrier-OSS-v1 270M width. Changing it requires a migration.
    /// </summary>
    public const int EmbeddingDimensions = 640;
}
