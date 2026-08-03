using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalOps.API.Shared.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRagIngestionSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rag_citation_snapshots",
                columns: table => new
                {
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    business_entity_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    business_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    query_text = table.Column<string>(type: "text", nullable: false),
                    retrieved_chunk_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    citation_payload = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rag_citation_snapshots", x => x.snapshot_id);
                });

            migrationBuilder.CreateTable(
                name: "rag_index_generations",
                columns: table => new
                {
                    index_generation_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    collection_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    embedding_model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    embedding_dimension = table.Column<int>(type: "integer", nullable: false),
                    distance_metric = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    activated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rag_index_generations", x => x.index_generation_id);
                    table.CheckConstraint("ck_rag_index_generations_status", "status IN ('building', 'active', 'retired', 'failed')");
                });

            migrationBuilder.CreateTable(
                name: "rag_ingestion_jobs",
                columns: table => new
                {
                    job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    staging_directory = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    total_observations = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    processed_observations = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    failed_observations = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    started_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    completed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    error_summary = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rag_ingestion_jobs", x => x.job_id);
                    table.CheckConstraint("ck_rag_ingestion_jobs_status", "status IN ('running', 'completed', 'failed')");
                });

            migrationBuilder.CreateTable(
                name: "rag_ingestion_errors",
                columns: table => new
                {
                    error_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: false),
                    stack_trace = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rag_ingestion_errors", x => x.error_id);
                    table.ForeignKey(
                        name: "fk_rag_ingestion_errors_rag_ingestion_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "rag_ingestion_jobs",
                        principalColumn: "job_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rag_chunk_sets",
                columns: table => new
                {
                    chunk_set_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chunking_strategy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    chunker_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    tokenizer_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    target_tokens = table.Column<int>(type: "integer", nullable: false),
                    overlap_tokens = table.Column<int>(type: "integer", nullable: false),
                    total_chunks = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rag_chunk_sets", x => x.chunk_set_id);
                    table.CheckConstraint("ck_rag_chunk_sets_limits", "target_tokens > 0 AND overlap_tokens >= 0 AND overlap_tokens < target_tokens AND total_chunks > 0");
                });

            migrationBuilder.CreateTable(
                name: "rag_chunks",
                columns: table => new
                {
                    chunk_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    chunk_set_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    token_count = table.Column<int>(type: "integer", nullable: false),
                    character_start = table.Column<int>(type: "integer", nullable: false),
                    character_end = table.Column<int>(type: "integer", nullable: false),
                    content_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    heading_path = table.Column<string>(type: "text", nullable: true),
                    page_numbers = table.Column<int[]>(type: "integer[]", nullable: false),
                    structure_metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    allowed_roles = table.Column<string[]>(type: "character varying(64)[]", nullable: false),
                    denied_roles = table.Column<string[]>(type: "character varying(64)[]", nullable: false),
                    security_classification = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "internal"),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rag_chunks", x => x.chunk_id);
                    table.CheckConstraint("ck_rag_chunks_offsets", "character_start >= 0 AND character_end > character_start");
                    table.CheckConstraint("ck_rag_chunks_token_count", "token_count > 0 AND token_count <= 512");
                    table.ForeignKey(
                        name: "fk_rag_chunks_rag_chunk_sets_chunk_set_id",
                        column: x => x.chunk_set_id,
                        principalTable: "rag_chunk_sets",
                        principalColumn: "chunk_set_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rag_index_points",
                columns: table => new
                {
                    point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    index_generation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chunk_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chunk_set_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    qdrant_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    indexed_at = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rag_index_points", x => x.point_id);
                    table.CheckConstraint("ck_rag_index_points_status", "status IN ('pending', 'indexed', 'failed', 'deleted')");
                    table.ForeignKey(
                        name: "fk_rag_index_points_rag_chunks_chunk_id",
                        column: x => x.chunk_id,
                        principalTable: "rag_chunks",
                        principalColumn: "chunk_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_rag_index_points_rag_index_generations_index_generation_id",
                        column: x => x.index_generation_id,
                        principalTable: "rag_index_generations",
                        principalColumn: "index_generation_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rag_document_sources",
                columns: table => new
                {
                    source_mapping_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_namespace = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_document_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    crawled_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rag_document_sources", x => x.source_mapping_id);
                });

            migrationBuilder.CreateTable(
                name: "rag_document_versions",
                columns: table => new
                {
                    version_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    raw_artifact_uri = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    raw_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    normalized_text_uri = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    normalized_text_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    char_count = table.Column<int>(type: "integer", nullable: false),
                    word_count = table.Column<int>(type: "integer", nullable: false),
                    extraction_quality = table.Column<string>(type: "jsonb", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rag_document_versions", x => x.version_id);
                });

            migrationBuilder.CreateTable(
                name: "rag_documents",
                columns: table => new
                {
                    document_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    authority_namespace = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    canonical_document_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    document_identity_strategy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    active_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    active_chunk_set_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rag_documents", x => x.document_id);
                    table.ForeignKey(
                        name: "fk_rag_documents_rag_chunk_sets_active_chunk_set_id",
                        column: x => x.active_chunk_set_id,
                        principalTable: "rag_chunk_sets",
                        principalColumn: "chunk_set_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_rag_documents_rag_document_versions_active_version_id",
                        column: x => x.active_version_id,
                        principalTable: "rag_document_versions",
                        principalColumn: "version_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ux_rag_chunk_sets_version_id",
                table: "rag_chunk_sets",
                column: "version_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_rag_chunks_set_hash",
                table: "rag_chunks",
                columns: new[] { "chunk_set_id", "content_sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_rag_chunks_set_index",
                table: "rag_chunks",
                columns: new[] { "chunk_set_id", "chunk_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rag_document_sources_document_id",
                table: "rag_document_sources",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ux_rag_doc_sources_version_source_url",
                table: "rag_document_sources",
                columns: new[] { "version_id", "source_id", "source_document_url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_rag_doc_versions_doc_id",
                table: "rag_document_versions",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ux_rag_doc_versions_document_hash",
                table: "rag_document_versions",
                columns: new[] { "document_id", "normalized_text_sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_rag_docs_canonical_key",
                table: "rag_documents",
                column: "canonical_document_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rag_documents_active_chunk_set_id",
                table: "rag_documents",
                column: "active_chunk_set_id");

            migrationBuilder.CreateIndex(
                name: "ix_rag_documents_active_version_id",
                table: "rag_documents",
                column: "active_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_rag_index_points_chunk_id",
                table: "rag_index_points",
                column: "chunk_id");

            migrationBuilder.CreateIndex(
                name: "ix_rag_index_points_qdrant_point_id",
                table: "rag_index_points",
                column: "qdrant_point_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_rag_index_points_generation_chunk",
                table: "rag_index_points",
                columns: new[] { "index_generation_id", "chunk_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rag_ingestion_errors_job_id",
                table: "rag_ingestion_errors",
                column: "job_id");

            migrationBuilder.AddForeignKey(
                name: "fk_rag_chunk_sets_rag_document_versions_version_id",
                table: "rag_chunk_sets",
                column: "version_id",
                principalTable: "rag_document_versions",
                principalColumn: "version_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_rag_document_sources_rag_document_versions_version_id",
                table: "rag_document_sources",
                column: "version_id",
                principalTable: "rag_document_versions",
                principalColumn: "version_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_rag_document_sources_rag_documents_document_id",
                table: "rag_document_sources",
                column: "document_id",
                principalTable: "rag_documents",
                principalColumn: "document_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_rag_document_versions_rag_documents_document_id",
                table: "rag_document_versions",
                column: "document_id",
                principalTable: "rag_documents",
                principalColumn: "document_id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_rag_chunk_sets_rag_document_versions_version_id",
                table: "rag_chunk_sets");

            migrationBuilder.DropForeignKey(
                name: "fk_rag_documents_rag_document_versions_active_version_id",
                table: "rag_documents");

            migrationBuilder.DropTable(
                name: "rag_citation_snapshots");

            migrationBuilder.DropTable(
                name: "rag_document_sources");

            migrationBuilder.DropTable(
                name: "rag_index_points");

            migrationBuilder.DropTable(
                name: "rag_ingestion_errors");

            migrationBuilder.DropTable(
                name: "rag_chunks");

            migrationBuilder.DropTable(
                name: "rag_index_generations");

            migrationBuilder.DropTable(
                name: "rag_ingestion_jobs");

            migrationBuilder.DropTable(
                name: "rag_document_versions");

            migrationBuilder.DropTable(
                name: "rag_documents");

            migrationBuilder.DropTable(
                name: "rag_chunk_sets");
        }
    }
}
