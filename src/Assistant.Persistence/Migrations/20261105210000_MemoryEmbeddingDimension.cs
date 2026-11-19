using Assistant.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Assistant.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AssistantDbContext))]
    [Migration("20261105210000_MemoryEmbeddingDimension")]
    public class MemoryEmbeddingDimension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
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

                DROP INDEX IF EXISTS ix_memories_embedding;
                ALTER TABLE memories DROP COLUMN embedding;
                ALTER TABLE memories ADD COLUMN embedding vector(640);

                CREATE INDEX ix_memories_embedding
                    ON memories USING hnsw (embedding vector_cosine_ops)
                    WHERE deleted_at IS NULL AND superseded_by IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
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

                DROP INDEX IF EXISTS ix_memories_embedding;
                ALTER TABLE memories DROP COLUMN embedding;
                ALTER TABLE memories ADD COLUMN embedding vector(1536);

                CREATE INDEX ix_memories_embedding
                    ON memories USING hnsw (embedding vector_cosine_ops)
                    WHERE deleted_at IS NULL AND superseded_by IS NULL;
                """);
        }
    }
}
