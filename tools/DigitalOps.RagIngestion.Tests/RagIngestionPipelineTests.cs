using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalOps.API.Features.Attachments;
using DigitalOps.API.Features.Drafting;
using DigitalOps.API.Features.OutgoingDocuments;
using DigitalOps.API.Features.Review;
using DigitalOps.API.Shared.Data.Entities;
using DigitalOps.API.Shared.Data;
using DigitalOps.RagIngestion.Models;
using DigitalOps.RagIngestion.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalOps.RagIngestion.Tests;

public sealed class RagIngestionPipelineTests
{
    [Fact]
    public async Task ProcessStagingPackage_indexes_then_resumes_idempotently()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DigitalOpsDbContext>()
            .UseSqlite(connection)
            .ReplaceService<IModelCustomizer, SqliteTestModelCustomizer>()
            .Options;
        await using var dbContext = new DigitalOpsDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var embedding = new FakeEmbeddingService();
        var qdrant = new FakeQdrantIngestionClient();
        var pipeline = new IngestionPipelineService(
            dbContext,
            embedding,
            qdrant);
        var report = CreateReport();
        var receipt = new AdmissionReceipt(
            "1.0",
            new string('c', 64),
            "registry-test-1",
            "approved",
            "legal-data-steward",
            DateTime.UtcNow,
            "ADM-2026-PIPELINE",
            [report.Observations.Single().ObservationId],
            []);

        var processed = await pipeline.ProcessStagingPackageAsync(
            report,
            stagingDirectory: "C:\\staging\\JOB_TEST",
            admissionReceipt: receipt);

        Assert.Equal(1, processed);
        Assert.Equal(1, embedding.CallCount);
        Assert.Single(qdrant.Points);
        var document = await dbContext.RagDocuments.SingleAsync();
        var chunkSet = await dbContext.RagChunkSets.SingleAsync();
        var indexPoint = await dbContext.RagIndexPoints.SingleAsync();
        Assert.Equal(
            report.Observations[0].ObservationId,
            document.ActiveVersionId);
        Assert.Equal(chunkSet.Id, document.ActiveChunkSetId);
        Assert.Equal("indexed", indexPoint.Status);
        Assert.Equal(
            "completed",
            (await dbContext.RagIngestionJobs.SingleAsync()).Status);
        var version = await dbContext.RagDocumentVersions.SingleAsync();
        var source = await dbContext.RagDocumentSources.SingleAsync();
        Assert.Equal("01/2026/QD", version.DocumentNumber);
        Assert.Equal("current", version.LegalStatus);
        Assert.Equal("sha256:source-version", version.SourceVersion);
        Assert.Equal("official", source.SourceTrustTier);
        Assert.Equal("legal_reference", source.CorpusType);
        Assert.Equal("ADM-2026-PIPELINE", source.AdmissionReference);
        Assert.Contains("source_provenance", version.MetadataJson);
        Assert.Equal("official", qdrant.Points.Single().SourceTrustTier);
        Assert.Equal("current", qdrant.Points.Single().LegalStatus);

        processed = await pipeline.ProcessStagingPackageAsync(
            report,
            isResume: true,
            stagingDirectory: "C:\\staging\\JOB_TEST",
            admissionReceipt: receipt);

        Assert.Equal(1, processed);
        Assert.Equal(1, embedding.CallCount);
        Assert.Single(qdrant.Points);
        Assert.Single(await dbContext.RagDocumentVersions.ToListAsync());
        Assert.Single(await dbContext.RagChunks.ToListAsync());
        Assert.Single(await dbContext.RagIndexPoints.ToListAsync());
    }

    [Fact]
    public async Task OllamaEmbeddingService_rejects_wrong_dimension_without_fallback()
    {
        var handler = new StubHttpMessageHandler(
            """{"embeddings":[[0.1,0.2]]}""");
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:11434")
        };
        var service = new OllamaEmbeddingService(client);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GenerateEmbeddingAsync("xin chao"));

        Assert.Contains("1024", exception.Message, StringComparison.Ordinal);
        Assert.Equal("/api/embed", handler.RequestUri?.AbsolutePath);
    }


    [Fact]
    public void StagingValidator_rejects_artifacts_outside_package()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "dxos-worker-tests",
            Guid.NewGuid().ToString("N"));
        var stagingDirectory = Path.Combine(testRoot, "package");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            const string text = "Noi dung hop le.";
            var bytes = Encoding.UTF8.GetBytes(text);
            var hash = Convert.ToHexString(
                SHA256.HashData(bytes)).ToLowerInvariant();
            var outsidePath = Path.Combine(testRoot, "outside.html");
            var normalizedPath = Path.Combine(stagingDirectory, "normalized.txt");
            File.WriteAllBytes(outsidePath, bytes);
            File.WriteAllBytes(normalizedPath, bytes);

            var now = DateTime.UtcNow;
            var observationId = Guid.NewGuid();
            var chunkSetId = Guid.NewGuid();
            var manifest = new StagingManifest(
                "JOB_ESCAPE", now, now, 1, 1, 1, 0);
            var observation = new DocumentObservationDto(
                observationId,
                "JOB_ESCAPE",
                "test-source",
                "example.gov.vn",
                "gov.vn",
                "authoritative",
                "gov:test:escape",
                "https://example.gov.vn/document/escape",
                "Escape test",
                "../outside.html",
                hash,
                "text/html",
                "normalized.txt",
                hash,
                text.Length,
                text.Split(' ').Length,
                new ExtractionQualityDto("clean", false, 1.0),
                now);
            var chunkSet = new ChunkSetDto(
                chunkSetId,
                observationId,
                "JOB_ESCAPE",
                "contiguous_structure_aware_sliding",
                "2.0.0",
                "test:word-count",
                448,
                64,
                1,
                now);
            var chunk = new ChunkDto(
                Guid.NewGuid(),
                chunkSetId,
                0,
                text,
                text.Split(' ').Length,
                0,
                text.Length,
                hash,
                null,
                [],
                new ChunkAclDto(["public"], ["PUBLIC"], "internal"));

            File.WriteAllText(
                Path.Combine(stagingDirectory, "manifest.json"),
                JsonSerializer.Serialize(manifest));
            File.WriteAllText(
                Path.Combine(stagingDirectory, "document-observations.jsonl"),
                JsonSerializer.Serialize(observation) + Environment.NewLine);
            File.WriteAllText(
                Path.Combine(stagingDirectory, "chunk-sets.jsonl"),
                JsonSerializer.Serialize(chunkSet) + Environment.NewLine);
            File.WriteAllText(
                Path.Combine(stagingDirectory, "chunks.jsonl"),
                JsonSerializer.Serialize(chunk) + Environment.NewLine);

            var report = StagingValidator.Validate(stagingDirectory);

            Assert.False(report.IsValid);
            Assert.Contains(
                report.Errors,
                error => error.Contains(
                    "outside the staging package",
                    StringComparison.Ordinal));
            Assert.Contains(
                report.Errors,
                error => error.Contains(
                    "overlapping ACL roles",
                    StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }
    private static ValidationReport CreateReport()
    {
        var observationId = Guid.NewGuid();
        var chunkSetId = Guid.NewGuid();
        const string text = "Noi dung van ban dung de kiem thu ingestion.";
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        var now = DateTime.UtcNow;
        return new ValidationReport(
            true,
            new StagingManifest(
                "JOB_TEST",
                now,
                now,
                1,
                1,
                1,
                0,
                "1.0",
                "legal_reference",
                "registry-test-1",
                ["official-source"]),
            [
                new DocumentObservationDto(
                    observationId,
                    "JOB_TEST",
                    "test-source",
                    "example.gov.vn",
                    "gov.vn",
                    "authoritative",
                    "gov:test:1",
                    "https://example.gov.vn/document/1",
                    "Test document",
                    "C:\\raw\\document.html",
                    new string('a', 64),
                    "text/html",
                    "C:\\raw\\document_norm.txt",
                    new string('b', 64),
                    text.Length,
                    text.Split(' ').Length,
                    new ExtractionQualityDto("clean", false, 1.0),
                    now,
                    new SourceProvenanceDto(
                        "official-source",
                        "registry-test-1",
                        "legal_reference",
                        "official",
                        "example.gov.vn",
                        "sha256:source-version",
                        "authoritative",
                        "vi"),
                    new LegalDocumentMetadataDto(
                        "01/2026/QD",
                        "Quyet dinh",
                        "Co quan nha nuoc",
                        new DateOnly(2026, 1, 1),
                        "current",
                        new DateOnly(2026, 2, 1),
                        null,
                        [],
                        [],
                        []))
            ],
            [
                new ChunkSetDto(
                    chunkSetId,
                    observationId,
                    "JOB_TEST",
                    "contiguous_structure_aware_sliding",
                    "2.0.0",
                    "heuristic:vietnamese-word-1.3x",
                    448,
                    64,
                    1,
                    now)
            ],
            [
                new ChunkDto(
                    Guid.NewGuid(),
                    chunkSetId,
                    0,
                    text,
                    10,
                    0,
                    text.Length,
                    hash,
                    null,
                    [],
                    new ChunkAclDto(["public"], [], "internal"))
            ],
            []);
    }

    private sealed class FakeEmbeddingService : IOllamaEmbeddingService
    {
        public int CallCount { get; private set; }

        public Task<float[]> GenerateEmbeddingAsync(
            string text,
            string model = IngestionPipelineService.EmbeddingModel,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new float[1024]);
        }
    }

    private sealed class FakeQdrantIngestionClient : IQdrantIngestionClient
    {
        public List<QdrantIngestionPoint> Points { get; } = [];

        public Task EnsureCollectionAsync(
            string collectionName,
            uint dimensions,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(
                IngestionPipelineService.CollectionName,
                collectionName);
            Assert.Equal(1024u, dimensions);
            return Task.CompletedTask;
        }

        public Task UpsertAsync(
            string collectionName,
            IReadOnlyList<QdrantIngestionPoint> points,
            CancellationToken cancellationToken = default)
        {
            Points.AddRange(points);
            return Task.CompletedTask;
        }
    }

    private sealed class StubHttpMessageHandler(string responseJson)
        : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(
                System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    responseJson,
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }
}

public sealed class SqliteTestModelCustomizer(
    ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
{
    public override void Customize(
        ModelBuilder modelBuilder,
        DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.Entity<RagChunk>()
            .Property(chunk => chunk.PageNumbers)
            .HasConversion(
                value => string.Join(",", value),
                value => string.IsNullOrEmpty(value)
                    ? Array.Empty<int>()
                    : value.Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray())
            .HasColumnType("TEXT")
            .HasDefaultValueSql(null)
            .HasDefaultValue(Array.Empty<int>());
        ConfigureStringArray(
            modelBuilder.Entity<RagChunk>()
                .Property(chunk => chunk.AllowedRoles),
            ["public"]);
        ConfigureStringArray(
            modelBuilder.Entity<RagChunk>()
                .Property(chunk => chunk.DeniedRoles),
            []);
        modelBuilder.Entity<RagCitationSnapshot>()
            .Property(snapshot => snapshot.RetrievedChunkIds)
            .HasConversion(
                value => string.Join(",", value),
                value => string.IsNullOrEmpty(value)
                    ? Array.Empty<Guid>()
                    : value.Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToArray())
            .HasColumnType("TEXT");
        ConfigureJson(
            modelBuilder.Entity<DocumentTemplate>()
                .Property(template => template.FormatRules),
            "{}",
            "format_rules");
        ConfigureJson(
            modelBuilder.Entity<OutgoingDocument>()
                .Property(document => document.ReviewIssues),
            "[]",
            "review_issues");
        ConfigureJson(
            modelBuilder.Entity<ReviewHistory>()
                .Property(review => review.ReviewIssues),
            "[]",
            "review_issues");
        modelBuilder.Entity<DocumentTemplate>()
            .ToTable("document_templates", table =>
                table.HasCheckConstraint(
                    "ck_document_templates_format_rules_object",
                    "json_type(format_rules) = 'object'"));
        modelBuilder.Entity<OutgoingDocument>()
            .ToTable("outgoing_documents", table =>
                table.HasCheckConstraint(
                    "ck_outgoing_documents_review_issues_array",
                    "json_type(review_issues) = 'array'"));
        modelBuilder.Entity<ReviewHistory>()
            .ToTable("review_history", table =>
                table.HasCheckConstraint(
                    "ck_review_history_issues_array",
                    "json_type(review_issues) = 'array'"));
        modelBuilder.Entity<Attachment>()
            .ToTable("attachments", table =>
                table.HasCheckConstraint(
                    "ck_attachments_exactly_one_parent",
                    "(incoming_document_id IS NOT NULL AND outgoing_document_id IS NULL) OR (incoming_document_id IS NULL AND outgoing_document_id IS NOT NULL)"));
    }

    private static void ConfigureJson(
        PropertyBuilder<JsonElement> property,
        string defaultJson,
        string columnName)
    {
        property.HasConversion(
                value => value.GetRawText(),
                value => JsonDocument.Parse(value).RootElement.Clone())
            .HasColumnName(columnName)
            .HasColumnType("TEXT")
            .HasDefaultValueSql($"'{defaultJson}'");
    }
    private static void ConfigureStringArray(
        PropertyBuilder<string[]> property,
        string[] defaultValue)
    {
        property
            .HasConversion(
                value => string.Join(",", value),
                value => string.IsNullOrEmpty(value)
                    ? Array.Empty<string>()
                    : value.Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries))
            .HasColumnType("TEXT")
            .HasDefaultValueSql(null)
            .HasDefaultValue(defaultValue);
    }
}
