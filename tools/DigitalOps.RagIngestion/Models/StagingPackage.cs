using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalOps.RagIngestion.Models;

public sealed record StagingManifest(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("started_at")] DateTime StartedAt,
    [property: JsonPropertyName("completed_at")] DateTime CompletedAt,
    [property: JsonPropertyName("total_observations")] int TotalObservations,
    [property: JsonPropertyName("total_chunk_sets")] int TotalChunkSets,
    [property: JsonPropertyName("total_chunks")] int TotalChunks,
    [property: JsonPropertyName("total_errors")] int TotalErrors,
    [property: JsonPropertyName("schema_version")] string? SchemaVersion = null,
    [property: JsonPropertyName("corpus_type")] string CorpusType = "general",
    [property: JsonPropertyName("source_registry_version")] string? SourceRegistryVersion = null,
    [property: JsonPropertyName("source_registry_entry_ids")] List<string>? SourceRegistryEntryIds = null,
    [property: JsonPropertyName("files")] ManifestFilesDto? Files = null
);

public sealed record ManifestFilesDto(
    [property: JsonPropertyName("observations_file")] string ObservationsFile,
    [property: JsonPropertyName("chunk_sets_file")] string ChunkSetsFile,
    [property: JsonPropertyName("chunks_file")] string ChunksFile,
    [property: JsonPropertyName("errors_file")] string ErrorsFile
);

public sealed record ExtractionQualityDto(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("ocr_used")] bool OcrUsed,
    [property: JsonPropertyName("confidence_score")] double ConfidenceScore
);

public sealed record SourceProvenanceDto(
    [property: JsonPropertyName("registry_entry_id")] string? RegistryEntryId,
    [property: JsonPropertyName("registry_version")] string? RegistryVersion,
    [property: JsonPropertyName("corpus_type")] string CorpusType,
    [property: JsonPropertyName("source_trust_tier")] string SourceTrustTier,
    [property: JsonPropertyName("source_domain")] string SourceDomain,
    [property: JsonPropertyName("source_version")] string SourceVersion,
    [property: JsonPropertyName("publish_policy")] string PublishPolicy,
    [property: JsonPropertyName("language")] string Language
);

public sealed record LegalDocumentMetadataDto(
    [property: JsonPropertyName("document_number")] string? DocumentNumber,
    [property: JsonPropertyName("document_type")] string? DocumentType,
    [property: JsonPropertyName("issuer")] string? Issuer,
    [property: JsonPropertyName("issued_date")] DateOnly? IssuedDate,
    [property: JsonPropertyName("legal_status")] string LegalStatus,
    [property: JsonPropertyName("effective_from")] DateOnly? EffectiveFrom,
    [property: JsonPropertyName("effective_to")] DateOnly? EffectiveTo,
    [property: JsonPropertyName("amends")] List<string> Amends,
    [property: JsonPropertyName("replaces")] List<string> Replaces,
    [property: JsonPropertyName("replaced_by")] List<string> ReplacedBy
);

public sealed record DocumentObservationDto(
    [property: JsonPropertyName("observation_id")] Guid ObservationId,
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("source_id")] string SourceId,
    [property: JsonPropertyName("source_namespace")] string SourceNamespace,
    [property: JsonPropertyName("authority_namespace")] string? AuthorityNamespace,
    [property: JsonPropertyName("document_identity_strategy")] string DocumentIdentityStrategy,
    [property: JsonPropertyName("canonical_document_key")] string CanonicalDocumentKey,
    [property: JsonPropertyName("source_document_url")] string SourceDocumentUrl,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("raw_artifact_uri")] string RawArtifactUri,
    [property: JsonPropertyName("raw_sha256")] string RawSha256,
    [property: JsonPropertyName("mime_type")] string MimeType,
    [property: JsonPropertyName("normalized_text_uri")] string NormalizedTextUri,
    [property: JsonPropertyName("normalized_text_sha256")] string NormalizedTextSha256,
    [property: JsonPropertyName("char_count")] int CharCount,
    [property: JsonPropertyName("word_count")] int WordCount,
    [property: JsonPropertyName("extraction_quality")] ExtractionQualityDto ExtractionQuality,
    [property: JsonPropertyName("crawled_at")] DateTime CrawledAt,
    [property: JsonPropertyName("source_provenance")] SourceProvenanceDto? SourceProvenance = null,
    [property: JsonPropertyName("legal_metadata")] LegalDocumentMetadataDto? LegalMetadata = null,
    [property: JsonPropertyName("document_metadata")] Dictionary<string, JsonElement>? DocumentMetadata = null
);

public sealed record ChunkSetDto(
    [property: JsonPropertyName("chunk_set_id")] Guid ChunkSetId,
    [property: JsonPropertyName("observation_id")] Guid ObservationId,
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("chunking_strategy")] string ChunkingStrategy,
    [property: JsonPropertyName("chunker_version")] string ChunkerVersion,
    [property: JsonPropertyName("tokenizer_name")] string TokenizerName,
    [property: JsonPropertyName("target_tokens")] int TargetTokens,
    [property: JsonPropertyName("overlap_tokens")] int OverlapTokens,
    [property: JsonPropertyName("total_chunks")] int TotalChunks,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("soft_max_tokens")] int? SoftMaxTokens = null,
    [property: JsonPropertyName("max_tokens")] int? MaxTokens = null
);

public sealed record ChunkAclDto(
    [property: JsonPropertyName("allowed_roles")] List<string> AllowedRoles,
    [property: JsonPropertyName("denied_roles")] List<string> DeniedRoles,
    [property: JsonPropertyName("security_classification")] string SecurityClassification
);

public sealed record ChunkDto(
    [property: JsonPropertyName("chunk_id")] Guid ChunkId,
    [property: JsonPropertyName("chunk_set_id")] Guid ChunkSetId,
    [property: JsonPropertyName("chunk_index")] int ChunkIndex,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("token_count")] int TokenCount,
    [property: JsonPropertyName("character_start")] int CharacterStart,
    [property: JsonPropertyName("character_end")] int CharacterEnd,
    [property: JsonPropertyName("content_sha256")] string ContentSha256,
    [property: JsonPropertyName("heading_path")] string? HeadingPath,
    [property: JsonPropertyName("page_numbers")] List<int> PageNumbers,
    [property: JsonPropertyName("chunk_acl")] ChunkAclDto ChunkAcl,
    [property: JsonPropertyName("structure_metadata")] Dictionary<string, JsonElement>? StructureMetadata = null
);

public sealed record SourceRegistryDocument(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("registry_version")] string RegistryVersion,
    [property: JsonPropertyName("sources")] List<SourceRegistryEntryDto> Sources
);

public sealed record SourceRegistryEntryDto(
    [property: JsonPropertyName("entry_id")] string EntryId,
    [property: JsonPropertyName("adapter")] string Adapter,
    [property: JsonPropertyName("source_id")] string SourceId,
    [property: JsonPropertyName("source_namespace")] string SourceNamespace,
    [property: JsonPropertyName("authority_namespace")] string? AuthorityNamespace,
    [property: JsonPropertyName("corpus_type")] string CorpusType,
    [property: JsonPropertyName("source_trust_tier")] string SourceTrustTier,
    [property: JsonPropertyName("publish_policy")] string PublishPolicy,
    [property: JsonPropertyName("allowed_hosts")] List<string> AllowedHosts,
    [property: JsonPropertyName("default_issuer")] string? DefaultIssuer,
    [property: JsonPropertyName("language")] string Language
);

public sealed record AdmissionQuarantineItem(
    [property: JsonPropertyName("observation_id")] Guid ObservationId,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("reason")] string Reason
);

public sealed record AdmissionReceipt(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("package_digest")] string PackageDigest,
    [property: JsonPropertyName("registry_version")] string RegistryVersion,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("approved_by")] string ApprovedBy,
    [property: JsonPropertyName("approved_at")] DateTime ApprovedAt,
    [property: JsonPropertyName("approval_reference")] string ApprovalReference,
    [property: JsonPropertyName("approved_observation_ids")] List<Guid> ApprovedObservationIds,
    [property: JsonPropertyName("quarantined_observations")] List<AdmissionQuarantineItem> QuarantinedObservations
);
