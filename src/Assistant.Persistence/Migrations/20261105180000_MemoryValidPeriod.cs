using Assistant.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Assistant.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AssistantDbContext))]
    [Migration("20261105180000_MemoryValidPeriod")]
    public class MemoryValidPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE memories ADD COLUMN superseded_by uuid;
                ALTER TABLE memories
                    ADD COLUMN valid_period tstzrange NOT NULL
                    DEFAULT tstzrange(CURRENT_TIMESTAMP, 'infinity', '[)');
                UPDATE memories
                    SET valid_period = tstzrange(created_at, 'infinity', '[)');

                CREATE INDEX ix_memories_embedding_current
                    ON memories USING hnsw (embedding vector_cosine_ops)
                    WHERE deleted_at IS NULL AND superseded_by IS NULL;

                DO $body$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_index i
                        JOIN pg_class c ON c.oid = i.indexrelid
                        WHERE c.relname = 'ix_memories_embedding_current'
                          AND i.indisvalid
                    ) THEN
                        RAISE EXCEPTION 'Partial HNSW index ix_memories_embedding_current is not valid';
                    END IF;
                END
                $body$;

                DROP INDEX ix_memories_embedding;
                ALTER INDEX ix_memories_embedding_current RENAME TO ix_memories_embedding;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE INDEX ix_memories_embedding_all
                    ON memories USING hnsw (embedding vector_cosine_ops);
                DROP INDEX ix_memories_embedding;
                ALTER INDEX ix_memories_embedding_all RENAME TO ix_memories_embedding;
                ALTER TABLE memories DROP COLUMN valid_period;
                ALTER TABLE memories DROP COLUMN superseded_by;
                """);
        }
    }
}
