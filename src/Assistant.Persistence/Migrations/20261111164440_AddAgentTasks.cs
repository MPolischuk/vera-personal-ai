using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Assistant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin_turn_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usage_turn_id = table.Column<Guid>(type: "uuid", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    instruction = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    partial_result = table.Column<string>(type: "text", nullable: true),
                    cost_usd = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false, defaultValue: 0m),
                    cost_cap_usd = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    deadline_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    idempotent = table.Column<bool>(type: "boolean", nullable: false),
                    restart_count = table.Column<int>(type: "integer", nullable: false),
                    external_task_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notice_armed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    locked_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_tasks", x => x.id);
                    table.CheckConstraint("ck_agent_tasks_failure_reason", "failure_reason IS NULL OR failure_reason IN ('interrupted', 'provider_budget', 'network', 'cost_cap', 'deadline', 'unknown_kind', 'worker_error')");
                    table.CheckConstraint("ck_agent_tasks_instruction_length", "char_length(instruction) BETWEEN 1 AND 8000");
                    table.CheckConstraint("ck_agent_tasks_state", "state IN ('submitted', 'working', 'input-required', 'auth-required', 'completed', 'failed', 'canceled', 'rejected')");
                    table.ForeignKey(
                        name: "fk_agent_tasks_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_tasks_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_tasks_conversation_id",
                table: "agent_tasks",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_tasks_state_locked_until",
                table: "agent_tasks",
                columns: new[] { "state", "locked_until" },
                filter: "state = 'working'");

            migrationBuilder.CreateIndex(
                name: "ix_agent_tasks_user_id",
                table: "agent_tasks",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_agent_tasks_external_task_id",
                table: "agent_tasks",
                column: "external_task_id",
                unique: true,
                filter: "external_task_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_agent_tasks_idempotency_key",
                table: "agent_tasks",
                column: "idempotency_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_tasks");
        }
    }
}
