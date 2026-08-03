using DigitalOps.API.Shared.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalOps.API.Shared.Data.Configurations;

public sealed class RagDocumentConfiguration : IEntityTypeConfiguration<RagDocument>
{
    public void Configure(EntityTypeBuilder<RagDocument> builder)
    {
        builder.ToTable("rag_documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("document_id").ValueGeneratedOnAdd().HasDefaultValueSql("gen_random_uuid()");
        builder.Property(d => d.AuthorityNamespace).HasColumnName("authority_namespace").HasMaxLength(128);
        builder.Property(d => d.CanonicalDocumentKey).HasColumnName("canonical_document_key").HasMaxLength(256).IsRequired();
        builder.Property(d => d.DocumentIdentityStrategy).HasColumnName("document_identity_strategy").HasMaxLength(32).IsRequired();
        builder.Property(d => d.Title).HasColumnName("title").HasMaxLength(512).IsRequired();
        builder.Property(d => d.ActiveVersionId).HasColumnName("active_version_id");
        builder.Property(d => d.ActiveChunkSetId).HasColumnName("active_chunk_set_id");
        builder.Property(d => d.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(d => d.ActiveVersion).WithMany().HasForeignKey(d => d.ActiveVersionId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(d => d.ActiveChunkSet).WithMany().HasForeignKey(d => d.ActiveChunkSetId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(d => d.CanonicalDocumentKey).IsUnique().HasDatabaseName("idx_rag_docs_canonical_key");
    }
}

public sealed class RagDocumentVersionConfiguration : IEntityTypeConfiguration<RagDocumentVersion>
{
    public void Configure(EntityTypeBuilder<RagDocumentVersion> builder)
    {
        builder.ToTable("rag_document_versions", table =>
        {
            table.HasCheckConstraint(
                "ck_rag_document_versions_legal_status",
                "legal_status IN ('current', 'expired', 'repealed', 'superseded', 'status_unknown')");
            table.HasCheckConstraint(
                "ck_rag_document_versions_effectivity",
                "effective_from IS NULL OR effective_to IS NULL OR effective_from <= effective_to");
        });
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).HasColumnName("version_id").ValueGeneratedOnAdd().HasDefaultValueSql("gen_random_uuid()");
        builder.Property(v => v.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(v => v.RawArtifactUri).HasColumnName("raw_artifact_uri").HasMaxLength(1024).IsRequired();
        builder.Property(v => v.RawSha256).HasColumnName("raw_sha256").HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(v => v.MimeType).HasColumnName("mime_type").HasMaxLength(128).IsRequired();
        builder.Property(v => v.NormalizedTextUri).HasColumnName("normalized_text_uri").HasMaxLength(1024).IsRequired();
        builder.Property(v => v.NormalizedTextSha256).HasColumnName("normalized_text_sha256").HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(v => v.CharCount).HasColumnName("char_count").IsRequired();
        builder.Property(v => v.WordCount).HasColumnName("word_count").IsRequired();
        builder.Property(v => v.ExtractionQualityJson).HasColumnName("extraction_quality").HasColumnType("jsonb").IsRequired();
        builder.Property(v => v.DocumentNumber).HasColumnName("document_number").HasMaxLength(256);
        builder.Property(v => v.DocumentType).HasColumnName("document_type").HasMaxLength(128);
        builder.Property(v => v.Issuer).HasColumnName("issuer").HasMaxLength(512);
        builder.Property(v => v.IssuedDate).HasColumnName("issued_date").HasColumnType("date");
        builder.Property(v => v.LegalStatus).HasColumnName("legal_status").HasMaxLength(32).HasDefaultValue("status_unknown").IsRequired();
        builder.Property(v => v.EffectiveFrom).HasColumnName("effective_from").HasColumnType("date");
        builder.Property(v => v.EffectiveTo).HasColumnName("effective_to").HasColumnType("date");
        builder.Property(v => v.SourceVersion).HasColumnName("source_version").HasMaxLength(256);
        builder.Property(v => v.Language).HasColumnName("language").HasMaxLength(16);
        builder.Property(v => v.MetadataJson).HasColumnName("metadata").HasColumnType("jsonb").HasDefaultValue("{}").IsRequired();
        builder.Property(v => v.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(v => v.Document).WithMany().HasForeignKey(v => v.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(v => v.DocumentId).HasDatabaseName("idx_rag_doc_versions_doc_id");
        builder.HasIndex(v => new { v.DocumentId, v.NormalizedTextSha256 })
            .IsUnique()
            .HasDatabaseName("ux_rag_doc_versions_document_hash");
    }
}

public sealed class RagDocumentSourceConfiguration : IEntityTypeConfiguration<RagDocumentSource>
{
    public void Configure(EntityTypeBuilder<RagDocumentSource> builder)
    {
        builder.ToTable("rag_document_sources", table =>
        {
            table.HasCheckConstraint(
                "ck_rag_document_sources_trust",
                "source_trust_tier IN ('official', 'verified_copy', 'aggregator', 'unverified')");
            table.HasCheckConstraint(
                "ck_rag_document_sources_corpus",
                "corpus_type IN ('general', 'legal_reference')");
            table.HasCheckConstraint(
                "ck_rag_document_sources_publish_policy",
                "publish_policy IN ('authoritative', 'verified_copy', 'cross_check_only', 'blocked')");
            table.HasCheckConstraint(
                "ck_rag_document_sources_trust_policy_pair",
                "(source_trust_tier = 'official' AND publish_policy = 'authoritative') OR (source_trust_tier = 'verified_copy' AND publish_policy = 'verified_copy') OR (source_trust_tier = 'aggregator' AND publish_policy = 'cross_check_only') OR (source_trust_tier = 'unverified' AND publish_policy = 'blocked')");
        });
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("source_mapping_id").ValueGeneratedOnAdd().HasDefaultValueSql("gen_random_uuid()");
        builder.Property(s => s.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(s => s.VersionId).HasColumnName("version_id").IsRequired();
        builder.Property(s => s.SourceId).HasColumnName("source_id").HasMaxLength(128).IsRequired();
        builder.Property(s => s.SourceNamespace).HasColumnName("source_namespace").HasMaxLength(128).IsRequired();
        builder.Property(s => s.SourceDocumentUrl).HasColumnName("source_document_url").HasMaxLength(1024).IsRequired();
        builder.Property(s => s.RegistryEntryId).HasColumnName("registry_entry_id").HasMaxLength(128);
        builder.Property(s => s.RegistryVersion).HasColumnName("registry_version").HasMaxLength(64);
        builder.Property(s => s.SourceDomain).HasColumnName("source_domain").HasMaxLength(253);
        builder.Property(s => s.SourceTrustTier).HasColumnName("source_trust_tier").HasMaxLength(32).HasDefaultValue("unverified").IsRequired();
        builder.Property(s => s.CorpusType).HasColumnName("corpus_type").HasMaxLength(32).HasDefaultValue("general").IsRequired();
        builder.Property(s => s.PublishPolicy).HasColumnName("publish_policy").HasMaxLength(32).HasDefaultValue("blocked").IsRequired();
        builder.Property(s => s.AdmissionReference).HasColumnName("admission_reference").HasMaxLength(256);
        builder.Property(s => s.AdmissionApprovedBy).HasColumnName("admission_approved_by").HasMaxLength(256);
        builder.Property(s => s.AdmissionApprovedAt).HasColumnName("admission_approved_at").HasColumnType("timestamptz");
        builder.Property(s => s.CrawledAt).HasColumnName("crawled_at").HasColumnType("timestamptz").IsRequired();

        builder.HasOne(s => s.Document).WithMany().HasForeignKey(s => s.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.Version).WithMany().HasForeignKey(s => s.VersionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(s => new { s.VersionId, s.SourceId, s.SourceDocumentUrl })
            .IsUnique()
            .HasDatabaseName("ux_rag_doc_sources_version_source_url");
        builder.HasIndex(s => new { s.CorpusType, s.SourceTrustTier })
            .HasDatabaseName("idx_rag_doc_sources_corpus_trust");
    }
}

public sealed class RagChunkSetConfiguration : IEntityTypeConfiguration<RagChunkSet>
{
    public void Configure(EntityTypeBuilder<RagChunkSet> builder)
    {
        builder.ToTable("rag_chunk_sets", table =>
        {
            table.HasCheckConstraint(
                "ck_rag_chunk_sets_limits",
                "target_tokens > 0 AND overlap_tokens >= 0 AND overlap_tokens < target_tokens AND (soft_max_tokens IS NULL OR (soft_max_tokens >= target_tokens AND soft_max_tokens <= 512)) AND (max_tokens IS NULL OR (max_tokens >= COALESCE(soft_max_tokens, target_tokens) AND max_tokens <= 512)) AND total_chunks > 0");
        });
        builder.HasKey(cs => cs.Id);
        builder.Property(cs => cs.Id).HasColumnName("chunk_set_id").ValueGeneratedOnAdd().HasDefaultValueSql("gen_random_uuid()");
        builder.Property(cs => cs.VersionId).HasColumnName("version_id").IsRequired();
        builder.Property(cs => cs.ChunkingStrategy).HasColumnName("chunking_strategy").HasMaxLength(64).IsRequired();
        builder.Property(cs => cs.ChunkerVersion).HasColumnName("chunker_version").HasMaxLength(32).IsRequired();
        builder.Property(cs => cs.TokenizerName).HasColumnName("tokenizer_name").HasMaxLength(128).IsRequired();
        builder.Property(cs => cs.TargetTokens).HasColumnName("target_tokens").IsRequired();
        builder.Property(cs => cs.SoftMaxTokens).HasColumnName("soft_max_tokens");
        builder.Property(cs => cs.MaxTokens).HasColumnName("max_tokens");
        builder.Property(cs => cs.OverlapTokens).HasColumnName("overlap_tokens").IsRequired();
        builder.Property(cs => cs.TotalChunks).HasColumnName("total_chunks").IsRequired();
        builder.Property(cs => cs.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(cs => cs.Version).WithMany().HasForeignKey(cs => cs.VersionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(cs => cs.VersionId)
            .IsUnique()
            .HasDatabaseName("ux_rag_chunk_sets_version_id");
    }
}

public sealed class RagChunkConfiguration : IEntityTypeConfiguration<RagChunk>
{
    public void Configure(EntityTypeBuilder<RagChunk> builder)
    {
        builder.ToTable("rag_chunks", table =>
        {
            table.HasCheckConstraint(
                "ck_rag_chunks_token_count",
                "token_count > 0 AND token_count <= 512");
            table.HasCheckConstraint(
                "ck_rag_chunks_offsets",
                "character_start >= 0 AND character_end > character_start");
        });
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("chunk_id").ValueGeneratedOnAdd().HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.ChunkSetId).HasColumnName("chunk_set_id").IsRequired();
        builder.Property(c => c.ChunkIndex).HasColumnName("chunk_index").IsRequired();
        builder.Property(c => c.Text).HasColumnName("text").IsRequired();
        builder.Property(c => c.TokenCount).HasColumnName("token_count").IsRequired();
        builder.Property(c => c.CharacterStart).HasColumnName("character_start").IsRequired();
        builder.Property(c => c.CharacterEnd).HasColumnName("character_end").IsRequired();
        builder.Property(c => c.ContentSha256).HasColumnName("content_sha256").HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(c => c.HeadingPath).HasColumnName("heading_path");
        builder.Property(c => c.PageNumbers).HasColumnName("page_numbers").HasColumnType("integer[]").IsRequired();
        builder.Property(c => c.StructureMetadataJson).HasColumnName("structure_metadata").HasColumnType("jsonb").HasDefaultValue("{}").IsRequired();
        builder.Property(c => c.AllowedRoles).HasColumnName("allowed_roles")
            .HasColumnType("character varying(64)[]")
            .IsRequired();
        builder.Property(c => c.DeniedRoles).HasColumnName("denied_roles")
            .HasColumnType("character varying(64)[]")
            .IsRequired();
        builder.Property(c => c.SecurityClassification).HasColumnName("security_classification").HasMaxLength(32).HasDefaultValue("internal").IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(c => c.ChunkSet).WithMany().HasForeignKey(c => c.ChunkSetId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => new { c.ChunkSetId, c.ChunkIndex })
            .IsUnique()
            .HasDatabaseName("ux_rag_chunks_set_index");
        builder.HasIndex(c => new { c.ChunkSetId, c.ContentSha256 })
            .IsUnique()
            .HasDatabaseName("ux_rag_chunks_set_hash");
    }
}

public sealed class RagIndexGenerationConfiguration : IEntityTypeConfiguration<RagIndexGeneration>
{
    public void Configure(EntityTypeBuilder<RagIndexGeneration> builder)
    {
        builder.ToTable("rag_index_generations", table =>
        {
            table.HasCheckConstraint(
                "ck_rag_index_generations_status",
                "status IN ('building', 'active', 'retired', 'failed')");
        });
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).HasColumnName("index_generation_id").ValueGeneratedOnAdd().HasDefaultValueSql("gen_random_uuid()");
        builder.Property(g => g.CollectionName).HasColumnName("collection_name").HasMaxLength(128).IsRequired();
        builder.Property(g => g.EmbeddingModel).HasColumnName("embedding_model").HasMaxLength(128).IsRequired();
        builder.Property(g => g.EmbeddingDimension).HasColumnName("embedding_dimension").IsRequired();
        builder.Property(g => g.DistanceMetric).HasColumnName("distance_metric").HasMaxLength(32).IsRequired();
        builder.Property(g => g.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(g => g.ActivatedAt).HasColumnName("activated_at").HasColumnType("timestamptz");
        builder.Property(g => g.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}

public sealed class RagIndexPointConfiguration : IEntityTypeConfiguration<RagIndexPoint>
{
    public void Configure(EntityTypeBuilder<RagIndexPoint> builder)
    {
        builder.ToTable("rag_index_points", table =>
        {
            table.HasCheckConstraint(
                "ck_rag_index_points_status",
                "status IN ('pending', 'indexed', 'failed', 'deleted')");
        });
        builder.HasKey(p => p.PointId);
        builder.Property(p => p.PointId).HasColumnName("point_id");
        builder.Property(p => p.IndexGenerationId).HasColumnName("index_generation_id").IsRequired();
        builder.Property(p => p.ChunkId).HasColumnName("chunk_id").IsRequired();
        builder.Property(p => p.ChunkSetId).HasColumnName("chunk_set_id").IsRequired();
        builder.Property(p => p.VersionId).HasColumnName("version_id").IsRequired();
        builder.Property(p => p.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(p => p.QdrantPointId).HasColumnName("qdrant_point_id").IsRequired();
        builder.Property(p => p.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(p => p.ErrorMessage).HasColumnName("error_message");
        builder.Property(p => p.IndexedAt).HasColumnName("indexed_at").HasColumnType("timestamptz");

        builder.HasOne(p => p.IndexGeneration).WithMany().HasForeignKey(p => p.IndexGenerationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(p => p.Chunk).WithMany().HasForeignKey(p => p.ChunkId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => p.QdrantPointId).IsUnique();
        builder.HasIndex(p => new { p.IndexGenerationId, p.ChunkId })
            .IsUnique()
            .HasDatabaseName("ux_rag_index_points_generation_chunk");
    }
}

public sealed class RagCitationSnapshotConfiguration : IEntityTypeConfiguration<RagCitationSnapshot>
{
    public void Configure(EntityTypeBuilder<RagCitationSnapshot> builder)
    {
        builder.ToTable("rag_citation_snapshots");
        builder.HasKey(cs => cs.Id);
        builder.Property(cs => cs.Id).HasColumnName("snapshot_id").ValueGeneratedOnAdd().HasDefaultValueSql("gen_random_uuid()");
        builder.Property(cs => cs.BusinessEntityType).HasColumnName("business_entity_type").HasMaxLength(128).IsRequired();
        builder.Property(cs => cs.BusinessEntityId).HasColumnName("business_entity_id").IsRequired();
        builder.Property(cs => cs.QueryText).HasColumnName("query_text").IsRequired();
        builder.Property(cs => cs.RetrievedChunkIds).HasColumnName("retrieved_chunk_ids")
            .HasColumnType("uuid[]")
            .IsRequired();
        builder.Property(cs => cs.CitationPayloadJson).HasColumnName("citation_payload").HasColumnType("jsonb").IsRequired();
        builder.Property(cs => cs.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}

public sealed class RagIngestionJobConfiguration : IEntityTypeConfiguration<RagIngestionJob>
{
    public void Configure(EntityTypeBuilder<RagIngestionJob> builder)
    {
        builder.ToTable("rag_ingestion_jobs", table =>
        {
            table.HasCheckConstraint(
                "ck_rag_ingestion_jobs_status",
                "status IN ('running', 'completed', 'failed')");
        });
        builder.HasKey(j => j.JobId);
        builder.Property(j => j.JobId).HasColumnName("job_id").HasMaxLength(128);
        builder.Property(j => j.StagingDirectory).HasColumnName("staging_directory").HasMaxLength(1024).IsRequired();
        builder.Property(j => j.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(j => j.TotalObservations).HasColumnName("total_observations").HasDefaultValue(0);
        builder.Property(j => j.ProcessedObservations).HasColumnName("processed_observations").HasDefaultValue(0);
        builder.Property(j => j.FailedObservations).HasColumnName("failed_observations").HasDefaultValue(0);
        builder.Property(j => j.StartedAt).HasColumnName("started_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(j => j.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamptz");
        builder.Property(j => j.ErrorSummary).HasColumnName("error_summary");
    }
}

public sealed class RagIngestionErrorConfiguration : IEntityTypeConfiguration<RagIngestionError>
{
    public void Configure(EntityTypeBuilder<RagIngestionError> builder)
    {
        builder.ToTable("rag_ingestion_errors");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("error_id").ValueGeneratedOnAdd().HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.JobId).HasColumnName("job_id").HasMaxLength(128).IsRequired();
        builder.Property(e => e.Stage).HasColumnName("stage").HasMaxLength(32).IsRequired();
        builder.Property(e => e.EntityType).HasColumnName("entity_type").HasMaxLength(64).IsRequired();
        builder.Property(e => e.EntityId).HasColumnName("entity_id").HasMaxLength(256);
        builder.Property(e => e.ErrorMessage).HasColumnName("error_message").IsRequired();
        builder.Property(e => e.StackTrace).HasColumnName("stack_trace");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(e => e.IngestionJob).WithMany().HasForeignKey(e => e.JobId).OnDelete(DeleteBehavior.Cascade);
    }
}
