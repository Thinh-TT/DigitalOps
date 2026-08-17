using System.Net;
using DigitalOps.API.Shared.Data;
using DigitalOps.RagIngestion.Models;
using DigitalOps.RagIngestion.Services;
using Microsoft.EntityFrameworkCore;
using Qdrant.Client;

namespace DigitalOps.RagIngestion;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var commandLine = CommandLine.Parse(args);
        if (commandLine.ShowHelp)
        {
            Console.WriteLine(CommandLine.Usage);
            return 0;
        }

        if (commandLine.ShowVersion)
        {
            Console.WriteLine(
                typeof(Program).Assembly.GetName().Version?.ToString()
                ?? "unknown");
            return 0;
        }

        if (!commandLine.IsSuccess || commandLine.Options is null)
        {
            Console.Error.WriteLine($"[ERROR] {commandLine.Error}");
            Console.Error.WriteLine(CommandLine.Usage);
            return 1;
        }

        var options = commandLine.Options;
        PrintBanner();
        if (options.UsesLegacySyntax)
        {
            Console.WriteLine(
                "[DEPRECATION] Use the validate, plan, or publish command; "
                + "legacy flags remain temporarily supported.");
        }

        Console.WriteLine(
            $"[CLI] Operation: {options.Operation.ToString().ToLowerInvariant()}");
        Console.WriteLine(
            $"[CLI] Target staging directory: {options.StagingDirectory}");

        var report = StagingValidator.Validate(options.StagingDirectory);
        if (!report.IsValid)
        {
            Console.Error.WriteLine("[ERROR] Staging validation failed:");
            foreach (var error in report.Errors)
            {
                Console.Error.WriteLine($"  - {error}");
            }

            return 2;
        }

        Console.WriteLine(
            $"[VALIDATION] SUCCESS! Manifest JobId: '{report.Manifest?.JobId}'");
        Console.WriteLine($"  - Observations: {report.Observations.Count}");
        Console.WriteLine($"  - ChunkSets:    {report.ChunkSets.Count}");
        Console.WriteLine($"  - Chunks:       {report.Chunks.Count}");
        if (report.Manifest?.SchemaVersion is null)
        {
            Console.WriteLine(
                "[WARNING] Legacy staging package has no schema_version; it may be validated but cannot be admitted or published.");
        }

        if (options.Operation == RagIngestionOperation.Validate)
        {
            Console.WriteLine(
                "[VALIDATE] Complete. 0 DB/Qdrant writes, 0 network calls.");
            // Preserve the observable output used by existing scripts.
            Console.WriteLine(
                "[VALIDATE-ONLY] Complete. 0 DB/Qdrant writes, 0 network calls.");
            return 0;
        }

        if (options.Operation == RagIngestionOperation.Plan)
        {
            foreach (var chunk in report.Chunks)
            {
                _ = IngestionPipelineService.ComputeQdrantPointId(
                    Guid.Empty,
                    chunk.ChunkId);
            }

            Console.WriteLine(
                "[PLAN] Integrity and deterministic IDs verified. "
                + "0 DB writes, 0 Qdrant writes, 0 network calls.");
            Console.WriteLine(
                "[DRY-RUN] Integrity and deterministic IDs verified. "
                + "0 DB writes, 0 Qdrant writes, 0 network calls.");
            return 0;
        }

        SourceRegistryDocument registry;
        try
        {
            registry = AdmissionService.LoadRegistry(
                options.SourceRegistryPath!);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"[ERROR] Source registry validation failed: {exception.Message}");
            return 5;
        }

        if (options.Operation == RagIngestionOperation.Admit)
        {
            try
            {
                var receipt = AdmissionService.CreateReceipt(
                    options.StagingDirectory,
                    report,
                    registry,
                    options.ApprovedBy!,
                    options.ApprovalReference!);
                AdmissionService.WriteReceipt(
                    options.StagingDirectory,
                    receipt);
                Console.WriteLine(
                    $"[ADMISSION] Status: {receipt.Status}; approved: {receipt.ApprovedObservationIds.Count}; quarantined: {receipt.QuarantinedObservations.Count}.");
                foreach (var item in receipt.QuarantinedObservations)
                {
                    Console.WriteLine(
                        $"  - QUARANTINE {item.ObservationId:D} {item.Code}: {item.Reason}");
                }
                return receipt.ApprovedObservationIds.Count > 0 ? 0 : 5;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(
                    $"[ERROR] Admission failed: {exception.Message}");
                return 5;
            }
        }

        AdmissionReceipt? admissionReceipt = null;
        try
        {
            admissionReceipt = AdmissionService.ValidateReceiptForPublish(
                options.StagingDirectory,
                report,
                registry);
            report = AdmissionService.SelectApproved(report, admissionReceipt);
            Console.WriteLine(
                $"[ADMISSION] Verified '{admissionReceipt.ApprovalReference}' by '{admissionReceipt.ApprovedBy}'; publishing {report.Observations.Count} approved observation(s).");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"[ERROR] Publish admission gate rejected the package: {exception.Message}");
            return 5;
        }

        return await PublishAsync(options, report, admissionReceipt);
    }

    private static async Task<int> PublishAsync(
        RagIngestionOptions options,
        ValidationReport report,
        AdmissionReceipt admissionReceipt)
    {
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
                $"[ERROR] Invalid ingestion configuration: {exception.Message}");
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
                    isResume: options.Resume,
                    stagingDirectory: Path.GetFullPath(
                        options.StagingDirectory),
                    admissionReceipt: admissionReceipt);
                Console.WriteLine("[PUBLISH] Ingestion finished successfully.");
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

    private static void PrintBanner()
    {
        Console.WriteLine("===========================================");
        Console.WriteLine(" DigitalOps RAG Ingestion CLI");
        Console.WriteLine("===========================================");
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
