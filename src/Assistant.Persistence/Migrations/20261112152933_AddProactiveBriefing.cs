using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Assistant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProactiveBriefing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "automations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    instruction = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    cadence = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    local_time = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    interval_minutes = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_automations", x => x.id);
                    table.CheckConstraint("ck_automations_cadence", "cadence IN ('once', 'interval', 'daily', 'system_daily')");
                    table.CheckConstraint("ck_automations_instruction_length", "char_length(instruction) BETWEEN 1 AND 2000");
                    table.CheckConstraint("ck_automations_interval", "interval_minutes IS NULL OR interval_minutes >= 30");
                    table.CheckConstraint("ck_automations_local_time", "local_time IS NULL OR local_time ~ '^([01][0-9]|2[0-3]):[0-5][0-9]$'");
                    table.CheckConstraint("ck_automations_status", "status IN ('active', 'paused')");
                    table.CheckConstraint("ck_automations_title_length", "char_length(title) BETWEEN 1 AND 80");
                    table.ForeignKey(
                        name: "fk_automations_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_automations_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "briefings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    local_date = table.Column<DateOnly>(type: "date", nullable: false),
                    summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_briefings", x => x.id);
                    table.CheckConstraint("ck_briefings_summary_length", "char_length(summary) BETWEEN 1 AND 1000");
                    table.ForeignKey(
                        name: "fk_briefings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "goals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goals", x => x.id);
                    table.CheckConstraint("ck_goals_status", "status IN ('active', 'done')");
                    table.CheckConstraint("ck_goals_text_length", "char_length(text) BETWEEN 1 AND 200");
                    table.ForeignKey(
                        name: "fk_goals_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mcp_oauth_credentials",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    server_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    refresh_protected = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mcp_oauth_credentials", x => new { x.user_id, x.server_id });
                    table.CheckConstraint("ck_mcp_oauth_credentials_refresh", "char_length(refresh_protected) BETWEEN 1 AND 4000");
                    table.CheckConstraint("ck_mcp_oauth_credentials_server", "char_length(server_id) BETWEEN 1 AND 64");
                    table.ForeignKey(
                        name: "fk_mcp_oauth_credentials_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "phrase_shortcuts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    phrase_normalized = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_phrase_shortcuts", x => x.id);
                    table.CheckConstraint("ck_phrase_shortcuts_action", "char_length(action) BETWEEN 1 AND 64");
                    table.CheckConstraint("ck_phrase_shortcuts_phrase_length", "char_length(phrase_normalized) BETWEEN 1 AND 40");
                    table.ForeignKey(
                        name: "fk_phrase_shortcuts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "briefing_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    briefing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    block = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reason = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    triage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source_ref = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    feedback = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    feedback_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_briefing_items", x => x.id);
                    table.CheckConstraint("ck_briefing_items_ahead_goal", "block <> 'ahead' OR source = 'goal'");
                    table.CheckConstraint("ck_briefing_items_block", "block IN ('today', 'ahead')");
                    table.CheckConstraint("ck_briefing_items_feedback", "feedback IS NULL OR feedback IN ('done', 'dismissed', 'helpful', 'not_helpful')");
                    table.CheckConstraint("ck_briefing_items_reason_length", "char_length(reason) BETWEEN 1 AND 120");
                    table.CheckConstraint("ck_briefing_items_title_length", "char_length(title) BETWEEN 1 AND 200");
                    table.CheckConstraint("ck_briefing_items_triage", "triage IN ('needs_you', 'can_wait')");
                    table.ForeignKey(
                        name: "fk_briefing_items_briefings_briefing_id",
                        column: x => x.briefing_id,
                        principalTable: "briefings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_automations_conversation_id",
                table: "automations",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_automations_user_id",
                table: "automations",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_briefing_items_briefing_id",
                table: "briefing_items",
                column: "briefing_id");

            migrationBuilder.CreateIndex(
                name: "ux_briefings_user_local_date",
                table: "briefings",
                columns: new[] { "user_id", "local_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_goals_user_id",
                table: "goals",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_phrase_shortcuts_user_phrase",
                table: "phrase_shortcuts",
                columns: new[] { "user_id", "phrase_normalized" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "automations");

            migrationBuilder.DropTable(
                name: "briefing_items");

            migrationBuilder.DropTable(
                name: "goals");

            migrationBuilder.DropTable(
                name: "mcp_oauth_credentials");

            migrationBuilder.DropTable(
                name: "phrase_shortcuts");

            migrationBuilder.DropTable(
                name: "briefings");
        }
    }
}
