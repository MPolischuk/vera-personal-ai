using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Assistant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStandingOrdersAndConditionalIntents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conditional_intents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conditional_intents", x => x.id);
                    table.CheckConstraint("ck_conditional_intents_event_name", "event_name IN ('inbound.fact')");
                    table.CheckConstraint("ck_conditional_intents_status", "status IN ('active', 'paused')");
                    table.CheckConstraint("ck_conditional_intents_text_length", "char_length(text) BETWEEN 1 AND 300");
                    table.ForeignKey(
                        name: "fk_conditional_intents_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "standing_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_standing_orders", x => x.id);
                    table.CheckConstraint("ck_standing_orders_status", "status IN ('active', 'paused')");
                    table.CheckConstraint("ck_standing_orders_text_length", "char_length(text) BETWEEN 1 AND 500");
                    table.ForeignKey(
                        name: "fk_standing_orders_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conditional_intents_user_id",
                table: "conditional_intents",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_standing_orders_user_id",
                table: "standing_orders",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conditional_intents");

            migrationBuilder.DropTable(
                name: "standing_orders");
        }
    }
}
