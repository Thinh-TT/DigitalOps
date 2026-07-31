using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalOps.API.Shared.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIncomingAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    incoming_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    uploaded_by_staff_id = table.Column<Guid>(type: "uuid", nullable: false),
                    extraction_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    extracted_text = table.Column<string>(type: "text", nullable: true),
                    extraction_error = table.Column<string>(type: "text", nullable: true),
                    extracted_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    uploaded_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attachments", x => x.id);
                    table.CheckConstraint("ck_attachments_extraction_status", "extraction_status IN ('Pending', 'Processing', 'Succeeded', 'Failed', 'Unsupported')");
                    table.CheckConstraint("ck_attachments_failed_error", "extraction_status <> 'Failed' OR (extraction_error IS NOT NULL AND length(trim(extraction_error)) > 0)");
                    table.CheckConstraint("ck_attachments_succeeded_extracted_at", "extraction_status <> 'Succeeded' OR extracted_at IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_attachments_incoming_documents_incoming_document_id",
                        column: x => x.incoming_document_id,
                        principalTable: "incoming_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_attachments_staff_uploaded_by_staff_id",
                        column: x => x.uploaded_by_staff_id,
                        principalTable: "staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_extraction_status",
                table: "attachments",
                column: "extraction_status");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_incoming_document_id",
                table: "attachments",
                column: "incoming_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_uploaded_by_staff_id",
                table: "attachments",
                column: "uploaded_by_staff_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attachments");
        }
    }
}
