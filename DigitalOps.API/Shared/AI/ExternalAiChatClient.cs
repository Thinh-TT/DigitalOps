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
        var useJsonObjectMode = string.Equals(
            _options.External.StructuredOutputMode,
            ExternalStructuredOutputModes.JsonObject,
            StringComparison.OrdinalIgnoreCase);
        var messages = useJsonObjectMode
            ? BuildJsonObjectMessages(request)
            : request.Messages;
        object responseFormat = useJsonObjectMode
            ? new { type = "json_object" }
            : new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = request.Schema.Name,
                    strict = true,
                    schema = request.Schema.Schema
                }
            };
        var payload = new Dictionary<string, object?>
        {
            ["model"] = _options.External.Model,
            ["messages"] = messages,
            ["temperature"] = requestSettings.Temperature,
            ["max_tokens"] = requestSettings.MaxOutputTokens,
            ["response_format"] = responseFormat
        };

        if (_options.External.DisableThinking)
        {
            payload["thinking"] = new { type = "disabled" };
        }

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

    private static IReadOnlyList<AiChatMessage> BuildJsonObjectMessages(
        AiChatRequest request)
    {
        var instruction = $"Return exactly one JSON object for schema '{request.Schema.Name}'. "
            + "Do not include markdown, explanations, or any text outside the JSON object. "
            + $"The JSON object must match this schema: {request.Schema.Schema.GetRawText()}";

        return [new AiChatMessage("system", instruction), .. request.Messages];
    }

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
