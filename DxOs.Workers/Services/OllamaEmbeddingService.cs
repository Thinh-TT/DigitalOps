using System;
using System.Net.Http;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Threading;

namespace DxOs.Workers.Services;

public sealed record OllamaEmbeddingRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("input")] string[] Input,
    [property: JsonPropertyName("dimensions")] int Dimensions
);

public sealed record OllamaEmbeddingResponse(
    [property: JsonPropertyName("embeddings")] float[][]? Embeddings
);

public interface IOllamaEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(
        string text,
        string model = "qwen3-embedding:0.6b",
        CancellationToken cancellationToken = default);
}

public sealed class OllamaEmbeddingService : IOllamaEmbeddingService
{
    private readonly HttpClient _httpClient;

    public OllamaEmbeddingService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri("http://localhost:11434");
        }
    }

    public async Task<float[]> GenerateEmbeddingAsync(
        string text,
        string model = "qwen3-embedding:0.6b",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException(
                "Embedding text cannot be empty.",
                nameof(text));
        }

        var request = new OllamaEmbeddingRequest(model, [text], 1024);
        using var response = await _httpClient.PostAsJsonAsync(
            "/api/embed",
            request,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(
            cancellationToken);
        var embedding = result?.Embeddings?.SingleOrDefault();
        if (embedding is null)
        {
            throw new InvalidOperationException(
                "Ollama returned an unexpected embedding count.");
        }
        if (embedding.Length != 1024
            || embedding.Any(value => !float.IsFinite(value)))
        {
            throw new InvalidOperationException(
                "Ollama embedding must contain 1024 finite dimensions.");
        }

        return embedding;
    }
}
