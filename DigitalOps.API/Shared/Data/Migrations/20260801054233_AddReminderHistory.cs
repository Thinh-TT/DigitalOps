using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalOps.API.Shared.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReminderHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reminder_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    incoming_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_staff_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reminder_kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reminder_date = table.Column<DateOnly>(type: "date", nullable: false),
                    delivery_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Unread"),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    read_at = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reminder_history", x => x.id);
                    table.CheckConstraint("ck_reminder_history_delivery_status", "delivery_status IN ('Unread', 'Read')");
                    table.CheckConstraint("ck_reminder_history_kind", "reminder_kind IN ('BeforeDeadline', 'DueDate', 'Overdue')");
                    table.CheckConstraint("ck_reminder_history_read_at", "(delivery_status = 'Read' AND read_at IS NOT NULL) OR (delivery_status = 'Unread' AND read_at IS NULL)");
                    table.ForeignKey(
                        name: "fk_reminder_history_incoming_documents_incoming_document_id",
                        column: x => x.incoming_document_id,
                        principalTable: "incoming_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reminder_history_staff_recipient_staff_id",
                        column: x => x.recipient_staff_id,
                        principalTable: "staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reminder_history_incoming_document_id",
                table: "reminder_history",
                column: "incoming_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_reminder_history_recipient_status",
                table: "reminder_history",
                columns: new[] { "recipient_staff_id", "delivery_status", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ux_reminder_history_idempotency",
                table: "reminder_history",
                columns: new[] { "incoming_document_id", "recipient_staff_id", "reminder_kind", "reminder_date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reminder_history");
        }
    }
}
