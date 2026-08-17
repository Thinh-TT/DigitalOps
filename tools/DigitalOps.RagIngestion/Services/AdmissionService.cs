using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalOps.RagIngestion.Models;

namespace DigitalOps.RagIngestion.Services;

public sealed record AdmissionEvaluation(
    IReadOnlyList<Guid> ApprovedObservationIds,
    IReadOnlyList<AdmissionQuarantineItem> QuarantinedObservations)
{
    public string Status => ApprovedObservationIds.Count == 0
        ? "rejected"
        : QuarantinedObservations.Count == 0
            ? "approved"
            : "partially_approved";
}

public static class AdmissionService
{
    public const string ReceiptFileName = "admission.json";
    private const int MaxRegistryBytes = 1024 * 1024;
    private static readonly HashSet<string> LegalStatuses = new(
        ["current", "expired", "repealed", "superseded", "status_unknown"],
        StringComparer.Ordinal);

    public static SourceRegistryDocument LoadRegistry(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            throw new InvalidOperationException(
                $"Source registry does not exist: {fullPath}");
        }
        if (info.Length is <= 0 or > MaxRegistryBytes)
        {
            throw new InvalidOperationException(
                "Source registry size is outside the allowed range.");
        }

        SourceRegistryDocument registry;
        try
        {
            registry = JsonSerializer.Deserialize<SourceRegistryDocument>(
                File.ReadAllText(fullPath),
                StagingValidator.ContractJsonOptions)
                ?? throw new InvalidOperationException(
                    "Source registry did not contain an object.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "Source registry is not valid JSON.",
                exception);
        }

        ValidateRegistry(registry);
        return registry;
    }

    public static AdmissionEvaluation Evaluate(
        ValidationReport report,
        SourceRegistryDocument registry)
    {
        if (!report.IsValid || report.Manifest is null)
        {
            throw new ArgumentException(
                "Admission requires a valid staging report.",
                nameof(report));
        }

        var approved = new List<Guid>();
        var quarantined = new List<AdmissionQuarantineItem>();
        var entries = registry.Sources.ToDictionary(
            entry => entry.EntryId,
            StringComparer.Ordinal);

        foreach (var observation in report.Observations)
        {
            var issue = EvaluateObservation(report.Manifest, observation, registry, entries);
            if (issue is null)
            {
                approved.Add(observation.ObservationId);
            }
            else
            {
                quarantined.Add(issue);
            }
        }

        return new AdmissionEvaluation(approved, quarantined);
    }

    public static AdmissionReceipt CreateReceipt(
        string stagingDirectory,
        ValidationReport report,
        SourceRegistryDocument registry,
        string approvedBy,
        string approvalReference,
        DateTime? approvedAt = null)
    {
        ValidateApprovalText(approvedBy, nameof(approvedBy));
        ValidateApprovalText(approvalReference, nameof(approvalReference));
        var evaluation = Evaluate(report, registry);
        return new AdmissionReceipt(
            "1.0",
            ComputePackageDigest(stagingDirectory, report),
            registry.RegistryVersion,
            evaluation.Status,
            approvedBy.Trim(),
            (approvedAt ?? DateTime.UtcNow).ToUniversalTime(),
            approvalReference.Trim(),
            evaluation.ApprovedObservationIds.ToList(),
            evaluation.QuarantinedObservations.ToList());
    }

    public static void WriteReceipt(
        string stagingDirectory,
        AdmissionReceipt receipt)
    {
        var directory = Path.GetFullPath(stagingDirectory);
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, ReceiptFileName);
        var temporary = destination + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(
                receipt,
                new JsonSerializerOptions(
                    StagingValidator.ContractJsonOptions)
                {
                    WriteIndented = true
                }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, destination, overwrite: true);
    }

    public static AdmissionReceipt ValidateReceiptForPublish(
        string stagingDirectory,
        ValidationReport report,
        SourceRegistryDocument registry)
    {
        var receiptPath = Path.Combine(
            Path.GetFullPath(stagingDirectory),
            ReceiptFileName);
        if (!File.Exists(receiptPath))
        {
            throw new InvalidOperationException(
                "admission.json is required before publish.");
        }

        AdmissionReceipt receipt;
        try
        {
            receipt = JsonSerializer.Deserialize<AdmissionReceipt>(
                File.ReadAllText(receiptPath),
                StagingValidator.ContractJsonOptions)
                ?? throw new InvalidOperationException(
                    "admission.json did not contain an object.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "admission.json is not valid JSON.",
                exception);
        }

        if (receipt.SchemaVersion != "1.0")
        {
            throw new InvalidOperationException(
                $"Unsupported admission schema_version '{receipt.SchemaVersion}'.");
        }
        ValidateApprovalText(receipt.ApprovedBy, "approved_by");
        ValidateApprovalText(receipt.ApprovalReference, "approval_reference");
        if (receipt.ApprovedAt.Kind == DateTimeKind.Unspecified
            || receipt.ApprovedAt > DateTime.UtcNow.AddMinutes(5))
        {
            throw new InvalidOperationException(
                "admission approved_at must be a valid UTC timestamp.");
        }
        if (!string.Equals(
                receipt.RegistryVersion,
                registry.RegistryVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Admission registry_version does not match the current registry.");
        }
        var digest = ComputePackageDigest(stagingDirectory, report);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(receipt.PackageDigest.ToLowerInvariant()),
                Encoding.ASCII.GetBytes(digest)))
        {
            throw new InvalidOperationException(
                "Admission package_digest does not match the current staging package.");
        }

        var current = Evaluate(report, registry);
        if (current.ApprovedObservationIds.Count == 0)
        {
            throw new InvalidOperationException(
                "The current package has no observations eligible for publish.");
        }
        if (!string.Equals(receipt.Status, current.Status, StringComparison.Ordinal)
            || !receipt.ApprovedObservationIds.Order()
                .SequenceEqual(current.ApprovedObservationIds.Order()))
        {
            throw new InvalidOperationException(
                "Admission approval no longer matches the current eligibility evaluation.");
        }

        return receipt;
    }

    public static ValidationReport SelectApproved(
        ValidationReport report,
        AdmissionReceipt receipt)
    {
        var approved = receipt.ApprovedObservationIds.ToHashSet();
        var observations = report.Observations
            .Where(observation => approved.Contains(observation.ObservationId))
            .ToList();
        var observationIds = observations
            .Select(observation => observation.ObservationId)
            .ToHashSet();
        var chunkSets = report.ChunkSets
            .Where(chunkSet => observationIds.Contains(chunkSet.ObservationId))
            .ToList();
        var chunkSetIds = chunkSets
            .Select(chunkSet => chunkSet.ChunkSetId)
            .ToHashSet();
        var chunks = report.Chunks
            .Where(chunk => chunkSetIds.Contains(chunk.ChunkSetId))
            .ToList();
        return new ValidationReport(
            true,
            report.Manifest,
            observations,
            chunkSets,
            chunks,
            []);
    }

    public static string ComputePackageDigest(
        string stagingDirectory,
        ValidationReport report)
    {
        var root = Path.GetFullPath(stagingDirectory);
        using var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var fileName in new[]
                 {
                     "manifest.json",
                     "document-observations.jsonl",
                     "chunk-sets.jsonl",
                     "chunks.jsonl"
                 })
        {
            Append(incremental, fileName);
            incremental.AppendData(File.ReadAllBytes(Path.Combine(root, fileName)));
        }
        foreach (var observation in report.Observations
                     .OrderBy(value => value.ObservationId))
        {
            Append(incremental, observation.ObservationId.ToString("D"));
            Append(incremental, observation.RawSha256.ToLowerInvariant());
            Append(incremental, observation.NormalizedTextSha256.ToLowerInvariant());
        }
        return Convert.ToHexStringLower(incremental.GetHashAndReset());
    }

    private static AdmissionQuarantineItem? EvaluateObservation(
        StagingManifest manifest,
        DocumentObservationDto observation,
        SourceRegistryDocument registry,
        IReadOnlyDictionary<string, SourceRegistryEntryDto> entries)
    {
        AdmissionQuarantineItem Reject(string code, string reason) =>
            new(observation.ObservationId, code, reason);

        if (manifest.SchemaVersion != "1.0")
        {
            return Reject("LEGACY_CONTRACT", "Publish requires staging schema 1.0.");
        }
        if (manifest.CorpusType != "legal_reference")
        {
            return Reject(
                "CORPUS_NOT_LEGAL",
                "Only governed legal_reference packages may be published.");
        }
        if (!string.Equals(
                manifest.SourceRegistryVersion,
                registry.RegistryVersion,
                StringComparison.Ordinal))
        {
            return Reject(
                "REGISTRY_VERSION_MISMATCH",
                "Package registry version does not match the admission registry.");
        }

        var provenance = observation.SourceProvenance;
        if (provenance?.RegistryEntryId is null
            || !entries.TryGetValue(provenance.RegistryEntryId, out var entry))
        {
            return Reject(
                "SOURCE_NOT_REGISTERED",
                "Observation does not resolve to a source registry entry.");
        }
        if (!string.Equals(
                provenance.RegistryVersion,
                registry.RegistryVersion,
                StringComparison.Ordinal)
            || !string.Equals(
                provenance.CorpusType,
                "legal_reference",
                StringComparison.Ordinal)
            || !string.Equals(
                provenance.SourceTrustTier,
                entry.SourceTrustTier,
                StringComparison.Ordinal)
            || !string.Equals(
                provenance.PublishPolicy,
                entry.PublishPolicy,
                StringComparison.Ordinal)
            || !string.Equals(
                observation.SourceId,
                entry.SourceId,
                StringComparison.Ordinal))
        {
            return Reject(
                "SOURCE_PROVENANCE_MISMATCH",
                "Observation provenance does not match the source registry.");
        }
        var publishableTrustPolicy =
            entry.SourceTrustTier == "official"
                && entry.PublishPolicy == "authoritative"
            || entry.SourceTrustTier == "verified_copy"
                && entry.PublishPolicy == "verified_copy";
        if (!publishableTrustPolicy)
        {
            return Reject(
                "SOURCE_NOT_PUBLISHABLE",
                "This trust-tier/publish-policy pair is not eligible for publish.");
        }
        if (!Uri.TryCreate(
                observation.SourceDocumentUrl,
                UriKind.Absolute,
                out var sourceUri)
            || sourceUri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(sourceUri.UserInfo)
            || !entry.AllowedHosts.Contains(
                sourceUri.Host.TrimEnd('.'),
                StringComparer.OrdinalIgnoreCase)
            || !string.Equals(
                provenance.SourceDomain.TrimEnd('.'),
                sourceUri.Host.TrimEnd('.'),
                StringComparison.OrdinalIgnoreCase))
        {
            return Reject(
                "SOURCE_URL_NOT_ALLOWED",
                "Observation URL is outside the registered source hosts.");
        }
        if (string.IsNullOrWhiteSpace(provenance.SourceVersion)
            || provenance.SourceVersion.Length > 256
            || string.IsNullOrWhiteSpace(provenance.Language))
        {
            return Reject(
                "SOURCE_VERSION_MISSING",
                "Source version and language are required.");
        }
        if (observation.ExtractionQuality.Status == "failed"
            || observation.ExtractionQuality.ConfidenceScore < 0.5)
        {
            return Reject(
                "EXTRACTION_QUALITY_LOW",
                "Extraction quality is not sufficient for indexing.");
        }

        var legal = observation.LegalMetadata;
        if (legal is null
            || string.IsNullOrWhiteSpace(legal.DocumentNumber)
            || string.IsNullOrWhiteSpace(legal.DocumentType)
            || string.IsNullOrWhiteSpace(legal.Issuer)
            || legal.IssuedDate is null)
        {
            return Reject(
                "LEGAL_METADATA_INCOMPLETE",
                "Document number, type, issuer, and issued date are required.");
        }
        if (!LegalStatuses.Contains(legal.LegalStatus))
        {
            return Reject(
                "LEGAL_STATUS_INVALID",
                "Legal status is outside the approved vocabulary.");
        }
        if (legal.EffectiveFrom is not null
            && legal.EffectiveTo is not null
            && legal.EffectiveFrom > legal.EffectiveTo)
        {
            return Reject(
                "EFFECTIVITY_INVALID",
                "effective_from must not be after effective_to.");
        }
        return null;
    }

    private static void ValidateRegistry(SourceRegistryDocument registry)
    {
        if (registry.SchemaVersion != "1.0"
            || string.IsNullOrWhiteSpace(registry.RegistryVersion)
            || registry.Sources is null
            || registry.Sources.Count == 0)
        {
            throw new InvalidOperationException(
                "Source registry header is invalid or unsupported.");
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in registry.Sources)
        {
            if (string.IsNullOrWhiteSpace(entry.EntryId)
                || !ids.Add(entry.EntryId)
                || string.IsNullOrWhiteSpace(entry.SourceId)
                || entry.CorpusType is not ("general" or "legal_reference")
                || entry.SourceTrustTier is not (
                    "official" or "verified_copy" or "aggregator" or "unverified")
                || entry.PublishPolicy is not (
                    "authoritative" or "verified_copy" or "cross_check_only" or "blocked")
                || entry.AllowedHosts is null
                || entry.AllowedHosts.Count == 0
                || entry.AllowedHosts.Any(host =>
                    string.IsNullOrWhiteSpace(host)
                    || host != host.Trim().ToLowerInvariant().TrimEnd('.')
                    || Uri.CheckHostName(host) != UriHostNameType.Dns)
                || entry.AllowedHosts.Distinct(
                    StringComparer.OrdinalIgnoreCase).Count()
                    != entry.AllowedHosts.Count)
            {
                throw new InvalidOperationException(
                    $"Source registry entry '{entry.EntryId}' is invalid.");
            }
        }
    }

    private static void ValidateApprovalText(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 256
            || value.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                $"Admission {field} is required and must be at most 256 printable characters.");
        }
    }

    private static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }
}
