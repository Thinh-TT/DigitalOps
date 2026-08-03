using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using DigitalOps.API.Shared.Data;
using DxOs.Workers.Services;
using Microsoft.EntityFrameworkCore;
using Qdrant.Client;

namespace DxOs.Workers;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=================================================");
        Console.WriteLine(" DigitalOps RAG Ingestion CLI Worker (DxOs.Workers)");
        Console.WriteLine("=================================================");

        string stagingDir = "";
        bool validateOnly = false;
        bool dryRun = false;
        bool resume = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--staging-dir" && i + 1 < args.Length)
            {
                stagingDir = args[i + 1];
            }
            else if (args[i] == "--validate-only")
            {
                validateOnly = true;
            }
            else if (args[i] == "--dry-run")
            {
                dryRun = true;
            }
            else if (args[i] == "--resume")
            {
                resume = true;
            }
        }

        if (string.IsNullOrWhiteSpace(stagingDir))
        {
            Console.WriteLine("Usage: dotnet run --project DxOs.Workers -- --staging-dir <path> [--validate-only] [--dry-run] [--resume]");
            return 1;
        }

        Console.WriteLine($"[CLI] Target Staging Directory: {stagingDir}");
        var report = StagingValidator.Validate(stagingDir);

        if (!report.IsValid)
        {
            Console.WriteLine("[ERROR] Staging validation failed:");
            foreach (var err in report.Errors)
            {
                Console.WriteLine($"  - {err}");
            }
            return 2;
        }

        Console.WriteLine($"[VALIDATION] SUCCESS! Manifest JobId: '{report.Manifest?.JobId}'");
        Console.WriteLine($"  - Observations: {report.Observations.Count}");
        Console.WriteLine($"  - ChunkSets:    {report.ChunkSets.Count}");
        Console.WriteLine($"  - Chunks:       {report.Chunks.Count}");

        if (validateOnly)
        {
            Console.WriteLine("[VALIDATE-ONLY] Complete. 0 DB/Qdrant writes, 0 network calls.");
            return 0;
        }

        if (dryRun)
        {
            foreach (var chunk in report.Chunks)
            {
                _ = IngestionPipelineService.ComputeQdrantPointId(
                    Guid.Empty,
                    chunk.ChunkId);
            }
            Console.WriteLine(
                "[DRY-RUN] Integrity and deterministic IDs verified. "
                + "0 DB writes, 0 Qdrant writes, 0 network calls.");
            return 0;
        }

        // Setup DbContext and Services
        DigitalOpsDbContext dbContext;
        HttpClient httpClient;
        QdrantIngestionClient ingestionClient;
        try
        {
            var connectionString = GetRequiredEnvironmentVariable(
                "ConnectionStrings__DigitalOps");
            var optionsBuilder =
                new DbContextOptionsBuilder<DigitalOpsDbContext>();
            optionsBuilder.UseNpgsql(connectionString);
            dbContext = new DigitalOpsDbContext(optionsBuilder.Options);

            var ollamaUri = new Uri(
                Environment.GetEnvironmentVariable("Ai__Ollama__BaseUrl")
                ?? "http://127.0.0.1:11434");
            if (!IsLoopbackHost(ollamaUri.Host))
            {
                throw new InvalidOperationException(
                    "Ai__Ollama__BaseUrl must use a loopback host.");
            }
            httpClient = new HttpClient
            {
                BaseAddress = ollamaUri,
                Timeout = TimeSpan.FromSeconds(120)
            };

            var qdrantHost =
                Environment.GetEnvironmentVariable("Rag__QdrantGrpcHost")
                ?? "127.0.0.1";
            if (!IsLoopbackHost(qdrantHost))
            {
                throw new InvalidOperationException(
                    "Rag__QdrantGrpcHost must be a loopback host.");
            }
            var qdrantPortText =
                Environment.GetEnvironmentVariable("Rag__QdrantGrpcPort")
                ?? "6334";
            if (!int.TryParse(qdrantPortText, out var qdrantPort)
                || qdrantPort is < 1 or > 65535)
            {
                throw new InvalidOperationException(
                    "Rag__QdrantGrpcPort must be a valid TCP port.");
            }
            var qdrantApiKey = GetRequiredEnvironmentVariable(
                "Ai__Qdrant__ApiKey");
            var qdrantClient = new QdrantClient(
                qdrantHost,
                qdrantPort,
                https: false,
                apiKey: qdrantApiKey);
            ingestionClient = new QdrantIngestionClient(qdrantClient);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"[ERROR] Invalid worker configuration: {exception.Message}");
            return 3;
        }
        using (dbContext)
        using (httpClient)
        {
            var embeddingService = new OllamaEmbeddingService(httpClient);

            var pipeline = new IngestionPipelineService(
                dbContext,
                embeddingService,
                ingestionClient);
            try
            {
                await pipeline.ProcessStagingPackageAsync(
                    report,
                    isResume: resume,
                    stagingDirectory: Path.GetFullPath(stagingDir));
                Console.WriteLine("[COMPLETE] Ingestion finished successfully.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(
                    $"[ERROR] Ingestion failed: {exception.Message}");
                return 4;
            }
        }
    }

    private static string GetRequiredEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Required environment variable '{name}' is not configured.");
        }
        return value;
    }

    private static bool IsLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || IPAddress.TryParse(host, out var address)
            && IPAddress.IsLoopback(address);
}
