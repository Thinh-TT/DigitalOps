using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalOps.RagIngestion.Models;

namespace DigitalOps.RagIngestion.Services;

public sealed record ValidationReport(
    bool IsValid,
    StagingManifest? Manifest,
    List<DocumentObservationDto> Observations,
    List<ChunkSetDto> ChunkSets,
    List<ChunkDto> Chunks,
    List<string> Errors
);

public static class StagingValidator
{
    internal static readonly JsonSerializerOptions ContractJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static ValidationReport Validate(string stagingDirectoryPath)
    {
        var errors = new List<string>();
        var observations = new List<DocumentObservationDto>();
        var chunkSets = new List<ChunkSetDto>();
        var chunks = new List<ChunkDto>();

        if (!Directory.Exists(stagingDirectoryPath))
        {
            errors.Add($"Staging directory not found: {stagingDirectoryPath}");
            return new ValidationReport(false, null, observations, chunkSets, chunks, errors);
        }

        var manifestPath = Path.Combine(stagingDirectoryPath, "manifest.json");
        var obsPath = Path.Combine(stagingDirectoryPath, "document-observations.jsonl");
        var csPath = Path.Combine(stagingDirectoryPath, "chunk-sets.jsonl");
        var ckPath = Path.Combine(stagingDirectoryPath, "chunks.jsonl");

        if (!File.Exists(manifestPath)) errors.Add("Missing manifest.json");
        if (!File.Exists(obsPath)) errors.Add("Missing document-observations.jsonl");
        if (!File.Exists(csPath)) errors.Add("Missing chunk-sets.jsonl");
        if (!File.Exists(ckPath)) errors.Add("Missing chunks.jsonl");

        if (errors.Count > 0)
        {
            return new ValidationReport(false, null, observations, chunkSets, chunks, errors);
        }

        StagingManifest? manifest = null;
        try
        {
            var json = File.ReadAllText(manifestPath);
            manifest = JsonSerializer.Deserialize<StagingManifest>(
                json,
                ContractJsonOptions);
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to parse manifest.json: {ex.Message}");
        }

        // Validate Observations JSONL
        int lineNum = 0;
        foreach (var line in File.ReadLines(obsPath))
        {
            lineNum++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var obs = JsonSerializer.Deserialize<DocumentObservationDto>(
                    line,
                    ContractJsonOptions);
                if (obs != null) observations.Add(obs);
            }
            catch (Exception ex)
            {
                errors.Add($"document-observations.jsonl line {lineNum}: {ex.Message}");
            }
        }

        // Validate ChunkSets JSONL
        lineNum = 0;
        foreach (var line in File.ReadLines(csPath))
        {
            lineNum++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var cs = JsonSerializer.Deserialize<ChunkSetDto>(
                    line,
                    ContractJsonOptions);
                if (cs != null) chunkSets.Add(cs);
            }
            catch (Exception ex)
            {
                errors.Add($"chunk-sets.jsonl line {lineNum}: {ex.Message}");
            }
        }

        // Validate Chunks JSONL
        lineNum = 0;
        foreach (var line in File.ReadLines(ckPath))
        {
            lineNum++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var ck = JsonSerializer.Deserialize<ChunkDto>(
                    line,
                    ContractJsonOptions);
                if (ck != null) chunks.Add(ck);
            }
            catch (Exception ex)
            {
                errors.Add($"chunks.jsonl line {lineNum}: {ex.Message}");
            }
        }

        if (errors.Count == 0)
        {
            StagingPackageIntegrityValidator.Validate(
                stagingDirectoryPath,
                manifest,
                observations,
                chunkSets,
                chunks,
                errors);
        }
        bool isValid = errors.Count == 0;
        return new ValidationReport(isValid, manifest, observations, chunkSets, chunks, errors);
    }
}
