using Assistant.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Assistant.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AssistantDbContext))]
    [Migration("20261105200000_MemoryTombstone")]
    public class MemoryTombstone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE memories ALTER COLUMN content DROP NOT NULL;
                ALTER TABLE memories ALTER COLUMN embedding DROP NOT NULL;
                ALTER TABLE memories ADD COLUMN deletion_reason character varying(16);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE memories SET content = '' WHERE content IS NULL;
                UPDATE memories
                    SET embedding = array_fill(0, ARRAY[1536])::vector(1536)
                    WHERE embedding IS NULL;
                ALTER TABLE memories ALTER COLUMN content SET NOT NULL;
                ALTER TABLE memories ALTER COLUMN embedding SET NOT NULL;
                ALTER TABLE memories DROP COLUMN deletion_reason;
                """);
        }
    }
}
