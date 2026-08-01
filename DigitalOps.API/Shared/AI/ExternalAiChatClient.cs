using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace DigitalOps.API.Shared.AI;

public sealed class ExternalAiChatClient(
    HttpClient httpClient,
    IOptions<AiProviderOptions> options,
    ILogger<ExternalAiChatClient> logger) : IAiChatClient
{
    private readonly AiProviderOptions _options = options.Value;

    public string Provider => AiProviderNames.External;

    public string Model => _options.External.Model;

    public async Task<AiChatResult> CompleteAsync(
        AiChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestSettings = AiRequestSettings.Resolve(request.Operation, _options);
        var payload = new
        {
            model = _options.External.Model,
            messages = request.Messages,
            temperature = requestSettings.Temperature,
            max_tokens = requestSettings.MaxOutputTokens,
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = request.Schema.Name,
                    strict = true,
                    schema = request.Schema.Schema
                }
            }
        };

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri(
                _options.External.BaseUrl,
                _options.External.ChatCompletionsPath))
        {
            Content = JsonContent.Create(payload)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _options.External.ApiKey);
        using var timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                httpRequest,
                timeoutCancellation.Token);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiProviderException("External AI request timed out.", null, exception);
        }
        catch (HttpRequestException exception)
        {
            throw new AiProviderException("External AI request failed.", null, exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new AiProviderException(
                    $"External AI request failed with HTTP {(int)response.StatusCode}.",
                    (int)response.StatusCode);
            }

            var body = await response.Content.ReadFromJsonAsync<ExternalChatResponse>(
                cancellationToken);
            var content = body?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new AiProviderException("External AI returned no chat content.");
            }

            var result = new AiChatResult(
                content,
                Provider,
                Model,
                body?.Usage?.PromptTokens,
                body?.Usage?.CompletionTokens);
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
    }

    private static Uri BuildUri(string baseUrl, string path) =>
        new($"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}");

    private sealed record ExternalChatResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<ExternalChoice>? Choices,
        [property: JsonPropertyName("usage")] ExternalUsage? Usage);

    private sealed record ExternalChoice(
        [property: JsonPropertyName("message")] ExternalMessage? Message);

    private sealed record ExternalMessage(
        [property: JsonPropertyName("content")] string? Content);

    private sealed record ExternalUsage(
        [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int? CompletionTokens);
}
