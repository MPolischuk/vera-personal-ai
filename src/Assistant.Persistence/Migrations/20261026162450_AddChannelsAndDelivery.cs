using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Assistant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelsAndDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "channel_inbox",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    channel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    external_peer_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channel_inbox", x => x.id);
                    table.CheckConstraint("ck_channel_inbox_status", "status IN ('received', 'processing', 'done', 'ignored', 'failed')");
                });

            migrationBuilder.CreateTable(
                name: "channel_link_tokens",
                columns: table => new
                {
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channel_link_tokens", x => x.token_hash);
                });

            migrationBuilder.CreateTable(
                name: "channel_peers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    external_peer_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    pairing_code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    pairing_code_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pairing_attempts = table.Column<int>(type: "integer", nullable: false),
                    last_code_reminder_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    delivery_blocked = table.Column<bool>(type: "boolean", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_inbound_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channel_peers", x => x.id);
                    table.CheckConstraint("ck_channel_peers_status", "status IN ('pending', 'approved', 'revoked', 'blocked')");
                    table.ForeignKey(
                        name: "fk_channel_peers_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "notification_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    target_ref_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    result = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_deliveries", x => x.id);
                    table.CheckConstraint("ck_notification_deliveries_result", "result IN ('pending', 'delivered', 'awaiting_ack', 'acked', 'ack_timeout', 'transient', 'permanent', 'not_applicable')");
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    dedup_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    trust = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    preferred_channel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actions = table.Column<string>(type: "jsonb", nullable: true),
                    source_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    source_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    delivered_channel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    action_taken = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.CheckConstraint("ck_notifications_status", "status IN ('pending', 'dispatching', 'deferred', 'delivered', 'acted', 'failed', 'cancelled')");
                    table.CheckConstraint("ck_notifications_trust", "trust IN ('trusted', 'untrusted')");
                });

            migrationBuilder.CreateTable(
                name: "push_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    endpoint = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    p256dh = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    auth = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    vapid_public_key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent_family = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_success_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_failure_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_count = table.Column<int>(type: "integer", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_push_subscriptions", x => x.id);
                    table.CheckConstraint("ck_push_subscriptions_status", "status IN ('active', 'disabled', 'revoked')");
                    table.ForeignKey(
                        name: "fk_push_subscriptions_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_channel_inbox_channel_external_id",
                table: "channel_inbox",
                columns: new[] { "channel", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_channel_inbox_channel_external_peer_id_received_at",
                table: "channel_inbox",
                columns: new[] { "channel", "external_peer_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_channel_inbox_status_received_at",
                table: "channel_inbox",
                columns: new[] { "status", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_channel_link_tokens_expires_at",
                table: "channel_link_tokens",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_channel_link_tokens_user_id",
                table: "channel_link_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_channel_peers_channel_external_peer_id",
                table: "channel_peers",
                columns: new[] { "channel", "external_peer_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_channel_peers_conversation_id",
                table: "channel_peers",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_channel_peers_user_id",
                table: "channel_peers",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_created_at",
                table: "notification_deliveries",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_notification_id_attempt",
                table: "notification_deliveries",
                columns: new[] { "notification_id", "attempt" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_status_created_at",
                table: "notifications",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_dedup_key",
                table: "notifications",
                columns: new[] { "user_id", "dedup_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_status_created_at",
                table: "notifications",
                columns: new[] { "user_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_push_subscriptions_device_id",
                table: "push_subscriptions",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "ix_push_subscriptions_endpoint_hash",
                table: "push_subscriptions",
                column: "endpoint_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_push_subscriptions_user_id",
                table: "push_subscriptions",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "channel_inbox");

            migrationBuilder.DropTable(
                name: "channel_link_tokens");

            migrationBuilder.DropTable(
                name: "channel_peers");

            migrationBuilder.DropTable(
                name: "notification_deliveries");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "push_subscriptions");
        }
    }
}
