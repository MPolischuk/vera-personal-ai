using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Assistant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHeartbeatFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "heartbeat_checks",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_heartbeat_checks", x => x.user_id);
                    table.CheckConstraint("ck_heartbeat_checks_text_length", "char_length(text) <= 2000");
                    table.ForeignKey(
                        name: "fk_heartbeat_checks_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "proactive_proposals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    tool_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    arguments_json = table.Column<string>(type: "jsonb", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proactive_proposals", x => x.id);
                    table.CheckConstraint("ck_proactive_proposals_origin", "origin IN ('heartbeat', 'consolidation')");
                    table.CheckConstraint("ck_proactive_proposals_reason_length", "char_length(reason) BETWEEN 1 AND 500");
                    table.CheckConstraint("ck_proactive_proposals_status", "status IN ('pending', 'approved', 'rejected')");
                    table.CheckConstraint("ck_proactive_proposals_tool_name_length", "char_length(tool_name) BETWEEN 1 AND 128");
                    table.ForeignKey(
                        name: "fk_proactive_proposals_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "suggestion_audits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    local_date = table.Column<DateOnly>(type: "date", nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_ref = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suggestion_audits", x => x.id);
                    table.CheckConstraint("ck_suggestion_audits_outcome", "outcome IN ('delivered', 'discarded')");
                    table.CheckConstraint("ck_suggestion_audits_reason_length", "char_length(reason) BETWEEN 1 AND 200");
                    table.ForeignKey(
                        name: "fk_suggestion_audits_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_proactive_proposals_user_id",
                table: "proactive_proposals",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_suggestion_audits_user_id_local_date",
                table: "suggestion_audits",
                columns: new[] { "user_id", "local_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "heartbeat_checks");

            migrationBuilder.DropTable(
                name: "proactive_proposals");

            migrationBuilder.DropTable(
                name: "suggestion_audits");
        }
    }
}
