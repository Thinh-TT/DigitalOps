using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalOps.API.Shared.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOutgoingDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "incoming_document_id",
                table: "attachments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "outgoing_document_id",
                table: "attachments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "outgoing_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    related_incoming_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    related_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    ai_draft_content = table.Column<string>(type: "text", nullable: true),
                    drafted_by_staff_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Editing"),
                    review_issues = table.Column<JsonElement>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    approved_by_staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    reference_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    issued_date = table.Column<DateOnly>(type: "date", nullable: true),
                    archived_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outgoing_documents", x => x.id);
                    table.CheckConstraint("ck_outgoing_documents_ai_draft_content", "status <> 'AiDraft' OR ai_draft_content IS NOT NULL");
                    table.CheckConstraint("ck_outgoing_documents_approved_status", "status NOT IN ('Approved', 'Archived') OR (approved_by_staff_id IS NOT NULL AND approved_at IS NOT NULL)");
                    table.CheckConstraint("ck_outgoing_documents_approved_tuple", "(approved_by_staff_id IS NULL AND approved_at IS NULL) OR (approved_by_staff_id IS NOT NULL AND approved_at IS NOT NULL)");
                    table.CheckConstraint("ck_outgoing_documents_archived_tuple", "(status = 'Archived' AND archived_at IS NOT NULL AND reference_number IS NOT NULL AND issued_date IS NOT NULL) OR (status <> 'Archived' AND archived_at IS NULL)");
                    table.CheckConstraint("ck_outgoing_documents_reference_tuple", "(reference_number IS NULL AND issued_date IS NULL) OR (reference_number IS NOT NULL AND issued_date IS NOT NULL)");
                    table.CheckConstraint("ck_outgoing_documents_review_issues_array", "jsonb_typeof(review_issues) = 'array'");
                    table.CheckConstraint("ck_outgoing_documents_status", "status IN ('AiDraft', 'Editing', 'PendingReview', 'ReviewFailed', 'PendingApproval', 'Approved', 'Archived')");
                    table.ForeignKey(
                        name: "fk_outgoing_documents_document_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "document_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_outgoing_documents_incoming_documents_related_incoming_docu",
                        column: x => x.related_incoming_document_id,
                        principalTable: "incoming_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_outgoing_documents_members_related_member_id",
                        column: x => x.related_member_id,
                        principalTable: "members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_outgoing_documents_staff_approved_by_staff_id",
                        column: x => x.approved_by_staff_id,
                        principalTable: "staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_outgoing_documents_staff_drafted_by_staff_id",
                        column: x => x.drafted_by_staff_id,
                        principalTable: "staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_outgoing_document_id",
                table: "attachments",
                column: "outgoing_document_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_attachments_exactly_one_parent",
                table: "attachments",
                sql: "num_nonnulls(incoming_document_id, outgoing_document_id) = 1");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_documents_approved_by_staff_id",
                table: "outgoing_documents",
                column: "approved_by_staff_id");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_documents_created_at",
                table: "outgoing_documents",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_documents_drafted_by_staff_id",
                table: "outgoing_documents",
                column: "drafted_by_staff_id");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_documents_related_incoming_document_id",
                table: "outgoing_documents",
                column: "related_incoming_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_documents_related_member_id",
                table: "outgoing_documents",
                column: "related_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_documents_status",
                table: "outgoing_documents",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_documents_template_id",
                table: "outgoing_documents",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ux_outgoing_documents_reference_number",
                table: "outgoing_documents",
                column: "reference_number",
                unique: true,
                filter: "reference_number IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_attachments_outgoing_documents_outgoing_document_id",
                table: "attachments",
                column: "outgoing_document_id",
                principalTable: "outgoing_documents",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_attachments_outgoing_documents_outgoing_document_id",
                table: "attachments");

            migrationBuilder.DropTable(
                name: "outgoing_documents");

            migrationBuilder.DropIndex(
                name: "ix_attachments_outgoing_document_id",
                table: "attachments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_attachments_exactly_one_parent",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "outgoing_document_id",
                table: "attachments");

            migrationBuilder.AlterColumn<Guid>(
                name: "incoming_document_id",
                table: "attachments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
