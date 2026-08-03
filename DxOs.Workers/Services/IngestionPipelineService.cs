using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalOps.API.Shared.Data;
using DigitalOps.API.Shared.Data.Entities;
using DxOs.Workers.Models;
using Microsoft.EntityFrameworkCore;

namespace DxOs.Workers.Services;

public sealed class IngestionPipelineService
{
    public const string CollectionName = "digitalops_knowledge_v1";
    public const string EmbeddingModel = "qwen3-embedding:0.6b";
    public const int EmbeddingDimensions = 1024;

    private readonly DigitalOpsDbContext _dbContext;
    private readonly IOllamaEmbeddingService _embeddingService;
    private readonly IQdrantIngestionClient _qdrantClient;

    public IngestionPipelineService(
        DigitalOpsDbContext dbContext,
        IOllamaEmbeddingService embeddingService,
        IQdrantIngestionClient qdrantClient)
    {
        _dbContext = dbContext;
        _embeddingService = embeddingService;
        _qdrantClient = qdrantClient;
    }

    public static Guid ComputeQdrantPointId(
        Guid indexGenerationId,
        Guid chunkId)
    {
        var bytes = Encoding.UTF8.GetBytes(
            $"{indexGenerationId:D}|{chunkId:D}");
        var hash = SHA256.HashData(bytes);
        return new Guid(hash.AsSpan(0, 16));
    }

    public async Task<int> ProcessStagingPackageAsync(
        ValidationReport report,
        bool isDryRun = false,
        bool isResume = false,
        string? stagingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        if (!report.IsValid || report.Manifest is null)
        {
            throw new ArgumentException(
                "Cannot process an invalid staging report.",
                nameof(report));
        }

        Console.WriteLine(
            $"[INGESTION] Job '{report.Manifest.JobId}': "
            + $"{report.Observations.Count} observations, "
            + $"{report.Chunks.Count} chunks.");

        if (isDryRun)
        {
            var dryRunGeneration = ComputeStableGuid(
                $"{CollectionName}|{EmbeddingModel}|{EmbeddingDimensions}");
            foreach (var chunk in report.Chunks)
            {
                _ = ComputeQdrantPointId(dryRunGeneration, chunk.ChunkId);
            }
            Console.WriteLine(
                "[DRY-RUN] Staging relationships and deterministic point IDs are valid. "
                + "0 DB writes, 0 Qdrant writes, 0 embedding network calls.");
            return report.Chunks.Count;
        }

        var job = await StartJobAsync(
            report,
            stagingDirectory,
            isResume,
            cancellationToken);
        var generation = await GetOrCreateGenerationAsync(cancellationToken);
        await _qdrantClient.EnsureCollectionAsync(
            CollectionName,
            EmbeddingDimensions,
            cancellationToken);

        var processedObservations = 0;
        foreach (var observation in report.Observations)
        {
            try
            {
                await ProcessObservationAsync(
                    observation,
                    report,
                    generation,
                    cancellationToken);
                processedObservations++;
                job.ProcessedObservations = processedObservations;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                job.FailedObservations++;
                job.ErrorSummary = exception.Message;
                _dbContext.RagIngestionErrors.Add(new RagIngestionError
                {
                    Id = Guid.NewGuid(),
                    JobId = job.JobId,
                    Stage = "index",
                    EntityType = "observation",
                    EntityId = observation.ObservationId.ToString("D"),
                    ErrorMessage = exception.Message,
                    StackTrace = exception.ToString(),
                    CreatedAt = DateTime.UtcNow
                });
                await _dbContext.SaveChangesAsync(cancellationToken);
                if (!isResume)
                {
                    Console.Error.WriteLine(
                        $"[INGESTION] Observation {observation.ObservationId} failed: {exception.Message}");
                }
            }
        }

        var allPackageChunkIds = report.Chunks
            .Select(chunk => chunk.ChunkId)
            .ToArray();
        var indexedCount = await _dbContext.RagIndexPoints
            .CountAsync(
                point => point.IndexGenerationId == generation.Id
                    && allPackageChunkIds.Contains(point.ChunkId)
                    && point.Status == "indexed",
                cancellationToken);

        if (indexedCount == report.Chunks.Count)
        {
            generation.Status = "active";
            generation.ActivatedAt ??= DateTime.UtcNow;
        }

        job.Status = job.FailedObservations == 0 ? "completed" : "failed";
        job.CompletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (job.FailedObservations > 0)
        {
            throw new InvalidOperationException(
                $"Ingestion failed for {job.FailedObservations} observation(s). "
                + "Run again with --resume after correcting the reported cause.");
        }

        Console.WriteLine(
            $"[INGESTION] Indexed {indexedCount} chunks and activated "
            + $"{processedObservations} document version(s).");
        return processedObservations;

    }
    private async Task<RagIngestionJob> StartJobAsync(
        ValidationReport report,
        string? stagingDirectory,
        bool isResume,
        CancellationToken cancellationToken)
    {
        var jobId = report.Manifest!.JobId;
        var job = await _dbContext.RagIngestionJobs
            .SingleOrDefaultAsync(
                existing => existing.JobId == jobId,
                cancellationToken);
        if (job is null)
        {
            job = new RagIngestionJob
            {
                JobId = jobId,
                StagingDirectory = stagingDirectory
                    ?? $"staging:{jobId}",
                Status = "running",
                TotalObservations = report.Observations.Count,
                StartedAt = DateTime.UtcNow
            };
            _dbContext.RagIngestionJobs.Add(job);
        }
        else
        {
            if (!isResume && job.Status == "running")
            {
                throw new InvalidOperationException(
                    $"Ingestion job '{jobId}' is already running; use --resume only after confirming the prior process stopped.");
            }
            job.Status = "running";
            job.TotalObservations = report.Observations.Count;
            job.ProcessedObservations = 0;
            job.FailedObservations = 0;
            if (!string.IsNullOrWhiteSpace(stagingDirectory))
            {
                job.StagingDirectory = stagingDirectory;
            }
            job.CompletedAt = null;
            job.ErrorSummary = null;
            job.StartedAt = DateTime.UtcNow;
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        return job;
    }

    private async Task<RagIndexGeneration> GetOrCreateGenerationAsync(
        CancellationToken cancellationToken)
    {
        var generation = await _dbContext.RagIndexGenerations
            .Where(existing =>
                existing.CollectionName == CollectionName
                && existing.EmbeddingModel == EmbeddingModel
                && existing.EmbeddingDimension == EmbeddingDimensions
                && (existing.Status == "active"
                    || existing.Status == "building"))
            .OrderByDescending(existing => existing.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (generation is not null)
        {
            return generation;
        }

        generation = new RagIndexGeneration
        {
            Id = Guid.NewGuid(),
            CollectionName = CollectionName,
            EmbeddingModel = EmbeddingModel,
            EmbeddingDimension = EmbeddingDimensions,
            DistanceMetric = "Cosine",
            Status = "building",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.RagIndexGenerations.Add(generation);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return generation;
    }

    private async Task ProcessObservationAsync(
        DocumentObservationDto observation,
        ValidationReport report,
        RagIndexGeneration generation,
        CancellationToken cancellationToken)
    {
        var document = await _dbContext.RagDocuments.SingleOrDefaultAsync(
            existing =>
                existing.CanonicalDocumentKey
                    == observation.CanonicalDocumentKey,
            cancellationToken);
        if (document is null)
        {
            document = new RagDocument
            {
                Id = Guid.NewGuid(),
                AuthorityNamespace = observation.AuthorityNamespace,
                CanonicalDocumentKey = observation.CanonicalDocumentKey,
                DocumentIdentityStrategy =
                    observation.DocumentIdentityStrategy,
                Title = observation.Title,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.RagDocuments.Add(document);
        }
        else
        {
            document.Title = observation.Title;
            document.AuthorityNamespace = observation.AuthorityNamespace;
            document.UpdatedAt = DateTime.UtcNow;
        }

        var version = await _dbContext.RagDocumentVersions
            .SingleOrDefaultAsync(
                existing => existing.Id == observation.ObservationId,
                cancellationToken);
        if (version is null)
        {
            version = new RagDocumentVersion
            {
                Id = observation.ObservationId,
                DocumentId = document.Id,
                RawArtifactUri = observation.RawArtifactUri,
                RawSha256 = observation.RawSha256,
                MimeType = observation.MimeType,
                NormalizedTextUri = observation.NormalizedTextUri,
                NormalizedTextSha256 = observation.NormalizedTextSha256,
                CharCount = observation.CharCount,
                WordCount = observation.WordCount,
                ExtractionQualityJson = JsonSerializer.Serialize(
                    observation.ExtractionQuality),
                MetadataJson = "{}",
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.RagDocumentVersions.Add(version);
        }

        var sourceExists = await _dbContext.RagDocumentSources.AnyAsync(
            source => source.VersionId == version.Id
                && source.SourceId == observation.SourceId
                && source.SourceDocumentUrl
                    == observation.SourceDocumentUrl,
            cancellationToken);
        if (!sourceExists)
        {
            _dbContext.RagDocumentSources.Add(new RagDocumentSource
            {
                Id = Guid.NewGuid(),
                DocumentId = document.Id,
                VersionId = version.Id,
                SourceId = observation.SourceId,
                SourceNamespace = observation.SourceNamespace,
                SourceDocumentUrl = observation.SourceDocumentUrl,
                CrawledAt = observation.CrawledAt
            });
        }

        var chunkSetDto = report.ChunkSets.Single(
            chunkSet => chunkSet.ObservationId
                == observation.ObservationId);
        var chunkSet = await _dbContext.RagChunkSets
            .SingleOrDefaultAsync(
                existing => existing.Id == chunkSetDto.ChunkSetId,
                cancellationToken);
        if (chunkSet is null)
        {
            chunkSet = new RagChunkSet
            {
                Id = chunkSetDto.ChunkSetId,
                VersionId = version.Id,
                ChunkingStrategy = chunkSetDto.ChunkingStrategy,
                ChunkerVersion = chunkSetDto.ChunkerVersion,
                TokenizerName = chunkSetDto.TokenizerName,
                TargetTokens = chunkSetDto.TargetTokens,
                OverlapTokens = chunkSetDto.OverlapTokens,
                TotalChunks = chunkSetDto.TotalChunks,
                CreatedAt = chunkSetDto.CreatedAt
            };
            _dbContext.RagChunkSets.Add(chunkSet);
        }

        var chunkDtos = report.Chunks
            .Where(chunk => chunk.ChunkSetId == chunkSet.Id)
            .OrderBy(chunk => chunk.ChunkIndex)
            .ToArray();
        foreach (var chunkDto in chunkDtos)
        {
            if (!await _dbContext.RagChunks.AnyAsync(
                    chunk => chunk.Id == chunkDto.ChunkId,
                    cancellationToken))
            {
                _dbContext.RagChunks.Add(MapChunk(chunkDto));
            }
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var chunkDto in chunkDtos)
        {
            var qdrantPointId = ComputeQdrantPointId(
                generation.Id,
                chunkDto.ChunkId);
            var indexPoint = await _dbContext.RagIndexPoints
                .SingleOrDefaultAsync(
                    point => point.QdrantPointId == qdrantPointId,
                    cancellationToken);
            if (indexPoint?.Status == "indexed")
            {
                continue;
            }
            if (indexPoint is null)
            {
                indexPoint = new RagIndexPoint
                {
                    PointId = qdrantPointId,
                    IndexGenerationId = generation.Id,
                    ChunkId = chunkDto.ChunkId,
                    ChunkSetId = chunkSet.Id,
                    VersionId = version.Id,
                    DocumentId = document.Id,
                    QdrantPointId = qdrantPointId,
                    Status = "pending"
                };
                _dbContext.RagIndexPoints.Add(indexPoint);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            try
            {
                var vector = await _embeddingService.GenerateEmbeddingAsync(
                    chunkDto.Text,
                    EmbeddingModel,
                    cancellationToken);
                await _qdrantClient.UpsertAsync(
                    CollectionName,
                    [
                        new QdrantIngestionPoint(
                            qdrantPointId,
                            chunkDto.ChunkId,
                            chunkSet.Id,
                            version.Id,
                            document.Id,
                            document.CanonicalDocumentKey,
                            chunkDto.ChunkAcl.SecurityClassification,
                            chunkDto.ChunkAcl.AllowedRoles,
                            chunkDto.ChunkAcl.DeniedRoles,
                            vector)
                    ],
                    cancellationToken);
                indexPoint.Status = "indexed";
                indexPoint.ErrorMessage = null;
                indexPoint.IndexedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                indexPoint.Status = "failed";
                indexPoint.ErrorMessage = exception.Message;
                await _dbContext.SaveChangesAsync(cancellationToken);
                throw;
            }
        }

        var allIndexed = await _dbContext.RagIndexPoints.AllAsync(
            point => point.IndexGenerationId != generation.Id
                || point.ChunkSetId != chunkSet.Id
                || point.Status == "indexed",
            cancellationToken);
        if (!allIndexed)
        {
            throw new InvalidOperationException(
                $"Chunk set {chunkSet.Id} was not fully indexed.");
        }
        document.ActiveVersionId = version.Id;
        document.ActiveChunkSetId = chunkSet.Id;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static RagChunk MapChunk(ChunkDto chunk) => new()
    {
        Id = chunk.ChunkId,
        ChunkSetId = chunk.ChunkSetId,
        ChunkIndex = chunk.ChunkIndex,
        Text = chunk.Text,
        TokenCount = chunk.TokenCount,
        CharacterStart = chunk.CharacterStart,
        CharacterEnd = chunk.CharacterEnd,
        ContentSha256 = chunk.ContentSha256,
        HeadingPath = chunk.HeadingPath,
        PageNumbers = chunk.PageNumbers.ToArray(),
        StructureMetadataJson = "{}",
        AllowedRoles = chunk.ChunkAcl.AllowedRoles.ToArray(),
        DeniedRoles = chunk.ChunkAcl.DeniedRoles.ToArray(),
        SecurityClassification =
            chunk.ChunkAcl.SecurityClassification,
        CreatedAt = DateTime.UtcNow
    };

    private static Guid ComputeStableGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }
}
