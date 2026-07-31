using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalOps.API.Shared.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIncomingDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "incoming_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    reference_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sender_org = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    summary = table.Column<string>(type: "text", nullable: false),
                    received_date = table.Column<DateOnly>(type: "date", nullable: false),
                    deadline = table.Column<DateOnly>(type: "date", nullable: false),
                    document_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    suggested_staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assignment_suggestion_reason = table.Column<string>(type: "text", nullable: true),
                    assignment_confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    assignment_suggested_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    assigned_to_staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assignment_confirmed_by_staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assignment_confirmed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "New"),
                    completed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incoming_documents", x => x.id);
                    table.CheckConstraint("ck_incoming_documents_assignment_confidence", "assignment_confidence IS NULL OR (assignment_confidence >= 0 AND assignment_confidence <= 1)");
                    table.CheckConstraint("ck_incoming_documents_assignment_tuple", "(assigned_to_staff_id IS NULL AND assignment_confirmed_by_staff_id IS NULL AND assignment_confirmed_at IS NULL) OR (assigned_to_staff_id IS NOT NULL AND assignment_confirmed_by_staff_id IS NOT NULL AND assignment_confirmed_at IS NOT NULL)");
                    table.CheckConstraint("ck_incoming_documents_received_deadline", "received_date <= deadline");
                    table.CheckConstraint("ck_incoming_documents_status", "status IN ('New', 'InProgress', 'Completed', 'Overdue')");
                    table.CheckConstraint("ck_incoming_documents_status_completed_at", "(status = 'Completed' AND completed_at IS NOT NULL) OR (status <> 'Completed' AND completed_at IS NULL)");
                    table.CheckConstraint("ck_incoming_documents_suggestion_tuple", "(suggested_staff_id IS NULL AND assignment_suggestion_reason IS NULL AND assignment_confidence IS NULL AND assignment_suggested_at IS NULL) OR (suggested_staff_id IS NOT NULL AND assignment_suggested_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_incoming_documents_document_types_document_type_id",
                        column: x => x.document_type_id,
                        principalTable: "document_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incoming_documents_staff_assigned_to_staff_id",
                        column: x => x.assigned_to_staff_id,
                        principalTable: "staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incoming_documents_staff_assignment_confirmed_by_staff_id",
                        column: x => x.assignment_confirmed_by_staff_id,
                        principalTable: "staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incoming_documents_staff_suggested_staff_id",
                        column: x => x.suggested_staff_id,
                        principalTable: "staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_incoming_documents_assigned_status",
                table: "incoming_documents",
                columns: new[] { "assigned_to_staff_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_incoming_documents_confirmed_by_staff_id",
                table: "incoming_documents",
                column: "assignment_confirmed_by_staff_id");

            migrationBuilder.CreateIndex(
                name: "ix_incoming_documents_document_type_id",
                table: "incoming_documents",
                column: "document_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_incoming_documents_reference_sender",
                table: "incoming_documents",
                columns: new[] { "reference_number", "sender_org" });

            migrationBuilder.CreateIndex(
                name: "ix_incoming_documents_status_deadline",
                table: "incoming_documents",
                columns: new[] { "status", "deadline" });

            migrationBuilder.CreateIndex(
                name: "ix_incoming_documents_suggested_staff_id",
                table: "incoming_documents",
                column: "suggested_staff_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incoming_documents");
        }
    }
}
