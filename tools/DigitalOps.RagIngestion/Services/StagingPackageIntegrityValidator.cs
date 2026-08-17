using System.Security.Cryptography;
using System.Text;
using DigitalOps.RagIngestion.Models;

namespace DigitalOps.RagIngestion.Services;

internal static class StagingPackageIntegrityValidator
{
    private static readonly HashSet<string> KnownRoles = new(
        [
            "public",
            "Administrator",
            "Clerk",
            "Drafter",
            "Leader"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> KnownClassifications = new(
        ["public", "internal", "confidential", "restricted"],
        StringComparer.OrdinalIgnoreCase);

    public static void Validate(
        string stagingDirectory,
        StagingManifest? manifest,
        IReadOnlyList<DocumentObservationDto> observations,
        IReadOnlyList<ChunkSetDto> chunkSets,
        IReadOnlyList<ChunkDto> chunks,
        List<string> errors)
    {
        if (manifest is null)
        {
            errors.Add("manifest.json did not contain a valid manifest object.");
            return;
        }

        if (manifest.SchemaVersion is not null
            && !string.Equals(
                manifest.SchemaVersion,
                "1.0",
                StringComparison.Ordinal))
        {
            errors.Add(
                $"Unsupported staging schema_version '{manifest.SchemaVersion}'; supported version is 1.0.");
        }
        if (manifest.CorpusType is not ("general" or "legal_reference"))
        {
            errors.Add(
                $"Unsupported corpus_type '{manifest.CorpusType}'.");
        }
        if (manifest.SchemaVersion == "1.0")
        {
            if (manifest.Files is null
                || manifest.Files.ObservationsFile != "document-observations.jsonl"
                || manifest.Files.ChunkSetsFile != "chunk-sets.jsonl"
                || manifest.Files.ChunksFile != "chunks.jsonl"
                || manifest.Files.ErrorsFile != "crawler-errors.jsonl")
            {
                errors.Add(
                    "Schema 1.0 manifest files must use the canonical staging filenames.");
            }
            if (manifest.SourceRegistryEntryIds is null)
            {
                errors.Add(
                    "Schema 1.0 manifest is missing source_registry_entry_ids.");
            }
            else
            {
                var declaredEntryIds = manifest.SourceRegistryEntryIds
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                var actualEntryIds = observations
                    .Select(observation =>
                        observation.SourceProvenance?.RegistryEntryId)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                if (declaredEntryIds.Length
                        != manifest.SourceRegistryEntryIds.Count
                    || declaredEntryIds.Distinct(
                            StringComparer.Ordinal).Count()
                        != declaredEntryIds.Length)
                {
                    errors.Add(
                        "Schema 1.0 source_registry_entry_ids must be non-empty and unique.");
                }
                if (!declaredEntryIds.SequenceEqual(
                        actualEntryIds,
                        StringComparer.Ordinal))
                {
                    errors.Add(
                        "Manifest source_registry_entry_ids do not match observation provenance.");
                }
            }
            if (manifest.CorpusType == "legal_reference"
                && string.IsNullOrWhiteSpace(
                    manifest.SourceRegistryVersion))
            {
                errors.Add(
                    "Legal schema 1.0 manifest requires source_registry_version.");
            }
            if (observations.Any(observation =>
                    observation.SourceProvenance?.RegistryVersion is not null
                    && !string.Equals(
                        observation.SourceProvenance.RegistryVersion,
                        manifest.SourceRegistryVersion,
                        StringComparison.Ordinal)))
            {
                errors.Add(
                    "Observation registry_version does not match the manifest.");
            }
        }

        if (observations.Count == 0 || chunkSets.Count == 0 || chunks.Count == 0)
        {
            errors.Add(
                "A staging package must contain at least one observation, chunk set, and chunk.");
        }
        if (manifest.TotalObservations != observations.Count)
        {
            errors.Add(
                $"Manifest observation count {manifest.TotalObservations} does not match {observations.Count}.");
        }
        if (manifest.TotalChunkSets != chunkSets.Count)
        {
            errors.Add(
                $"Manifest chunk-set count {manifest.TotalChunkSets} does not match {chunkSets.Count}.");
        }
        if (manifest.TotalChunks != chunks.Count)
        {
            errors.Add(
                $"Manifest chunk count {manifest.TotalChunks} does not match {chunks.Count}.");
        }

        var observationsById = UniqueBy(
            observations,
            observation => observation.ObservationId,
            "observation_id",
            errors);
        var chunkSetsById = UniqueBy(
            chunkSets,
            chunkSet => chunkSet.ChunkSetId,
            "chunk_set_id",
            errors);
        UniqueBy(chunks, chunk => chunk.ChunkId, "chunk_id", errors);
        var canonicalKeys = new HashSet<string>(StringComparer.Ordinal);

        var normalizedTexts = new Dictionary<Guid, string>();
        foreach (var observation in observations)
        {
            if (string.IsNullOrWhiteSpace(observation.CanonicalDocumentKey)
                || !canonicalKeys.Add(observation.CanonicalDocumentKey))
            {
                errors.Add(
                    $"Observation {observation.ObservationId} has an empty or duplicate canonical_document_key.");
            }
            if (!string.Equals(
                    observation.JobId,
                    manifest.JobId,
                    StringComparison.Ordinal))
            {
                errors.Add(
                    $"Observation {observation.ObservationId} belongs to job '{observation.JobId}', not '{manifest.JobId}'.");
            }

            ValidateArtifactHash(
                stagingDirectory,
                observation.RawArtifactUri,
                observation.RawSha256,
                $"raw artifact for observation {observation.ObservationId}",
                errors);

            var normalizedPath = ResolvePath(
                stagingDirectory,
                observation.NormalizedTextUri,
                $"normalized text for observation {observation.ObservationId}",
                errors);
            if (normalizedPath is null)
            {
                continue;
            }
            if (!File.Exists(normalizedPath))
            {
                errors.Add(
                    $"Normalized text for observation {observation.ObservationId} does not exist: {normalizedPath}");
                continue;
            }

            try
            {
                var bytes = File.ReadAllBytes(normalizedPath);
                var actualHash = Convert.ToHexString(
                    SHA256.HashData(bytes)).ToLowerInvariant();
                if (!string.Equals(
                        actualHash,
                        observation.NormalizedTextSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(
                        $"Normalized text hash mismatch for observation {observation.ObservationId}.");
                }

                var text = Encoding.UTF8.GetString(bytes);
                normalizedTexts[observation.ObservationId] = text;
                var characterCount = text.EnumerateRunes().Count();
                if (characterCount != observation.CharCount)
                {
                    errors.Add(
                        $"Normalized character count mismatch for observation {observation.ObservationId}: expected {observation.CharCount}, got {characterCount}.");
                }
            }
            catch (Exception exception)
            {
                errors.Add(
                    $"Unable to validate normalized text for observation {observation.ObservationId}: {exception.Message}");
            }
        }

        foreach (var observation in observations)
        {
            var observationChunkSets = chunkSets.Count(
                chunkSet => chunkSet.ObservationId == observation.ObservationId);
            if (observationChunkSets != 1)
            {
                errors.Add(
                    $"Observation {observation.ObservationId} must have exactly one chunk set; found {observationChunkSets}.");
            }
        }

        foreach (var chunkSet in chunkSets)
        {
            if (!observationsById.ContainsKey(chunkSet.ObservationId))
            {
                errors.Add(
                    $"Chunk set {chunkSet.ChunkSetId} references missing observation {chunkSet.ObservationId}.");
            }
            if (!string.Equals(chunkSet.JobId, manifest.JobId, StringComparison.Ordinal))
            {
                errors.Add(
                    $"Chunk set {chunkSet.ChunkSetId} belongs to a different job.");
            }
            var softMax = chunkSet.SoftMaxTokens ?? chunkSet.TargetTokens;
            var hardMax = chunkSet.MaxTokens ?? Math.Max(
                softMax,
                chunkSet.TargetTokens);
            if (!(chunkSet.OverlapTokens < chunkSet.TargetTokens
                && chunkSet.TargetTokens <= softMax
                && softMax <= hardMax
                && hardMax <= 512))
            {
                errors.Add(
                    $"Chunk set {chunkSet.ChunkSetId} has invalid token limits.");
            }

            var setChunks = chunks
                .Where(chunk => chunk.ChunkSetId == chunkSet.ChunkSetId)
                .OrderBy(chunk => chunk.ChunkIndex)
                .ToArray();
            if (setChunks.Length != chunkSet.TotalChunks)
            {
                errors.Add(
                    $"Chunk set {chunkSet.ChunkSetId} declares {chunkSet.TotalChunks} chunks but contains {setChunks.Length}.");
            }
            if (!setChunks.Select(chunk => chunk.ChunkIndex)
                    .SequenceEqual(Enumerable.Range(0, setChunks.Length)))
            {
                errors.Add(
                    $"Chunk set {chunkSet.ChunkSetId} has non-contiguous chunk indexes.");
            }
        }

        foreach (var chunk in chunks)
        {
            ValidateAcl(chunk, errors);
            if (!chunkSetsById.TryGetValue(chunk.ChunkSetId, out var chunkSet))
            {
                errors.Add(
                    $"Chunk {chunk.ChunkId} references missing chunk set {chunk.ChunkSetId}.");
                continue;
            }
            if (chunk.TokenCount is <= 0 or > 512)
            {
                errors.Add(
                    $"Chunk {chunk.ChunkId} token_count {chunk.TokenCount} is outside 1..512.");
            }
            var actualChunkHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(chunk.Text)))
                .ToLowerInvariant();
            if (!string.Equals(
                    actualChunkHash,
                    chunk.ContentSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Chunk {chunk.ChunkId} content hash mismatch.");
            }

            if (!normalizedTexts.TryGetValue(chunkSet.ObservationId, out var text))
            {
                continue;
            }
            var runes = text.EnumerateRunes().ToArray();
            if (chunk.CharacterStart < 0
                || chunk.CharacterEnd < chunk.CharacterStart
                || chunk.CharacterEnd > runes.Length)
            {
                errors.Add($"Chunk {chunk.ChunkId} has invalid character offsets.");
                continue;
            }
            var slicedText = string.Concat(
                runes[chunk.CharacterStart..chunk.CharacterEnd]
                    .Select(rune => rune.ToString()));
            if (!string.Equals(slicedText, chunk.Text, StringComparison.Ordinal))
            {
                errors.Add(
                    $"Chunk {chunk.ChunkId} text does not match its normalized-text offsets.");
            }
        }
    }

    private static void ValidateAcl(ChunkDto chunk, List<string> errors)
    {
        var acl = chunk.ChunkAcl;
        if (acl is null)
        {
            errors.Add($"Chunk {chunk.ChunkId} is missing chunk_acl.");
            return;
        }
        if (acl.AllowedRoles is null || acl.AllowedRoles.Count == 0)
        {
            errors.Add($"Chunk {chunk.ChunkId} must allow at least one role.");
            return;
        }
        if (acl.DeniedRoles is null)
        {
            errors.Add($"Chunk {chunk.ChunkId} has a null denied_roles list.");
            return;
        }
        if (acl.AllowedRoles.Count > 32 || acl.DeniedRoles.Count > 32)
        {
            errors.Add($"Chunk {chunk.ChunkId} contains too many ACL roles.");
        }

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in acl.AllowedRoles)
        {
            if (string.IsNullOrWhiteSpace(role)
                || role.Length > 64
                || !string.Equals(role, role.Trim(), StringComparison.Ordinal)
                || !KnownRoles.Contains(role)
                || !allowed.Add(role))
            {
                errors.Add(
                    $"Chunk {chunk.ChunkId} has an invalid or duplicate allowed role '{role}'.");
            }
        }
        var denied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in acl.DeniedRoles)
        {
            if (string.IsNullOrWhiteSpace(role)
                || role.Length > 64
                || !string.Equals(role, role.Trim(), StringComparison.Ordinal)
                || !KnownRoles.Contains(role)
                || !denied.Add(role))
            {
                errors.Add(
                    $"Chunk {chunk.ChunkId} has an invalid or duplicate denied role '{role}'.");
            }
        }
        if (allowed.Overlaps(denied))
        {
            errors.Add($"Chunk {chunk.ChunkId} has overlapping ACL roles.");
        }
        if (string.IsNullOrWhiteSpace(acl.SecurityClassification)
            || !KnownClassifications.Contains(acl.SecurityClassification))
        {
            errors.Add(
                $"Chunk {chunk.ChunkId} has invalid security classification '{acl.SecurityClassification}'.");
        }
    }
    private static Dictionary<Guid, T> UniqueBy<T>(
        IEnumerable<T> values,


        Func<T, Guid> keySelector,
        string keyName,
        List<string> errors)
    {
        var result = new Dictionary<Guid, T>();
        foreach (var value in values)
        {
            var key = keySelector(value);
            if (!result.TryAdd(key, value))
            {
                errors.Add($"Duplicate {keyName}: {key}.");
            }
        }
        return result;
    }

    private static void ValidateArtifactHash(
        string stagingDirectory,
        string artifactUri,
        string expectedHash,
        string label,
        List<string> errors)
    {
        var path = ResolvePath(stagingDirectory, artifactUri, label, errors);
        if (path is null)
        {
            return;
        }
        if (!File.Exists(path))
        {
            errors.Add($"{label} does not exist: {path}");
            return;
        }
        var actualHash = Convert.ToHexString(
            SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{label} hash mismatch.");
        }
    }

    private static string? ResolvePath(
        string stagingDirectory,
        string path,
        string label,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            errors.Add($"{label} has an empty path.");
            return null;
        }

        var root = Path.GetFullPath(stagingDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidate = Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(root, path));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.StartsWith(root, comparison))
        {
            errors.Add(
                $"{label} resolves outside the staging package: {path}");
            return null;
        }

        return candidate;
    }
}
