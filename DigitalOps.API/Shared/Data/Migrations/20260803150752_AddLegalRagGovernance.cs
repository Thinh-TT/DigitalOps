using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalOps.API.Shared.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalRagGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_rag_chunk_sets_limits",
                table: "rag_chunk_sets");

            migrationBuilder.AddColumn<string>(
                name: "document_number",
                table: "rag_document_versions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "document_type",
                table: "rag_document_versions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "effective_from",
                table: "rag_document_versions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "effective_to",
                table: "rag_document_versions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "issued_date",
                table: "rag_document_versions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "issuer",
                table: "rag_document_versions",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "language",
                table: "rag_document_versions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "legal_status",
                table: "rag_document_versions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "status_unknown");

            migrationBuilder.AddColumn<string>(
                name: "source_version",
                table: "rag_document_versions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "admission_approved_at",
                table: "rag_document_sources",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "admission_approved_by",
                table: "rag_document_sources",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "admission_reference",
                table: "rag_document_sources",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "corpus_type",
                table: "rag_document_sources",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "general");

            migrationBuilder.AddColumn<string>(
                name: "publish_policy",
                table: "rag_document_sources",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "blocked");

            migrationBuilder.AddColumn<string>(
                name: "registry_entry_id",
                table: "rag_document_sources",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "registry_version",
                table: "rag_document_sources",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_domain",
                table: "rag_document_sources",
                type: "character varying(253)",
                maxLength: 253,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_trust_tier",
                table: "rag_document_sources",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "unverified");

            migrationBuilder.AddColumn<int>(
                name: "max_tokens",
                table: "rag_chunk_sets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "soft_max_tokens",
                table: "rag_chunk_sets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_rag_document_versions_effectivity",
                table: "rag_document_versions",
                sql: "effective_from IS NULL OR effective_to IS NULL OR effective_from <= effective_to");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rag_document_versions_legal_status",
                table: "rag_document_versions",
                sql: "legal_status IN ('current', 'expired', 'repealed', 'superseded', 'status_unknown')");

            migrationBuilder.CreateIndex(
                name: "idx_rag_doc_sources_corpus_trust",
                table: "rag_document_sources",
                columns: new[] { "corpus_type", "source_trust_tier" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_rag_document_sources_corpus",
                table: "rag_document_sources",
                sql: "corpus_type IN ('general', 'legal_reference')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rag_document_sources_publish_policy",
                table: "rag_document_sources",
                sql: "publish_policy IN ('authoritative', 'verified_copy', 'cross_check_only', 'blocked')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rag_document_sources_trust_policy_pair",
                table: "rag_document_sources",
                sql: "(source_trust_tier = 'official' AND publish_policy = 'authoritative') OR (source_trust_tier = 'verified_copy' AND publish_policy = 'verified_copy') OR (source_trust_tier = 'aggregator' AND publish_policy = 'cross_check_only') OR (source_trust_tier = 'unverified' AND publish_policy = 'blocked')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rag_document_sources_trust",
                table: "rag_document_sources",
                sql: "source_trust_tier IN ('official', 'verified_copy', 'aggregator', 'unverified')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rag_chunk_sets_limits",
                table: "rag_chunk_sets",
                sql: "target_tokens > 0 AND overlap_tokens >= 0 AND overlap_tokens < target_tokens AND (soft_max_tokens IS NULL OR (soft_max_tokens >= target_tokens AND soft_max_tokens <= 512)) AND (max_tokens IS NULL OR (max_tokens >= COALESCE(soft_max_tokens, target_tokens) AND max_tokens <= 512)) AND total_chunks > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_rag_document_versions_effectivity",
                table: "rag_document_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rag_document_versions_legal_status",
                table: "rag_document_versions");

            migrationBuilder.DropIndex(
                name: "idx_rag_doc_sources_corpus_trust",
                table: "rag_document_sources");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rag_document_sources_corpus",
                table: "rag_document_sources");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rag_document_sources_publish_policy",
                table: "rag_document_sources");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rag_document_sources_trust_policy_pair",
                table: "rag_document_sources");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rag_document_sources_trust",
                table: "rag_document_sources");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rag_chunk_sets_limits",
                table: "rag_chunk_sets");

            migrationBuilder.DropColumn(
                name: "document_number",
                table: "rag_document_versions");

            migrationBuilder.DropColumn(
                name: "document_type",
                table: "rag_document_versions");

            migrationBuilder.DropColumn(
                name: "effective_from",
                table: "rag_document_versions");

            migrationBuilder.DropColumn(
                name: "effective_to",
                table: "rag_document_versions");

            migrationBuilder.DropColumn(
                name: "issued_date",
                table: "rag_document_versions");

            migrationBuilder.DropColumn(
                name: "issuer",
                table: "rag_document_versions");

            migrationBuilder.DropColumn(
                name: "language",
                table: "rag_document_versions");

            migrationBuilder.DropColumn(
                name: "legal_status",
                table: "rag_document_versions");

            migrationBuilder.DropColumn(
                name: "source_version",
                table: "rag_document_versions");

            migrationBuilder.DropColumn(
                name: "admission_approved_at",
                table: "rag_document_sources");

            migrationBuilder.DropColumn(
                name: "admission_approved_by",
                table: "rag_document_sources");

            migrationBuilder.DropColumn(
                name: "admission_reference",
                table: "rag_document_sources");

            migrationBuilder.DropColumn(
                name: "corpus_type",
                table: "rag_document_sources");

            migrationBuilder.DropColumn(
                name: "publish_policy",
                table: "rag_document_sources");

            migrationBuilder.DropColumn(
                name: "registry_entry_id",
                table: "rag_document_sources");

            migrationBuilder.DropColumn(
                name: "registry_version",
                table: "rag_document_sources");

            migrationBuilder.DropColumn(
                name: "source_domain",
                table: "rag_document_sources");

            migrationBuilder.DropColumn(
                name: "source_trust_tier",
                table: "rag_document_sources");

            migrationBuilder.DropColumn(
                name: "max_tokens",
                table: "rag_chunk_sets");

            migrationBuilder.DropColumn(
                name: "soft_max_tokens",
                table: "rag_chunk_sets");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rag_chunk_sets_limits",
                table: "rag_chunk_sets",
                sql: "target_tokens > 0 AND overlap_tokens >= 0 AND overlap_tokens < target_tokens AND total_chunks > 0");
        }
    }
}
