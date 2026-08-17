using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DigitalOps.API.Shared.AI;
using DigitalOps.API.Shared.AI.Retrieval;
using DigitalOps.API.Shared.Data;
using DigitalOps.API.Shared.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalOps.API.Tests;

public sealed class TestDoubleQdrantKnowledgeClient : IQdrantKnowledgeClient
{
    public Task EnsureCollectionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public List<RagChunkKnowledgeCandidate> RagCandidates { get; } = [];

    public Task<IReadOnlyDictionary<Guid, string>> GetStaffContentHashesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    public Task UpsertStaffPointsAsync(IReadOnlyList<StaffKnowledgePoint> points, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteStaffPointsAsync(IReadOnlyList<Guid> staffIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<IReadOnlyList<StaffKnowledgeCandidate>> SearchStaffAsync(float[] queryVector, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StaffKnowledgeCandidate>>(new List<StaffKnowledgeCandidate>());
    public Task<IReadOnlyList<TemplateKnowledgeState>> GetTemplateStatesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TemplateKnowledgeState>>(new List<TemplateKnowledgeState>());
    public Task UpsertTemplatePointsAsync(IReadOnlyList<TemplateKnowledgePoint> points, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteTemplatePointsAsync(IReadOnlyList<Guid> pointIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<IReadOnlyList<TemplateKnowledgeCandidate>> SearchTemplateAsync(float[] queryVector, Guid templateId, string documentTypeCode, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TemplateKnowledgeCandidate>>(new List<TemplateKnowledgeCandidate>());
    public Task<IReadOnlyList<FormatRuleKnowledgeState>> GetFormatRuleStatesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FormatRuleKnowledgeState>>(new List<FormatRuleKnowledgeState>());
    public Task UpsertFormatRulePointsAsync(IReadOnlyList<FormatRuleKnowledgePoint> points, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteFormatRulePointsAsync(IReadOnlyList<Guid> pointIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<IReadOnlyList<FormatRuleKnowledgeCandidate>> SearchFormatRulesAsync(float[] queryVector, Guid templateId, string documentTypeCode, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FormatRuleKnowledgeCandidate>>(new List<FormatRuleKnowledgeCandidate>());
    public Task<IReadOnlyList<RagChunkKnowledgeCandidate>> SearchRagChunksAsync(float[] queryVector, int limit, double minScore, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RagChunkKnowledgeCandidate>>(RagCandidates.Where(candidate => candidate.Score >= minScore).Take(limit).ToArray());
}

public sealed class RAGRetrievalServiceTests
{
    private static async Task<(SqliteConnection Connection, DigitalOpsDbContext DbContext)> CreateSqliteDbContextAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<DigitalOpsDbContext>()
            .UseSqlite(connection)
            .ReplaceService<IModelCustomizer, AuthenticationTestModelCustomizer>()
            .Options;

        var dbContext = new DigitalOpsDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        return (connection, dbContext);
    }

    [Fact]
    public async Task RetrieveAsync_FiltersByActiveVersionAndAcl_ReturnsOnlyValidPoints()
    {
        // Arrange
        var (connection, dbContext) = await CreateSqliteDbContextAsync();
        await using var conn = connection;
        await using var db = dbContext;

        var qdrantClient = new TestDoubleQdrantKnowledgeClient();
        var logger = NullLogger<RAGRetrievalService>.Instance;

        var docId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var chunkSetId = Guid.NewGuid();
        var indexGenId = Guid.NewGuid();

        var doc = new RagDocument
        {
            Id = docId,
            CanonicalDocumentKey = "gov:nghidinh:15/2023/ND-CP",
            DocumentIdentityStrategy = "authoritative",
            Title = "Nghị định 15/2023/NĐ-CP"
        };
        db.RagDocuments.Add(doc);
        await db.SaveChangesAsync();

        var docVersion = new RagDocumentVersion
        {
            Id = versionId,
            DocumentId = docId,
            RawArtifactUri = "storage/raw/doc.pdf",
            RawSha256 = new string('1', 64),
            MimeType = "application/pdf",
            NormalizedTextUri = "storage/norm/doc.txt",
            NormalizedTextSha256 = new string('2', 64),
            CharCount = 1000,
            WordCount = 200,
            ExtractionQualityJson = "{}",
            DocumentNumber = "15/2023/NĐ-CP",
            DocumentType = "Nghị định",
            Issuer = "Chính phủ",
            IssuedDate = new DateOnly(2023, 4, 15),
            LegalStatus = "current",
            EffectiveFrom = new DateOnly(2023, 5, 1),
            SourceVersion = "sha256:official-v1",
            Language = "vi"
        };
        db.RagDocumentVersions.Add(docVersion);
        db.RagDocumentSources.Add(new RagDocumentSource
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            VersionId = versionId,
            SourceId = "vanban_chinhphu",
            SourceNamespace = "vanban.chinhphu.vn",
            SourceDocumentUrl = "https://vanban.chinhphu.vn/doc-15",
            RegistryEntryId = "vanban-chinhphu-official",
            RegistryVersion = "2026-08-03.1",
            SourceDomain = "vanban.chinhphu.vn",
            SourceTrustTier = "official",
            CorpusType = "legal_reference",
            PublishPolicy = "authoritative",
            AdmissionReference = "T4-03-test",
            AdmissionApprovedBy = "Test",
            AdmissionApprovedAt = DateTime.UtcNow,
            CrawledAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var chunkSet = new RagChunkSet
        {
            Id = chunkSetId,
            VersionId = versionId,
            ChunkingStrategy = "qwen_sliding_window",
            ChunkerVersion = "1.0.0",
            TokenizerName = "Qwen/Qwen2.5-0.5B-Instruct",
            TargetTokens = 512,
            OverlapTokens = 64,
            TotalChunks = 1
        };
        db.RagChunkSets.Add(chunkSet);
        await db.SaveChangesAsync();

        doc.ActiveVersionId = versionId;
        doc.ActiveChunkSetId = chunkSetId;

        var activeChunk = new RagChunk
        {
            Id = Guid.NewGuid(),
            ChunkSetId = chunkSetId,
            ChunkIndex = 0,
            Text = "Dữ liệu hợp lệ cho người dùng có quyền.",
            TokenCount = 20,
            CharacterStart = 0,
            CharacterEnd = 50,
            ContentSha256 = new string('a', 64),
            AllowedRoles = new[] { "public", "staff" },
            DeniedRoles = Array.Empty<string>()
        };
        db.RagChunks.Add(activeChunk);

        var indexGen = new RagIndexGeneration
        {
            Id = indexGenId,
            CollectionName = "digitalops_rag_v1",
            EmbeddingModel = "qwen3-embedding:0.6b",
            EmbeddingDimension = 1024,
            DistanceMetric = "Cosine",
            Status = "active"
        };
        db.RagIndexGenerations.Add(indexGen);

        var indexPoint = new RagIndexPoint
        {
            PointId = Guid.NewGuid(),
            IndexGenerationId = indexGenId,
            ChunkId = activeChunk.Id,
            ChunkSetId = chunkSetId,
            VersionId = versionId,
            DocumentId = docId,
            QdrantPointId = Guid.NewGuid(),
            Status = "indexed"
        };
        db.RagIndexPoints.Add(indexPoint);
        await db.SaveChangesAsync();

        qdrantClient.RagCandidates.Add(new(indexPoint.QdrantPointId, 0.91));
        var service = new RAGRetrievalService(db, qdrantClient, logger);
        var queryVector = new float[1024];

        // Act
        var results = await service.RetrieveAsync(queryVector, userRole: "staff", topK: 5);

        // Assert
        Assert.Single(results);
        Assert.Equal(activeChunk.Id, results[0].ChunkId);
        Assert.Equal("Nghị định 15/2023/NĐ-CP", results[0].Title);
        Assert.Equal("https://vanban.chinhphu.vn/doc-15", results[0].SourceUrl);
        Assert.Equal("official", results[0].SourceTrustTier);
        Assert.Equal("15/2023/NĐ-CP", results[0].DocumentNumber);
        Assert.False(results[0].IsEffectivityUnknown);

        var source = await db.RagDocumentSources.SingleAsync();
        source.SourceTrustTier = "aggregator";
        source.PublishPolicy = "cross_check_only";
        await db.SaveChangesAsync();
        Assert.Empty(await service.RetrieveAsync(
            queryVector,
            userRole: "staff",
            topK: 5));

        source.SourceTrustTier = "official";
        source.PublishPolicy = "authoritative";
        docVersion.LegalStatus = "expired";
        docVersion.EffectiveTo = new DateOnly(2025, 12, 31);
        await db.SaveChangesAsync();
        Assert.Empty(await service.RetrieveAsync(
            queryVector,
            userRole: "staff",
            topK: 5,
            asOf: new DateOnly(2026, 8, 3)));
        Assert.Single(await service.RetrieveAsync(
            queryVector,
            userRole: "staff",
            topK: 5,
            asOf: new DateOnly(2026, 8, 3),
            includeHistorical: true));
    }

    [Fact]
    public async Task RetrieveAsync_BlocksDeniedRole_ReturnsEmpty()
    {
        // Arrange
        var (connection, dbContext) = await CreateSqliteDbContextAsync();
        await using var conn = connection;
        await using var db = dbContext;

        var qdrantClient = new TestDoubleQdrantKnowledgeClient();
        var logger = NullLogger<RAGRetrievalService>.Instance;

        var docId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var chunkSetId = Guid.NewGuid();
        var indexGenId = Guid.NewGuid();

        var doc = new RagDocument
        {
            Id = docId,
            CanonicalDocumentKey = "gov:doc:secret",
            DocumentIdentityStrategy = "authoritative",
            Title = "Văn bản Bảo mật"
        };
        db.RagDocuments.Add(doc);
        await db.SaveChangesAsync();

        var docVersion = new RagDocumentVersion
        {
            Id = versionId,
            DocumentId = docId,
            RawArtifactUri = "storage/raw/secret.pdf",
            RawSha256 = new string('3', 64),
            MimeType = "application/pdf",
            NormalizedTextUri = "storage/norm/secret.txt",
            NormalizedTextSha256 = new string('4', 64),
            CharCount = 500,
            WordCount = 100,
            ExtractionQualityJson = "{}",
            DocumentNumber = "02/2024/QĐ",
            DocumentType = "Quyết định",
            Issuer = "Cơ quan thử nghiệm",
            IssuedDate = new DateOnly(2024, 1, 1),
            LegalStatus = "current",
            EffectiveFrom = new DateOnly(2024, 1, 1),
            SourceVersion = "sha256:secret-v1",
            Language = "vi"
        };
        db.RagDocumentVersions.Add(docVersion);
        db.RagDocumentSources.Add(new RagDocumentSource
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            VersionId = versionId,
            SourceId = "vanban_chinhphu",
            SourceNamespace = "vanban.chinhphu.vn",
            SourceDocumentUrl = "https://vanban.chinhphu.vn/secret",
            RegistryEntryId = "vanban-chinhphu-official",
            RegistryVersion = "2026-08-03.1",
            SourceDomain = "vanban.chinhphu.vn",
            SourceTrustTier = "official",
            CorpusType = "legal_reference",
            PublishPolicy = "authoritative",
            AdmissionReference = "T4-03-test",
            AdmissionApprovedBy = "Test",
            AdmissionApprovedAt = DateTime.UtcNow,
            CrawledAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var chunkSet = new RagChunkSet
        {
            Id = chunkSetId,
            VersionId = versionId,
            ChunkingStrategy = "qwen_sliding_window",
            ChunkerVersion = "1.0.0",
            TokenizerName = "Qwen/Qwen2.5-0.5B-Instruct",
            TargetTokens = 512,
            OverlapTokens = 64,
            TotalChunks = 1
        };
        db.RagChunkSets.Add(chunkSet);
        await db.SaveChangesAsync();

        doc.ActiveVersionId = versionId;
        doc.ActiveChunkSetId = chunkSetId;

        var secretChunk = new RagChunk
        {
            Id = Guid.NewGuid(),
            ChunkSetId = chunkSetId,
            ChunkIndex = 0,
            Text = "Dữ liệu mật.",
            TokenCount = 10,
            CharacterStart = 0,
            CharacterEnd = 20,
            ContentSha256 = new string('b', 64),
            AllowedRoles = new[] { "public" },
            DeniedRoles = new[] { "restricted_role" }
        };
        db.RagChunks.Add(secretChunk);

        var indexGen = new RagIndexGeneration
        {
            Id = indexGenId,
            CollectionName = "digitalops_rag_v1",
            EmbeddingModel = "qwen3-embedding:0.6b",
            EmbeddingDimension = 1024,
            DistanceMetric = "Cosine",
            Status = "active"
        };
        db.RagIndexGenerations.Add(indexGen);

        var indexPoint = new RagIndexPoint
        {
            PointId = Guid.NewGuid(),
            IndexGenerationId = indexGenId,
            ChunkId = secretChunk.Id,
            ChunkSetId = chunkSetId,
            VersionId = versionId,
            DocumentId = docId,
            QdrantPointId = Guid.NewGuid(),
            Status = "indexed"
        };
        db.RagIndexPoints.Add(indexPoint);
        await db.SaveChangesAsync();

        qdrantClient.RagCandidates.Add(new(indexPoint.QdrantPointId, 0.92));
        var service = new RAGRetrievalService(db, qdrantClient, logger);
        var queryVector = new float[1024];

        // Act
        var results = await service.RetrieveAsync(queryVector, userRole: "restricted_role", topK: 5);

        // Assert
        Assert.Empty(results);
    }
}
