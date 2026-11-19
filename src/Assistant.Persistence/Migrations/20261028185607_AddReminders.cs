using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Assistant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reminders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    local_start = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    next_occurrence_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recurrence_rule = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    occurrences_fired = table.Column<int>(type: "integer", nullable: false),
                    last_fired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    origin_channel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    origin_conversation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provenance = table.Column<string>(type: "jsonb", nullable: false),
                    snooze_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reminders", x => x.id);
                    table.CheckConstraint("ck_reminders_status", "status IN ('active', 'completed', 'cancelled', 'missed')");
                    table.CheckConstraint("ck_reminders_text_length", "char_length(text) BETWEEN 1 AND 500");
                    table.ForeignKey(
                        name: "fk_reminders_conversations_origin_conversation_id",
                        column: x => x.origin_conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_reminders_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reminders_origin_conversation_id",
                table: "reminders",
                column: "origin_conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_reminders_user_id_status_next_occurrence_at",
                table: "reminders",
                columns: new[] { "user_id", "status", "next_occurrence_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reminders");
        }
    }
}
