using System.Net;
using System.Text;
using System.Text.Json;
using DigitalOps.API.Shared.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DigitalOps.API.Tests;

public sealed class AiProviderTests
{
    [Fact]
    public void Ollama_development_options_are_valid()
    {
        var result = Validate(CreateOllamaOptions(), Environments.Development);

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public void External_provider_is_valid_only_in_development()
    {
        var options = CreateExternalOptions();

        var developmentResult = Validate(options, Environments.Development);
        var productionResult = Validate(options, Environments.Production);

        Assert.True(developmentResult.Succeeded, developmentResult.FailureMessage);
        Assert.False(productionResult.Succeeded);
        Assert.Contains(
            "allowed only in the Development environment",
            productionResult.FailureMessage,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Unsafe_fallback_and_contract_changes_are_rejected()
    {
        var options = CreateOllamaOptions();
        options.AutomaticFallback = true;
        options.ContextTokens = 4096;
        options.DraftMaxOutputTokens = 192;

        var result = Validate(options, Environments.Development);

        Assert.False(result.Succeeded);
        Assert.Contains("AutomaticFallback", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("ContextTokens", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("DraftMaxOutputTokens", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Approved_local_model_and_embedding_baseline_cannot_drift()
    {
        var options = CreateOllamaOptions();
        options.Ollama.LlmModel = "another-model";
        options.Ollama.LlmDigest = new string('a', 64);
        options.Embedding.Model = "another-embedding";
        options.Embedding.Digest = new string('b', 64);

        var result = Validate(options, Environments.Development);

        Assert.False(result.Succeeded);
        Assert.Contains("Ollama LLM model and digest", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Embedding model and digest", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Qdrant_requires_loopback_key_and_locked_collection_score()
    {
        var options = CreateOllamaOptions();
        options.Qdrant.BaseUrl = "https://qdrant.example";
        options.Qdrant.ApiKey = string.Empty;
        options.Qdrant.CollectionName = "other_collection";
        options.Qdrant.MinScore = 0.1;

        var result = Validate(options, Environments.Development);

        Assert.False(result.Succeeded);
        Assert.Contains("Qdrant:BaseUrl", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Qdrant:ApiKey", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Qdrant:CollectionName", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Qdrant:MinScore", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void External_provider_requires_secret_model_and_structured_outputs()
    {
        var options = CreateExternalOptions();
        options.External.ApiKey = string.Empty;
        options.External.Model = string.Empty;
        options.External.SupportsStructuredOutputs = false;

        var result = Validate(options, Environments.Development);

        Assert.False(result.Succeeded);
        Assert.Contains("External:ApiKey", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("External:Model", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("SupportsStructuredOutputs", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void External_provider_rejects_unknown_structured_output_mode()
    {
        var options = CreateExternalOptions();
        options.External.StructuredOutputMode = "Xml";

        var result = Validate(options, Environments.Development);

        Assert.False(result.Succeeded);
        Assert.Contains("StructuredOutputMode", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Dependency_injection_selects_external_chat_and_keeps_embedding_local()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:Provider"] = AiProviderNames.External,
                ["Ai:External:BaseUrl"] = "http://127.0.0.1:19090/v1",
                ["Ai:External:ChatCompletionsPath"] = "/chat/completions",
                ["Ai:External:Model"] = "external-dev-model",
                ["Ai:External:ApiKey"] = "external-secret",
                ["Ai:External:SupportsStructuredOutputs"] = "true",
                ["Ai:Qdrant:ApiKey"] = "test-qdrant-key"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment { EnvironmentName = Environments.Development });
        services.AddDigitalOpsAi(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();

        Assert.IsType<ExternalAiChatClient>(
            scope.ServiceProvider.GetRequiredService<IAiChatClient>());
        Assert.IsType<OllamaEmbeddingClient>(
            scope.ServiceProvider.GetRequiredService<IEmbeddingClient>());
    }

    [Fact]
    public async Task External_client_sends_openai_compatible_structured_output_and_bearer_key()
    {
        var handler = new RecordingHandler(_ => CreateJsonResponse(
            "{\"choices\":[{\"message\":{\"content\":\"{\\\"answer\\\":\\\"ok\\\"}\"}}],\"usage\":{\"prompt_tokens\":12,\"completion_tokens\":4}}"));
        var client = new ExternalAiChatClient(
            new HttpClient(handler),
            Options.Create(CreateExternalOptions()),
            NullLogger<ExternalAiChatClient>.Instance);

        var result = await client.CompleteAsync(CreateChatRequest());

        Assert.Equal(AiProviderNames.External, result.Provider);
        Assert.Equal("external-dev-model", result.Model);
        Assert.Equal("{\"answer\":\"ok\"}", result.Content);
        Assert.Equal(12, result.PromptTokens);
        Assert.Equal(4, result.OutputTokens);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("external-secret", handler.AuthorizationParameter);
        var body = handler.LastBody;
        Assert.Contains("\"response_format\"", body, StringComparison.Ordinal);
        Assert.Contains("\"json_schema\"", body, StringComparison.Ordinal);
        Assert.Contains("\"model\":\"external-dev-model\"", body, StringComparison.Ordinal);
        Assert.Contains("\"max_tokens\":256", body, StringComparison.Ordinal);
        Assert.DoesNotContain("external-secret", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task External_client_sends_json_object_mode_with_schema_instruction()
    {
        var handler = new RecordingHandler(_ => CreateJsonResponse(
            "{\"choices\":[{\"message\":{\"content\":\"{\\\"answer\\\":\\\"ok\\\"}\"}}]}"));
        var options = CreateExternalOptions();
        options.External.StructuredOutputMode = ExternalStructuredOutputModes.JsonObject;
        options.External.DisableThinking = true;
        var client = new ExternalAiChatClient(
            new HttpClient(handler),
            Options.Create(options),
            NullLogger<ExternalAiChatClient>.Instance);

        await client.CompleteAsync(CreateChatRequest());

        var body = handler.LastBody;
        Assert.Contains("\"type\":\"json_object\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("json_schema", body, StringComparison.Ordinal);
        Assert.Contains("Return exactly one JSON object", body, StringComparison.Ordinal);
        Assert.Contains("test_schema", body, StringComparison.Ordinal);
        Assert.Contains("additionalProperties", body, StringComparison.Ordinal);
        Assert.Contains("\"thinking\":{\"type\":\"disabled\"}", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ollama_client_sends_locked_context_and_schema_format()
    {
        var handler = new RecordingHandler(_ => CreateJsonResponse(
            "{\"message\":{\"content\":\"{\\\"answer\\\":\\\"ok\\\"}\"},\"prompt_eval_count\":8,\"eval_count\":3}"));
        var client = new OllamaAiChatClient(
            new HttpClient(handler),
            Options.Create(CreateOllamaOptions()),
            NullLogger<OllamaAiChatClient>.Instance);

        var result = await client.CompleteAsync(CreateChatRequest());

        Assert.Equal(AiProviderNames.Ollama, result.Provider);
        Assert.Equal("qwen3:4b-instruct-2507-q4_K_M", result.Model);
        Assert.Equal("{\"answer\":\"ok\"}", result.Content);
        Assert.Equal(new Uri("http://127.0.0.1:11434/api/chat"), handler.LastRequestUri);
        var body = handler.LastBody;
        Assert.Contains("\"num_ctx\":8192", body, StringComparison.Ordinal);
        Assert.Contains("\"num_predict\":256", body, StringComparison.Ordinal);
        Assert.Contains("\"format\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Embedding_client_always_uses_local_ollama_embedding_endpoint()
    {
        var handler = new RecordingHandler(_ => CreateJsonResponse(
            "{\"embeddings\":[[0.1,0.2,0.3]]}"));
        var client = new OllamaEmbeddingClient(
            new HttpClient(handler),
            Options.Create(new AiProviderOptions
            {
                Qdrant = new QdrantAiOptions { ApiKey = "test-qdrant-key" },
                Embedding = new EmbeddingAiOptions
                {
                    Dimensions = 3
                }
            }),
            NullLogger<OllamaEmbeddingClient>.Instance);

        var result = await client.EmbedAsync(["hello"]);

        Assert.Equal(AiProviderNames.Ollama, client.Provider);
        Assert.Equal("qwen3-embedding:0.6b", client.Model);
        var embedding = Assert.Single(result);
        Assert.Equal([0.1f, 0.2f, 0.3f], embedding);
        Assert.Equal(new Uri("http://127.0.0.1:11434/api/embed"), handler.LastRequestUri);
    }

    [Fact]
    public async Task Embedding_client_rejects_wrong_vector_dimension()
    {
        var handler = new RecordingHandler(_ => CreateJsonResponse(
            "{\"embeddings\":[[0.1,0.2]]}"));
        var client = new OllamaEmbeddingClient(
            new HttpClient(handler),
            Options.Create(CreateOllamaOptions()),
            NullLogger<OllamaEmbeddingClient>.Instance);

        var exception = await Assert.ThrowsAsync<AiProviderException>(() =>
            client.EmbedAsync(["hello"]));

        Assert.Contains("dimension is not 1024", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Qdrant_client_uses_locked_collection_sync_filters_and_score()
    {
        var staffId = Guid.NewGuid();
        var handler = new RecordingHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith(
                    "/collections/digitalops_knowledge_v1",
                    StringComparison.Ordinal))
            {
                return CreateJsonResponse(
                    "{\"result\":{\"config\":{\"params\":{\"vectors\":{\"size\":1024,\"distance\":\"Cosine\"}}}}}");
            }

            if (path.EndsWith("/points/scroll", StringComparison.Ordinal))
            {
                return CreateJsonResponse(
                    $"{{\"result\":{{\"points\":[{{\"id\":\"{staffId:D}\",\"payload\":{{\"sourceId\":\"{staffId:D}\",\"contentHash\":\"hash-1\"}}}}],\"next_page_offset\":null}}}}");
            }

            if (path.EndsWith("/points/query", StringComparison.Ordinal))
            {
                return CreateJsonResponse(
                    $"{{\"result\":{{\"points\":[{{\"id\":\"{staffId:D}\",\"score\":0.91,\"payload\":{{\"sourceId\":\"{staffId:D}\",\"contentHash\":\"hash-1\",\"content\":\"Staff content\"}}}}]}}}}");
            }

            return CreateJsonResponse("{\"result\":{\"status\":\"acknowledged\"}}");
        });
        var client = new QdrantKnowledgeClient(
            new HttpClient(handler),
            Options.Create(CreateOllamaOptions()),
            NullLogger<QdrantKnowledgeClient>.Instance);

        await client.EnsureCollectionAsync();
        var hashes = await client.GetStaffContentHashesAsync();
        await client.UpsertStaffPointsAsync([
            new StaffKnowledgePoint(
                staffId,
                "staff-v1:hash-1",
                $"staff:{staffId:D}:1",
                "hash-1",
                "Staff content",
                new float[1024],
                DateTime.UtcNow)
        ]);
        await client.DeleteStaffPointsAsync([staffId]);
        var candidates = await client.SearchStaffAsync(new float[1024]);

        Assert.Equal("hash-1", hashes[staffId]);
        Assert.Equal(staffId, Assert.Single(candidates).StaffId);
        Assert.All(handler.QdrantApiKeys, key => Assert.Equal("test-qdrant-key", key));
        Assert.Contains(handler.Bodies, body =>
            body.Contains("\"sourceType\"", StringComparison.Ordinal)
            && body.Contains("\"isActive\"", StringComparison.Ordinal)
            && body.Contains("\"accessScope\"", StringComparison.Ordinal)
            && body.Contains("\"score_threshold\":0.316666", StringComparison.Ordinal)
            && body.Contains("\"limit\":5", StringComparison.Ordinal));
        Assert.Contains(handler.Bodies, body =>
            body.Contains("\"contentHash\":\"hash-1\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Qdrant_client_isolates_template_sync_and_filters_exact_template()
    {
        var pointId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var handler = new RecordingHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/points/scroll", StringComparison.Ordinal))
            {
                return CreateJsonResponse(
                    $"{{\"result\":{{\"points\":[{{\"id\":\"{pointId:D}\",\"payload\":{{\"sourceId\":\"{templateId:D}\",\"sourceVersion\":\"template-v1\",\"chunkId\":\"template:1\",\"contentHash\":\"template-hash\"}}}}],\"next_page_offset\":null}}}}");
            }

            if (path.EndsWith("/points/query", StringComparison.Ordinal))
            {
                return CreateJsonResponse(
                    $"{{\"result\":{{\"points\":[{{\"id\":\"{pointId:D}\",\"score\":0.92,\"payload\":{{\"sourceId\":\"{templateId:D}\",\"documentTypeCode\":\"PLAN\",\"sourceVersion\":\"template-v1\",\"chunkId\":\"template:1\",\"contentHash\":\"template-hash\",\"content\":\"Template content\"}}}}]}}}}");
            }

            return CreateJsonResponse("{\"result\":{\"status\":\"acknowledged\"}}");
        });
        var client = new QdrantKnowledgeClient(
            new HttpClient(handler),
            Options.Create(CreateOllamaOptions()),
            NullLogger<QdrantKnowledgeClient>.Instance);

        var states = await client.GetTemplateStatesAsync();
        await client.UpsertTemplatePointsAsync([
            new TemplateKnowledgePoint(
                pointId,
                templateId,
                "PLAN",
                "template-v1",
                "template:1",
                "template-hash",
                "Template content",
                new float[1024],
                DateTime.UtcNow)
        ]);
        await client.DeleteTemplatePointsAsync([pointId]);
        var candidates = await client.SearchTemplateAsync(
            new float[1024],
            templateId,
            "PLAN");

        Assert.Equal(pointId, Assert.Single(states).PointId);
        Assert.Equal(templateId, Assert.Single(candidates).TemplateId);
        Assert.Contains(handler.Bodies, body =>
            body.Contains("\"sourceType\":\"Template\"", StringComparison.Ordinal)
            && body.Contains($"\"sourceId\":\"{templateId:D}\"", StringComparison.Ordinal)
            && body.Contains("\"documentTypeCode\":\"PLAN\"", StringComparison.Ordinal));
        Assert.Contains(handler.Bodies, body =>
            body.Contains("\"score_threshold\":0.316666", StringComparison.Ordinal)
            && body.Contains("\"accessScope\"", StringComparison.Ordinal));
        Assert.Contains(handler.Bodies, body =>
            body.Contains($"\"points\":[\"{pointId:D}\"]", StringComparison.Ordinal));
    }

    private static AiProviderOptions CreateExternalOptions() => new()
    {
        Provider = AiProviderNames.External,
        External = new ExternalAiOptions
        {
            BaseUrl = "http://127.0.0.1:19090/v1",
            ChatCompletionsPath = "/chat/completions",
            Model = "external-dev-model",
            ApiKey = "external-secret",
            SupportsStructuredOutputs = true,
            StructuredOutputMode = ExternalStructuredOutputModes.JsonSchema
        },
        Qdrant = new QdrantAiOptions { ApiKey = "test-qdrant-key" }
    };

    private static AiProviderOptions CreateOllamaOptions() => new()
    {
        Qdrant = new QdrantAiOptions { ApiKey = "test-qdrant-key" }
    };

    private static AiChatRequest CreateChatRequest()
    {
        using var document = JsonDocument.Parse(
            "{\"type\":\"object\",\"properties\":{\"answer\":{\"type\":\"string\"}},\"required\":[\"answer\"],\"additionalProperties\":false}");
        return new AiChatRequest(
            AiOperationKind.Assignment,
            [new AiChatMessage("user", "hello")],
            new AiJsonSchema("test_schema", document.RootElement.Clone()));
    }

    private static ValidateOptionsResult Validate(
        AiProviderOptions options,
        string environmentName)
    {
        return new AiProviderOptionsValidator(
            new TestHostEnvironment { EnvironmentName = environmentName })
            .Validate(Options.DefaultName, options);
    }

    private static HttpResponseMessage CreateJsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        public string? AuthorizationScheme { get; private set; }

        public string? AuthorizationParameter { get; private set; }

        public string LastBody { get; private set; } = string.Empty;

        public List<string> Bodies { get; } = [];

        public List<string?> QdrantApiKeys { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            LastBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Bodies.Add(LastBody);
            QdrantApiKeys.Add(
                request.Headers.TryGetValues("api-key", out var values)
                    ? values.SingleOrDefault()
                    : null);
            return responseFactory(request);
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "DigitalOps.API.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
