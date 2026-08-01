using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace DigitalOps.API.Shared.AI;

public sealed class OllamaAiChatClient(
    HttpClient httpClient,
    IOptions<AiProviderOptions> options,
    ILogger<OllamaAiChatClient> logger) : IAiChatClient
{
    private readonly AiProviderOptions _options = options.Value;

    public string Provider => AiProviderNames.Ollama;

    public string Model => _options.Ollama.LlmModel;

    public async Task<AiChatResult> CompleteAsync(
        AiChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestSettings = AiRequestSettings.Resolve(request.Operation, _options);
        var payload = new
        {
            model = _options.Ollama.LlmModel,
            messages = request.Messages,
            stream = false,
            think = false,
            format = request.Schema.Schema,
            keep_alive = "15m",
            options = new
            {
                num_ctx = _options.ContextTokens,
                num_predict = requestSettings.MaxOutputTokens,
                temperature = requestSettings.Temperature
            }
        };

        using var response = await SendAsync(
            HttpMethod.Post,
            "/api/chat",
            payload,
            cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
            cancellationToken);

        if (body?.Message?.Content is not { Length: > 0 } content)
        {
            throw new AiProviderException("Ollama returned no chat content.");
        }

        var result = new AiChatResult(
            content,
            Provider,
            Model,
            body.PromptTokens,
            body.OutputTokens);
        logger.LogInformation(
            "AI chat completed: {Provider}/{Model}, operation {Operation}, {ElapsedMilliseconds} ms, prompt tokens {PromptTokens}, output tokens {OutputTokens}",
            Provider,
            Model,
            request.Operation,
            stopwatch.ElapsedMilliseconds,
            result.PromptTokens,
            result.OutputTokens);
        return result;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            method,
            BuildUri(_options.Ollama.BaseUrl, path))
        {
            Content = JsonContent.Create(payload)
        };
        using var timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            var response = await httpClient.SendAsync(
                request,
                timeoutCancellation.Token);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var statusCode = (int)response.StatusCode;
            response.Dispose();
            throw new AiProviderException(
                $"Ollama request failed with HTTP {statusCode}.",
                statusCode);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiProviderException("Ollama request timed out.", null, exception);
        }
        catch (HttpRequestException exception)
        {
            throw new AiProviderException("Ollama request failed.", null, exception);
        }
    }

    private static Uri BuildUri(string baseUrl, string path) =>
        new($"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}");

    private sealed record OllamaChatResponse(
        [property: JsonPropertyName("message")] OllamaMessage? Message,
        [property: JsonPropertyName("prompt_eval_count")] int? PromptTokens,
        [property: JsonPropertyName("eval_count")] int? OutputTokens);

    private sealed record OllamaMessage(
        [property: JsonPropertyName("content")] string? Content);
}

public sealed class OllamaEmbeddingClient(
    HttpClient httpClient,
    IOptions<AiProviderOptions> options,
    ILogger<OllamaEmbeddingClient> logger) : IEmbeddingClient
{
    private readonly AiProviderOptions _options = options.Value;

    public string Provider => AiProviderNames.Ollama;

    public string Model => _options.Embedding.Model;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        if (inputs.Count == 0)
        {
            return Array.Empty<float[]>();
        }

        var payload = new
        {
            model = _options.Embedding.Model,
            input = inputs,
            dimensions = _options.Embedding.Dimensions,
            keep_alive = "15m"
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri(_options.Ollama.BaseUrl, "/api/embed"))
        {
            Content = JsonContent.Create(payload)
        };
        using var timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                request,
                timeoutCancellation.Token);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiProviderException("Ollama embedding request timed out.", null, exception);
        }
        catch (HttpRequestException exception)
        {
            throw new AiProviderException("Ollama embedding request failed.", null, exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new AiProviderException(
                    $"Ollama embedding request failed with HTTP {(int)response.StatusCode}.",
                    (int)response.StatusCode);
            }

            var body = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(
                cancellationToken);
            var embeddings = body?.Embeddings?
                .Select(embedding => embedding.ToArray())
                .ToArray();

            if (embeddings is null || embeddings.Length != inputs.Count)
            {
                throw new AiProviderException("Ollama returned an unexpected embedding count.");
            }

            if (embeddings.Any(
                    embedding => embedding.Length != _options.Embedding.Dimensions))
            {
                throw new AiProviderException(
                    $"Ollama returned an embedding whose dimension is not {_options.Embedding.Dimensions}.");
            }

            logger.LogInformation(
                "AI embedding completed: {Provider}/{Model}, {InputCount} inputs, {Dimensions} dimensions, {ElapsedMilliseconds} ms",
                Provider,
                Model,
                inputs.Count,
                _options.Embedding.Dimensions,
                stopwatch.ElapsedMilliseconds);
            return embeddings;
        }
    }

    private static Uri BuildUri(string baseUrl, string path) =>
        new($"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}");

    private sealed record OllamaEmbeddingResponse(
        [property: JsonPropertyName("embeddings")] IReadOnlyList<float[]>? Embeddings);
}
