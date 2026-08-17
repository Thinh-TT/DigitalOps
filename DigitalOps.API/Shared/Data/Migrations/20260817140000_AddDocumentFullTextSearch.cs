using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DigitalOps.API.Shared.Data.Migrations;

[DbContext(typeof(DigitalOpsDbContext))]
[Migration("20260817140000_AddDocumentFullTextSearch")]
public sealed partial class AddDocumentFullTextSearch : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE INDEX IF NOT EXISTS ix_incoming_documents_summary_fts
            ON incoming_documents USING gin (to_tsvector('simple', coalesce(summary, '')));
            """);

        migrationBuilder.Sql(
            """
            CREATE INDEX IF NOT EXISTS ix_outgoing_documents_content_fts
            ON outgoing_documents USING gin (to_tsvector('simple', coalesce(title, '') || ' ' || coalesce(content, '') || ' ' || coalesce(ai_draft_content, '')));
            """);

        migrationBuilder.Sql(
            """
            CREATE INDEX IF NOT EXISTS ix_attachments_extracted_text_fts
            ON attachments USING gin (to_tsvector('simple', coalesce(extracted_text, '')));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS ix_incoming_documents_summary_fts;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS ix_outgoing_documents_content_fts;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS ix_attachments_extracted_text_fts;");
    }
}
