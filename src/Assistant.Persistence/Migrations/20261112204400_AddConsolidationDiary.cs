using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Assistant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConsolidationDiary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_proactive_proposals_tool_name_length",
                table: "proactive_proposals");

            migrationBuilder.CreateTable(
                name: "consolidation_diary",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    previous_id = table.Column<Guid>(type: "uuid", nullable: true),
                    successor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consolidation_diary", x => x.id);
                    table.CheckConstraint("ck_consolidation_diary_outcome", "outcome IN ('proposed', 'approved', 'rejected_version', 'rejected_scan', 'undone')");
                    table.CheckConstraint("ck_consolidation_diary_reason_length", "char_length(reason) BETWEEN 1 AND 120");
                    table.ForeignKey(
                        name: "fk_consolidation_diary_proposal_entity_proposal_id",
                        column: x => x.proposal_id,
                        principalTable: "proactive_proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_consolidation_diary_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_proactive_proposals_tool_name_length",
                table: "proactive_proposals",
                sql: "(origin = 'consolidation' AND char_length(tool_name) = 0) OR (origin <> 'consolidation' AND char_length(tool_name) BETWEEN 1 AND 128)");

            migrationBuilder.CreateIndex(
                name: "ix_consolidation_diary_proposal_id",
                table: "consolidation_diary",
                column: "proposal_id");

            migrationBuilder.CreateIndex(
                name: "ix_consolidation_diary_user_id",
                table: "consolidation_diary",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consolidation_diary");

            migrationBuilder.DropCheckConstraint(
                name: "ck_proactive_proposals_tool_name_length",
                table: "proactive_proposals");

            migrationBuilder.AddCheckConstraint(
                name: "ck_proactive_proposals_tool_name_length",
                table: "proactive_proposals",
                sql: "char_length(tool_name) BETWEEN 1 AND 128");
        }
    }
}
