using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DigitalOps.API.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DigitalOps.API.Shared.AI.Retrieval;

public sealed record RetrievalResult(
    Guid ChunkId,
    Guid DocumentId,
    Guid VersionId,
    string Text,
    string HeadingPath,
    double Score,
    string CanonicalDocumentKey,
    string Title,
    string SourceId,
    string SourceUrl,
    string SourceTrustTier,
    string SourceVersion,
    string LegalStatus,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    string? DocumentNumber,
    string? DocumentType,
    string? Issuer,
    bool IsEffectivityUnknown
);

public interface IRAGRetrievalService
{
    Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        float[] queryVector,
        string userRole = "public",
        int topK = 5,
        double minScore = 0.316666,
        DateOnly? asOf = null,
        bool includeHistorical = false,
        CancellationToken cancellationToken = default);
}

public sealed class RAGRetrievalService : IRAGRetrievalService
{
    private readonly DigitalOpsDbContext _dbContext;
    private readonly IQdrantKnowledgeClient _qdrantClient;
    private readonly ILogger<RAGRetrievalService> _logger;

    private static readonly int[] CandidateMultipliers = [4, 8, 12, 20];

    public RAGRetrievalService(
        DigitalOpsDbContext dbContext,
        IQdrantKnowledgeClient qdrantClient,
        ILogger<RAGRetrievalService> logger)
    {
        _dbContext = dbContext;
        _qdrantClient = qdrantClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        float[] queryVector,
        string userRole = "public",
        int topK = 5,
        double minScore = 0.316666,
        DateOnly? asOf = null,
        bool includeHistorical = false,
        CancellationToken cancellationToken = default)
    {
        var filteredResults = new List<RetrievalResult>();
        ArgumentNullException.ThrowIfNull(queryVector);
        if (topK is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(
                nameof(topK),
                "topK must be between 1 and 50.");
        }
        var effectiveDate = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (string.IsNullOrWhiteSpace(userRole))
        {
            throw new ArgumentException(
                "A non-empty user role is required.",
                nameof(userRole));
        }


        foreach (var multiplier in CandidateMultipliers)
        {
            int candidateLimit = topK * multiplier;
            _logger.LogInformation("Retrieving candidates with multiplier {Multiplier}x (limit {Limit})", multiplier, candidateLimit);

            var vectorCandidates = await _qdrantClient.SearchRagChunksAsync(
                queryVector,
                candidateLimit,
                minScore,
                cancellationToken);
            if (vectorCandidates.Count == 0)
            {
                continue;
            }
            var candidatePointIds = vectorCandidates
                .Select(candidate => candidate.QdrantPointId)
                .ToArray();
            var scores = vectorCandidates.ToDictionary(
                candidate => candidate.QdrantPointId,
                candidate => candidate.Score);

            var activePointsQuery = from p in _dbContext.RagIndexPoints
                                    join d in _dbContext.RagDocuments on p.DocumentId equals d.Id
                                    join c in _dbContext.RagChunks on p.ChunkId equals c.Id
                                    join v in _dbContext.RagDocumentVersions on p.VersionId equals v.Id
                                    join s in _dbContext.RagDocumentSources on v.Id equals s.VersionId
                                    where candidatePointIds.Contains(p.QdrantPointId)
                                          && p.Status == "indexed"
                                          && d.ActiveVersionId == p.VersionId
                                          && d.ActiveChunkSetId == p.ChunkSetId
                                          && s.CorpusType == "legal_reference"
                                          && ((s.SourceTrustTier == "official"
                                                  && s.PublishPolicy == "authoritative")
                                              || (s.SourceTrustTier == "verified_copy"
                                                  && s.PublishPolicy == "verified_copy"))
                                          && s.AdmissionReference != null
                                          && (includeHistorical
                                              || (v.LegalStatus != "expired"
                                                  && v.LegalStatus != "repealed"
                                                  && v.LegalStatus != "superseded"
                                                  && (v.EffectiveFrom == null
                                                      || v.EffectiveFrom <= effectiveDate)
                                                  && (v.EffectiveTo == null
                                                      || v.EffectiveTo >= effectiveDate)))
                                    select new
                                    {
                                        p.QdrantPointId,
                                        p.ChunkId,
                                        p.DocumentId,
                                        p.VersionId,
                                        c.Text,
                                        HeadingPath = c.HeadingPath ?? string.Empty,
                                        c.AllowedRoles,
                                        c.DeniedRoles,
                                        d.CanonicalDocumentKey,
                                        d.Title,
                                        s.SourceId,
                                        SourceUrl = s.SourceDocumentUrl,
                                        s.SourceTrustTier,
                                        SourceVersion = v.SourceVersion ?? string.Empty,
                                        v.LegalStatus,
                                        v.EffectiveFrom,
                                        v.EffectiveTo,
                                        v.DocumentNumber,
                                        v.DocumentType,
                                        v.Issuer
                                    };

            var candidateItems = await activePointsQuery
                .ToListAsync(cancellationToken);
            var activeByPointId = candidateItems
                .GroupBy(item => item.QdrantPointId)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(item => item.SourceTrustTier == "official" ? 0 : 1)
                        .First());

            filteredResults = vectorCandidates
                .Where(candidate =>
                    activeByPointId.ContainsKey(candidate.QdrantPointId))
                .Select(candidate => new
                {
                    Candidate = candidate,
                    Item = activeByPointId[candidate.QdrantPointId]
                })
                .Where(result =>
                    !result.Item.DeniedRoles.Contains(
                        userRole,
                        StringComparer.OrdinalIgnoreCase)
                    && (result.Item.AllowedRoles.Contains(
                            "public",
                            StringComparer.OrdinalIgnoreCase)
                        || result.Item.AllowedRoles.Contains(
                            userRole,
                            StringComparer.OrdinalIgnoreCase)))
                .Select(result => new RetrievalResult(
                    result.Item.ChunkId,
                    result.Item.DocumentId,
                    result.Item.VersionId,
                    result.Item.Text,
                    result.Item.HeadingPath,
                    scores[result.Candidate.QdrantPointId],
                    result.Item.CanonicalDocumentKey,
                    result.Item.Title,
                    result.Item.SourceId,
                    result.Item.SourceUrl,
                    result.Item.SourceTrustTier,
                    result.Item.SourceVersion,
                    result.Item.LegalStatus,
                    result.Item.EffectiveFrom,
                    result.Item.EffectiveTo,
                    result.Item.DocumentNumber,
                    result.Item.DocumentType,
                    result.Item.Issuer,
                    result.Item.LegalStatus == "status_unknown"
                        || result.Item.EffectiveFrom is null))
                .Take(topK)
                .ToList();

            if (filteredResults.Count >= topK)
            {
                _logger.LogInformation("Adaptive multiplier satisfied at {Multiplier}x with {Count} candidates", multiplier, filteredResults.Count);
                break;
            }
        }

        return filteredResults;
    }
}
