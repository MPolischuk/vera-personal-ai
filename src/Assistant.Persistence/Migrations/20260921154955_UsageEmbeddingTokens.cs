using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Assistant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UsageEmbeddingTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "embedding_tokens",
                table: "usage_records",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "embedding_tokens",
                table: "usage_records");
        }
    }
}
