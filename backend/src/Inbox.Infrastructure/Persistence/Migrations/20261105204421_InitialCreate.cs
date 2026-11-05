using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Inbox.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    telegram_chat_id = table.Column<long>(type: "bigint", nullable: false),
                    first_name = table.Column<string>(type: "text", nullable: false),
                    last_name = table.Column<string>(type: "text", nullable: true),
                    username = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contacts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "text", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    telegram_update_id = table.Column<long>(type: "bigint", nullable: true),
                    telegram_message_id = table.Column<long>(type: "bigint", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_queued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                    table.CheckConstraint("ck_messages_direction", "direction IN ('inbound','outbound')");
                    table.CheckConstraint("ck_messages_status", "status IN ('received','pending','sent','failed')");
                    table.ForeignKey(
                        name: "fk_messages_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_contacts_telegram_chat_id",
                table: "contacts",
                column: "telegram_chat_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_messages_conversation",
                table: "messages",
                columns: new[] { "contact_id", "occurred_at", "seq" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_messages_pending",
                table: "messages",
                column: "last_queued_at",
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_messages_unread",
                table: "messages",
                column: "contact_id",
                filter: "read_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_messages_seq",
                table: "messages",
                column: "seq",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_messages_telegram_update_id",
                table: "messages",
                column: "telegram_update_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "contacts");
        }
    }
}
