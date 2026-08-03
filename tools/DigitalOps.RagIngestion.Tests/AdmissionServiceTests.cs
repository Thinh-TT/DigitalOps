using System.Security.Cryptography;
using System.Text;
using DigitalOps.RagIngestion.Models;
using DigitalOps.RagIngestion.Services;

namespace DigitalOps.RagIngestion.Tests;

public sealed class AdmissionServiceTests
{
    [Fact]
    public void CreateReceipt_approves_registered_official_legal_document()
    {
        var stagingDirectory = CreateStagingDirectory();
        try
        {
            var report = CreateReport(
                trustTier: "official",
                publishPolicy: "authoritative");
            var registry = CreateRegistry(
                trustTier: "official",
                publishPolicy: "authoritative");

            var receipt = AdmissionService.CreateReceipt(
                stagingDirectory,
                report,
                registry,
                approvedBy: "legal-data-steward",
                approvalReference: "ADM-2026-001");
            AdmissionService.WriteReceipt(stagingDirectory, receipt);
            var validated = AdmissionService.ValidateReceiptForPublish(
                stagingDirectory,
                report,
                registry);

            Assert.Equal("approved", validated.Status);
            Assert.Equal(
                [report.Observations.Single().ObservationId],
                validated.ApprovedObservationIds);
            Assert.Empty(validated.QuarantinedObservations);
        }
        finally
        {
            Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    [Fact]
    public void Evaluate_quarantines_cross_check_only_aggregator()
    {
        var report = CreateReport(
            trustTier: "aggregator",
            publishPolicy: "cross_check_only");
        var registry = CreateRegistry(
            trustTier: "aggregator",
            publishPolicy: "cross_check_only");

        var evaluation = AdmissionService.Evaluate(report, registry);

        Assert.Empty(evaluation.ApprovedObservationIds);
        var quarantine = Assert.Single(evaluation.QuarantinedObservations);
        Assert.Equal("SOURCE_NOT_PUBLISHABLE", quarantine.Code);
    }

    [Fact]
    public void Evaluate_rejects_mismatched_trust_and_publish_policy()
    {
        var report = CreateReport(
            trustTier: "official",
            publishPolicy: "verified_copy");
        var registry = CreateRegistry(
            trustTier: "official",
            publishPolicy: "verified_copy");

        var evaluation = AdmissionService.Evaluate(report, registry);

        Assert.Empty(evaluation.ApprovedObservationIds);
        Assert.Equal(
            "SOURCE_NOT_PUBLISHABLE",
            Assert.Single(evaluation.QuarantinedObservations).Code);
    }

    [Fact]
    public void ValidateReceiptForPublish_rejects_package_changed_after_approval()
    {
        var stagingDirectory = CreateStagingDirectory();
        try
        {
            var report = CreateReport(
                trustTier: "official",
                publishPolicy: "authoritative");
            var registry = CreateRegistry(
                trustTier: "official",
                publishPolicy: "authoritative");
            var receipt = AdmissionService.CreateReceipt(
                stagingDirectory,
                report,
                registry,
                approvedBy: "legal-data-steward",
                approvalReference: "ADM-2026-002");
            AdmissionService.WriteReceipt(stagingDirectory, receipt);
            File.AppendAllText(
                Path.Combine(stagingDirectory, "chunks.jsonl"),
                "tampered");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                AdmissionService.ValidateReceiptForPublish(
                    stagingDirectory,
                    report,
                    registry));

            Assert.Contains("package_digest", exception.Message);
        }
        finally
        {
            Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    [Fact]
    public void Evaluate_quarantines_legacy_contract()
    {
        var report = CreateReport(
            trustTier: "official",
            publishPolicy: "authoritative",
            schemaVersion: null);

        var evaluation = AdmissionService.Evaluate(
            report,
            CreateRegistry("official", "authoritative"));

        Assert.Empty(evaluation.ApprovedObservationIds);
        Assert.Equal(
            "LEGACY_CONTRACT",
            Assert.Single(evaluation.QuarantinedObservations).Code);
    }

    private static ValidationReport CreateReport(
        string trustTier,
        string publishPolicy,
        string? schemaVersion = "1.0")
    {
        var observationId = Guid.NewGuid();
        var chunkSetId = Guid.NewGuid();
        const string text = "Noi dung van ban phap ly dung de kiem thu admission.";
        var hash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        var now = DateTime.UtcNow;
        var manifest = new StagingManifest(
            "JOB_ADMISSION",
            now,
            now,
            1,
            1,
            1,
            0,
            schemaVersion,
            "legal_reference",
            "registry-test-1",
            ["official-source"],
            new ManifestFilesDto(
                "document-observations.jsonl",
                "chunk-sets.jsonl",
                "chunks.jsonl",
                "errors.jsonl"));
        var observation = new DocumentObservationDto(
            observationId,
            manifest.JobId,
            "official_test_source",
            "vanban.example.gov.vn",
            "gov.vn",
            "authoritative",
            "gov:test:01-2026",
            "https://vanban.example.gov.vn/document/01-2026",
            "Van ban phap ly mau",
            "raw/document.html",
            hash,
            "text/html",
            "normalized/document.txt",
            hash,
            text.Length,
            text.Split(' ').Length,
            new ExtractionQualityDto("clean", false, 0.95),
            now,
            new SourceProvenanceDto(
                "official-source",
                "registry-test-1",
                "legal_reference",
                trustTier,
                "vanban.example.gov.vn",
                $"sha256:{hash}",
                publishPolicy,
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
                []));
        var chunkSet = new ChunkSetDto(
            chunkSetId,
            observationId,
            manifest.JobId,
            "contiguous_structure_aware_sliding",
            "2.0.0",
            "test-tokenizer",
            448,
            64,
            1,
            now,
            448,
            512);
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
            new ChunkAclDto(["public"], [], "internal"));
        return new ValidationReport(
            true,
            manifest,
            [observation],
            [chunkSet],
            [chunk],
            []);
    }

    private static SourceRegistryDocument CreateRegistry(
        string trustTier,
        string publishPolicy) => new(
            "1.0",
            "registry-test-1",
            [
                new SourceRegistryEntryDto(
                    "official-source",
                    "test-adapter",
                    "official_test_source",
                    "vanban.example.gov.vn",
                    "gov.vn",
                    "legal_reference",
                    trustTier,
                    publishPolicy,
                    ["vanban.example.gov.vn"],
                    "Co quan nha nuoc",
                    "vi")
            ]);

    private static string CreateStagingDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "digitalops-admission-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        foreach (var file in new[]
                 {
                     "manifest.json",
                     "document-observations.jsonl",
                     "chunk-sets.jsonl",
                     "chunks.jsonl"
                 })
        {
            File.WriteAllText(
                Path.Combine(directory, file),
                $"test:{file}",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        return directory;
    }
}
