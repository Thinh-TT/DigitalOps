using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;

namespace DigitalOps.API.Shared.AI;

public static class AiProviderNames
{
    public const string Ollama = "Ollama";
    public const string External = "External";
}

public static class ExternalStructuredOutputModes
{
    public const string JsonSchema = "JsonSchema";

    public const string JsonObject = "JsonObject";
}

public sealed class AiProviderOptions
{
    public const string SectionName = "Ai";

    public string Provider { get; set; } = AiProviderNames.Ollama;

    public bool AutomaticFallback { get; set; }

    public int ContextTokens { get; set; } = 8192;

    public int TimeoutSeconds { get; set; } = 60;

    public int AssignmentMaxOutputTokens { get; set; } = 256;

    public int ReviewMaxOutputTokens { get; set; } = 768;

    public int DraftMaxOutputTokens { get; set; } = 1024;

    public OllamaAiOptions Ollama { get; set; } = new();

    public ExternalAiOptions External { get; set; } = new();

    public EmbeddingAiOptions Embedding { get; set; } = new();

    public QdrantAiOptions Qdrant { get; set; } = new();
}

public sealed class OllamaAiOptions
{
    public const string ApprovedLlmModel = "qwen3:4b-instruct-2507-q4_K_M";

    public const string ApprovedLlmDigest =
        "0edcdef34593eac1aa2be9c7d06c432dcf81945adca5eca2f27662c18f168ba0";

    public string BaseUrl { get; set; } = "http://127.0.0.1:11434";

    public string LlmModel { get; set; } = ApprovedLlmModel;

    public string LlmDigest { get; set; } = ApprovedLlmDigest;
}

public sealed class ExternalAiOptions
{
    public string BaseUrl { get; set; } = string.Empty;

    public string ChatCompletionsPath { get; set; } = "/v1/chat/completions";

    public string Model { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public bool SupportsStructuredOutputs { get; set; }

    public string StructuredOutputMode { get; set; } = ExternalStructuredOutputModes.JsonSchema;

    public bool DisableThinking { get; set; }
}

public sealed class EmbeddingAiOptions
{
    public const string ApprovedModel = "qwen3-embedding:0.6b";

    public const string ApprovedDigest =
        "ac6da0dfba84a81fdbfbaf330198c33cd77c4cdfc53e8bc50eb581914a15621d";

    public string Provider { get; set; } = AiProviderNames.Ollama;

    public string Model { get; set; } = ApprovedModel;

    public string Digest { get; set; } = ApprovedDigest;

    public int Dimensions { get; set; } = 1024;
}

public sealed class QdrantAiOptions
{
    public const string ApprovedCollectionName = "digitalops_knowledge_v1";

    public const double ApprovedMinScore = 0.316666;

    public string BaseUrl { get; set; } = "http://127.0.0.1:6333";

    public string ApiKey { get; set; } = string.Empty;

    public string CollectionName { get; set; } = ApprovedCollectionName;

    public double MinScore { get; set; } = ApprovedMinScore;
}

public sealed class AiProviderOptionsValidator(
    IHostEnvironment environment) : IValidateOptions<AiProviderOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        AiProviderOptions options)
    {
        var failures = new List<string>();

        if (!string.Equals(options.Provider, AiProviderNames.Ollama, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(options.Provider, AiProviderNames.External, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                $"{AiProviderOptions.SectionName}:Provider must be Ollama or External.");
        }

        if (options.AutomaticFallback)
        {
            failures.Add(
                $"{AiProviderOptions.SectionName}:AutomaticFallback must remain false.");
        }

        if (options.ContextTokens != 8192)
        {
            failures.Add(
                $"{AiProviderOptions.SectionName}:ContextTokens must be 8192 for the approved contract.");
        }

        if (options.TimeoutSeconds <= 0 || options.TimeoutSeconds > 60)
        {
            failures.Add(
                $"{AiProviderOptions.SectionName}:TimeoutSeconds must be between 1 and 60.");
        }

        ValidateOutputBudget(
            failures,
            nameof(options.AssignmentMaxOutputTokens),
            options.AssignmentMaxOutputTokens,
            256);
        ValidateOutputBudget(
            failures,
            nameof(options.ReviewMaxOutputTokens),
            options.ReviewMaxOutputTokens,
            768);
        ValidateOutputBudget(
            failures,
            nameof(options.DraftMaxOutputTokens),
            options.DraftMaxOutputTokens,
            1024);

        if (!string.Equals(options.Embedding.Provider, AiProviderNames.Ollama, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                $"{AiProviderOptions.SectionName}:Embedding:Provider must remain Ollama.");
        }

        if (!string.Equals(
                options.Embedding.Model,
                EmbeddingAiOptions.ApprovedModel,
                StringComparison.Ordinal)
            || !string.Equals(
                options.Embedding.Digest,
                EmbeddingAiOptions.ApprovedDigest,
                StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                $"{AiProviderOptions.SectionName}:Embedding model and digest must match the approved baseline.");
        }

        if (options.Embedding.Dimensions != 1024)
        {
            failures.Add(
                $"{AiProviderOptions.SectionName}:Embedding:Dimensions must remain 1024.");
        }

        ValidateOllama(failures, options.Ollama);
        ValidateQdrant(failures, options.Qdrant);

        if (string.Equals(options.Provider, AiProviderNames.External, StringComparison.OrdinalIgnoreCase))
        {
            if (!environment.IsDevelopment())
            {
                failures.Add(
                    "Ai:Provider=External is allowed only in the Development environment.");
            }

            if (!Uri.TryCreate(options.External.BaseUrl, UriKind.Absolute, out var externalUri)
                || (externalUri.Scheme != Uri.UriSchemeHttps && !externalUri.IsLoopback))
            {
                failures.Add(
                    "Ai:External:BaseUrl must be HTTPS (or loopback HTTP for a local test server).");
            }

            if (string.IsNullOrWhiteSpace(options.External.ChatCompletionsPath)
                || !options.External.ChatCompletionsPath.StartsWith('/'))
            {
                failures.Add(
                    "Ai:External:ChatCompletionsPath must be an absolute path beginning with '/'.");
            }

            if (string.IsNullOrWhiteSpace(options.External.Model))
            {
                failures.Add("Ai:External:Model is required when External is selected.");
            }

            if (string.IsNullOrWhiteSpace(options.External.ApiKey))
            {
                failures.Add("Ai:External:ApiKey is required when External is selected.");
            }

            if (!options.External.SupportsStructuredOutputs)
            {
                failures.Add(
                    "Ai:External:SupportsStructuredOutputs must be true for the approved JSON Schema contract.");
            }

            if (!string.Equals(
                    options.External.StructuredOutputMode,
                    ExternalStructuredOutputModes.JsonSchema,
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(
                    options.External.StructuredOutputMode,
                    ExternalStructuredOutputModes.JsonObject,
                    StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(
                    "Ai:External:StructuredOutputMode must be JsonSchema or JsonObject.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateOllama(
        ICollection<string> failures,
        OllamaAiOptions options)
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp
            || !uri.IsLoopback)
        {
            failures.Add(
                "Ai:Ollama:BaseUrl must be an HTTP loopback URL.");
        }

        if (!string.Equals(
                options.LlmModel,
                OllamaAiOptions.ApprovedLlmModel,
                StringComparison.Ordinal)
            || !string.Equals(
                options.LlmDigest,
                OllamaAiOptions.ApprovedLlmDigest,
                StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                "Ai:Ollama LLM model and digest must match the approved baseline.");
        }
    }

    private static void ValidateQdrant(
        ICollection<string> failures,
        QdrantAiOptions options)
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp
            || !uri.IsLoopback)
        {
            failures.Add(
                "Ai:Qdrant:BaseUrl must be an HTTP loopback URL for the approved MVP/demo baseline.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add("Ai:Qdrant:ApiKey is required.");
        }

        if (!string.Equals(
                options.CollectionName,
                QdrantAiOptions.ApprovedCollectionName,
                StringComparison.Ordinal))
        {
            failures.Add(
                $"Ai:Qdrant:CollectionName must remain {QdrantAiOptions.ApprovedCollectionName}.");
        }

        if (Math.Abs(options.MinScore - QdrantAiOptions.ApprovedMinScore) > 0.0000001)
        {
            failures.Add(
                $"Ai:Qdrant:MinScore must remain {QdrantAiOptions.ApprovedMinScore}.");
        }
    }

    private static void ValidateOutputBudget(
        ICollection<string> failures,
        string propertyName,
        int actual,
        int expected)
    {
        if (actual != expected)
        {
            failures.Add(
                $"{AiProviderOptions.SectionName}:{propertyName} must be {expected} for the approved contract.");
        }
    }
}
