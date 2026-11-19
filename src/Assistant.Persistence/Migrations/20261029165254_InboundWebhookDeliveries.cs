using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Assistant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InboundWebhookDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbound_webhook_deliveries",
                columns: table => new
                {
                    source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    delivery_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbound_webhook_deliveries", x => new { x.source, x.delivery_id });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbound_webhook_deliveries");
        }
    }
}
