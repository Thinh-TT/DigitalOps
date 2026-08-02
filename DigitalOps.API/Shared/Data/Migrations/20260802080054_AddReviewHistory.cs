using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalOps.API.Shared.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "review_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    outgoing_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_no = table.Column<int>(type: "integer", nullable: false),
                    review_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reviewed_by_staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    content_snapshot = table.Column<string>(type: "text", nullable: false),
                    review_result = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    review_issues = table.Column<JsonElement>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    reviewed_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_history", x => x.id);
                    table.CheckConstraint("ck_review_history_attempt_no", "attempt_no > 0");
                    table.CheckConstraint("ck_review_history_issues_array", "jsonb_typeof(review_issues) = 'array'");
                    table.CheckConstraint("ck_review_history_result", "review_result IN ('Failed', 'Passed')");
                    table.CheckConstraint("ck_review_history_source", "review_source IN ('Rule', 'AI', 'Hybrid')");
                    table.ForeignKey(
                        name: "fk_review_history_outgoing_documents_outgoing_document_id",
                        column: x => x.outgoing_document_id,
                        principalTable: "outgoing_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_review_history_staff_reviewed_by_staff_id",
                        column: x => x.reviewed_by_staff_id,
                        principalTable: "staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_review_history_document_reviewed_at",
                table: "review_history",
                columns: new[] { "outgoing_document_id", "reviewed_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_review_history_reviewed_by_staff_id",
                table: "review_history",
                column: "reviewed_by_staff_id");

            migrationBuilder.CreateIndex(
                name: "ux_review_history_document_attempt",
                table: "review_history",
                columns: new[] { "outgoing_document_id", "attempt_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "review_history");
        }
    }
}
