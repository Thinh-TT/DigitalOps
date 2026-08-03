using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DxOs.Workers.Models;

public sealed record StagingManifest(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("started_at")] DateTime StartedAt,
    [property: JsonPropertyName("completed_at")] DateTime CompletedAt,
    [property: JsonPropertyName("total_observations")] int TotalObservations,
    [property: JsonPropertyName("total_chunk_sets")] int TotalChunkSets,
    [property: JsonPropertyName("total_chunks")] int TotalChunks,
    [property: JsonPropertyName("total_errors")] int TotalErrors
);

public sealed record ExtractionQualityDto(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("ocr_used")] bool OcrUsed,
    [property: JsonPropertyName("confidence_score")] double ConfidenceScore
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
    [property: JsonPropertyName("crawled_at")] DateTime CrawledAt
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
    [property: JsonPropertyName("created_at")] DateTime CreatedAt
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
    [property: JsonPropertyName("chunk_acl")] ChunkAclDto ChunkAcl
);
