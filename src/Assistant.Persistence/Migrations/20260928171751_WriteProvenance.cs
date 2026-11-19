using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Assistant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WriteProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "write_provenance",
                table: "preferences",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "model_clean");

            migrationBuilder.AddColumn<bool>(
                name: "turn_contaminated",
                table: "pending_confirmations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "write_provenance",
                table: "memories",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "model_clean");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "write_provenance",
                table: "preferences");

            migrationBuilder.DropColumn(
                name: "turn_contaminated",
                table: "pending_confirmations");

            migrationBuilder.DropColumn(
                name: "write_provenance",
                table: "memories");
        }
    }
}
