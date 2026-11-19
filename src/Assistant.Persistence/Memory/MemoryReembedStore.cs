using System.Globalization;
using Assistant.Core.Memory;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace Assistant.Persistence.Memory;

internal sealed class MemoryReembedStore(AssistantDbContext db) : IMemoryReembedSource
{
    public const string EnsurePartialIndexSql =
        """
        DO $body$
        DECLARE
            current_version text;
            parts int[];
        BEGIN
            SELECT extversion INTO current_version
            FROM pg_extension
            WHERE extname = 'vector';

            IF current_version IS NULL THEN
                RAISE EXCEPTION 'pgvector is not installed. The HNSW index was not created';
            END IF;

            parts := string_to_array(
                regexp_replace(current_version, '[^0-9.].*$', ''), '.')::int[];
            IF parts < ARRAY[0, 8, 7]::int[] THEN
                RAISE EXCEPTION
                    'pgvector % is older than 0.8.7. The HNSW index was not created',
                    current_version;
            END IF;
        END
        $body$;

        CREATE INDEX IF NOT EXISTS ix_memories_embedding
            ON memories USING hnsw (embedding vector_cosine_ops)
            WHERE deleted_at IS NULL AND superseded_by IS NULL;
        """;

    public async ValueTask<string?> ReadPgvectorVersionAsync(CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT extversion FROM pg_extension WHERE extname = 'vector'";
        var result = await command.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : Convert.ToString(result, CultureInfo.InvariantCulture);
    }

    public async ValueTask EnsurePartialHnswIndexAsync(CancellationToken ct)
        => await db.Database.ExecuteSqlRawAsync(EnsurePartialIndexSql, ct);

    public async ValueTask<IReadOnlyList<MemoryReembedRow>> ListPendingAsync(
        string embeddingModel, CancellationToken ct)
    {
        var rows = await db.Memories
            .IgnoreQueryFilters()
            .Where(memory =>
                memory.DeletedAt == null
                && memory.Content != null
                && memory.EmbeddingModel != embeddingModel)
            .OrderBy(memory => memory.Id)
            .Select(memory => new MemoryReembedRow(memory.Id, memory.Content!, memory.IndexTerms))
            .ToListAsync(ct);
        return rows;
    }

    public async ValueTask SaveEmbeddingAsync(
        Guid id, float[] embedding, string embeddingModel, CancellationToken ct)
    {
        if (embedding.Length != MemorySchema.EmbeddingDimensions)
        {
            throw new ArgumentException(
                $"Embedding length must be {MemorySchema.EmbeddingDimensions}.",
                nameof(embedding));
        }

        var entity = await db.Memories
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(memory => memory.Id == id, ct);
        if (entity is null || entity.DeletedAt is not null || entity.Content is null)
        {
            return;
        }

        entity.Embedding = new Vector(embedding);
        entity.EmbeddingModel = embeddingModel;
        await db.SaveChangesAsync(ct);
    }
}
