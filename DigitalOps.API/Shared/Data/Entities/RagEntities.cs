using System;

namespace DigitalOps.API.Shared.Data.Entities;

public sealed class RagDocument
{
    public Guid Id { get; set; }
    public string? AuthorityNamespace { get; set; }
    public string CanonicalDocumentKey { get; set; } = null!;
    public string DocumentIdentityStrategy { get; set; } = null!;
    public string Title { get; set; } = null!;
    public Guid? ActiveVersionId { get; set; }
    public Guid? ActiveChunkSetId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public RagDocumentVersion? ActiveVersion { get; set; }
    public RagChunkSet? ActiveChunkSet { get; set; }
}

public sealed class RagDocumentVersion
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string RawArtifactUri { get; set; } = null!;
    public string RawSha256 { get; set; } = null!;
    public string MimeType { get; set; } = null!;
    public string NormalizedTextUri { get; set; } = null!;
    public string NormalizedTextSha256 { get; set; } = null!;
    public int CharCount { get; set; }
    public int WordCount { get; set; }
    public string ExtractionQualityJson { get; set; } = null!;
    public string MetadataJson { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    public RagDocument Document { get; set; } = null!;
}

public sealed class RagDocumentSource
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid VersionId { get; set; }
    public string SourceId { get; set; } = null!;
    public string SourceNamespace { get; set; } = null!;
    public string SourceDocumentUrl { get; set; } = null!;
    public DateTime CrawledAt { get; set; }

    public RagDocument Document { get; set; } = null!;
    public RagDocumentVersion Version { get; set; } = null!;
}

public sealed class RagChunkSet
{
    public Guid Id { get; set; }
    public Guid VersionId { get; set; }
    public string ChunkingStrategy { get; set; } = null!;
    public string ChunkerVersion { get; set; } = null!;
    public string TokenizerName { get; set; } = null!;
    public int TargetTokens { get; set; }
    public int OverlapTokens { get; set; }
    public int TotalChunks { get; set; }
    public DateTime CreatedAt { get; set; }

    public RagDocumentVersion Version { get; set; } = null!;
}

public sealed class RagChunk
{
    public Guid Id { get; set; }
    public Guid ChunkSetId { get; set; }
    public int ChunkIndex { get; set; }
    public string Text { get; set; } = null!;
    public int TokenCount { get; set; }
    public int CharacterStart { get; set; }
    public int CharacterEnd { get; set; }
    public string ContentSha256 { get; set; } = null!;
    public string? HeadingPath { get; set; }
    public int[] PageNumbers { get; set; } = Array.Empty<int>();
    public string StructureMetadataJson { get; set; } = null!;
    public string[] AllowedRoles { get; set; } = Array.Empty<string>();
    public string[] DeniedRoles { get; set; } = Array.Empty<string>();
    public string SecurityClassification { get; set; } = "internal";
    public DateTime CreatedAt { get; set; }

    public RagChunkSet ChunkSet { get; set; } = null!;
}

public sealed class RagIndexGeneration
{
    public Guid Id { get; set; }
    public string CollectionName { get; set; } = null!;
    public string EmbeddingModel { get; set; } = null!;
    public int EmbeddingDimension { get; set; }
    public string DistanceMetric { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime? ActivatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class RagIndexPoint
{
    public Guid PointId { get; set; }
    public Guid IndexGenerationId { get; set; }
    public Guid ChunkId { get; set; }
    public Guid ChunkSetId { get; set; }
    public Guid VersionId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid QdrantPointId { get; set; }
    public string Status { get; set; } = null!;
    public string? ErrorMessage { get; set; }
    public DateTime? IndexedAt { get; set; }

    public RagIndexGeneration IndexGeneration { get; set; } = null!;
    public RagChunk Chunk { get; set; } = null!;
}

public sealed class RagCitationSnapshot
{
    public Guid Id { get; set; }
    public string BusinessEntityType { get; set; } = null!;
    public Guid BusinessEntityId { get; set; }
    public string QueryText { get; set; } = null!;
    public Guid[] RetrievedChunkIds { get; set; } = Array.Empty<Guid>();
    public string CitationPayloadJson { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}

public sealed class RagIngestionJob
{
    public string JobId { get; set; } = null!;
    public string StagingDirectory { get; set; } = null!;
    public string Status { get; set; } = null!;
    public int TotalObservations { get; set; }
    public int ProcessedObservations { get; set; }
    public int FailedObservations { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorSummary { get; set; }
}

public sealed class RagIngestionError
{
    public Guid Id { get; set; }
    public string JobId { get; set; } = null!;
    public string Stage { get; set; } = null!;
    public string EntityType { get; set; } = null!;
    public string? EntityId { get; set; }
    public string ErrorMessage { get; set; } = null!;
    public string? StackTrace { get; set; }
    public DateTime CreatedAt { get; set; }

    public RagIngestionJob IngestionJob { get; set; } = null!;
}
