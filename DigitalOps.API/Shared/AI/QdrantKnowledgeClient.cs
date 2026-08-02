using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace DigitalOps.API.Shared.AI;

public sealed class QdrantKnowledgeClient(
    HttpClient httpClient,
    IOptions<AiProviderOptions> options,
    ILogger<QdrantKnowledgeClient> logger) : IQdrantKnowledgeClient
{
    private const int VectorDimensions = 1024;
    private const int SearchLimit = 5;
    private readonly QdrantAiOptions _options = options.Value.Qdrant;

    public async Task EnsureCollectionAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            CollectionPath(),
            payload: null,
            cancellationToken,
            allowNotFound: true);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            using var createResponse = await SendAsync(
                HttpMethod.Put,
                CollectionPath(),
                new
                {
                    vectors = new
                    {
                        size = VectorDimensions,
                        distance = "Cosine"
                    }
                },
                cancellationToken,
                allowConflict: true);

            if (createResponse.StatusCode == HttpStatusCode.Conflict)
            {
                using var concurrentResponse = await SendAsync(
                    HttpMethod.Get,
                    CollectionPath(),
                    payload: null,
                    cancellationToken);
                await ValidateCollectionAsync(concurrentResponse, cancellationToken);
                return;
            }

            return;
        }

        await ValidateCollectionAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetStaffContentHashesAsync(
        CancellationToken cancellationToken = default)
    {
        var hashes = new Dictionary<Guid, string>();
        JsonElement? offset = null;

        do
        {
            var payload = new Dictionary<string, object?>
            {
                ["filter"] = new
                {
                    must = new[]
                    {
                        new
                        {
                            key = "sourceType",
                            match = new { value = "Staff" }
                        }
                    }
                },
                ["limit"] = 100,
                ["with_payload"] = true,
                ["with_vector"] = false
            };
            if (offset is not null)
            {
                payload["offset"] = offset.Value;
            }

            using var response = await SendAsync(
                HttpMethod.Post,
                $"{CollectionPath()}/points/scroll",
                payload,
                cancellationToken);
            using var document = await ReadJsonAsync(response, cancellationToken);
            var result = GetRequiredProperty(document.RootElement, "result");
            foreach (var point in GetRequiredProperty(result, "points").EnumerateArray())
            {
                var pointPayload = GetRequiredProperty(point, "payload");
                if (TryReadGuid(pointPayload, "sourceId", out var staffId)
                    && TryReadString(pointPayload, "contentHash", out var contentHash))
                {
                    hashes[staffId] = contentHash;
                }
            }

            offset = result.TryGetProperty("next_page_offset", out var nextOffset)
                && nextOffset.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                    ? nextOffset.Clone()
                    : null;
        }
        while (offset is not null);

        return hashes;
    }

    public async Task UpsertStaffPointsAsync(
        IReadOnlyList<StaffKnowledgePoint> points,
        CancellationToken cancellationToken = default)
    {
        if (points.Count == 0)
        {
            return;
        }

        using var response = await SendAsync(
            HttpMethod.Put,
            $"{CollectionPath()}/points?wait=true",
            new
            {
                points = points.Select(point => new
                {
                    id = point.StaffId,
                    vector = point.Vector,
                    payload = new
                    {
                        sourceType = "Staff",
                        sourceId = point.StaffId,
                        sourceVersion = point.SourceVersion,
                        chunkId = point.ChunkId,
                        contentHash = point.ContentHash,
                        content = point.Content,
                        isActive = true,
                        accessScope = "Internal",
                        indexedAtUtc = point.IndexedAtUtc
                    }
                })
            },
            cancellationToken);

        logger.LogInformation(
            "Qdrant Staff knowledge synchronized: {UpsertedCount} points upserted",
            points.Count);
    }

    public async Task DeleteStaffPointsAsync(
        IReadOnlyList<Guid> staffIds,
        CancellationToken cancellationToken = default)
    {
        if (staffIds.Count == 0)
        {
            return;
        }

        using var response = await SendAsync(
            HttpMethod.Post,
            $"{CollectionPath()}/points/delete?wait=true",
            new { points = staffIds },
            cancellationToken);

        logger.LogInformation(
            "Qdrant Staff knowledge synchronized: {DeletedCount} stale points deleted",
            staffIds.Count);
    }

    public async Task<IReadOnlyList<StaffKnowledgeCandidate>> SearchStaffAsync(
        float[] queryVector,
        CancellationToken cancellationToken = default)
    {
        if (queryVector.Length != VectorDimensions)
        {
            throw new AiProviderException(
                $"Qdrant query vector dimension must be {VectorDimensions}.");
        }

        using var response = await SendAsync(
            HttpMethod.Post,
            $"{CollectionPath()}/points/query",
            new
            {
                query = queryVector,
                filter = new
                {
                    must = new object[]
                    {
                        new
                        {
                            key = "sourceType",
                            match = new { value = "Staff" }
                        },
                        new
                        {
                            key = "isActive",
                            match = new { value = true }
                        },
                        new
                        {
                            key = "accessScope",
                            match = new { value = "Internal" }
                        }
                    }
                },
                score_threshold = _options.MinScore,
                limit = SearchLimit,
                with_payload = true,
                with_vector = false
            },
            cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);
        var result = GetRequiredProperty(document.RootElement, "result");
        var points = GetRequiredProperty(result, "points");
        var candidates = new List<StaffKnowledgeCandidate>();

        foreach (var point in points.EnumerateArray())
        {
            var payload = GetRequiredProperty(point, "payload");
            if (!TryReadGuid(payload, "sourceId", out var staffId)
                || !TryReadString(payload, "contentHash", out var contentHash)
                || !TryReadString(payload, "content", out var content)
                || !point.TryGetProperty("score", out var scoreElement)
                || !scoreElement.TryGetDouble(out var score))
            {
                throw new AiProviderException("Qdrant returned an invalid Staff point.");
            }

            candidates.Add(new StaffKnowledgeCandidate(
                staffId,
                contentHash,
                content,
                score));
        }

        return candidates;
    }

    private async Task ValidateCollectionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var document = await ReadJsonAsync(response, cancellationToken);
        try
        {
            var vectors = document.RootElement
                .GetProperty("result")
                .GetProperty("config")
                .GetProperty("params")
                .GetProperty("vectors");
            var size = vectors.GetProperty("size").GetInt32();
            var distance = vectors.GetProperty("distance").GetString();
            if (size != VectorDimensions
                || !string.Equals(distance, "Cosine", StringComparison.OrdinalIgnoreCase))
            {
                throw new AiProviderException(
                    "Qdrant collection is incompatible with the approved vector contract.");
            }
        }
        catch (KeyNotFoundException exception)
        {
            throw new AiProviderException(
                "Qdrant returned an invalid collection description.",
                innerException: exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new AiProviderException(
                "Qdrant returned an invalid collection description.",
                innerException: exception);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? payload,
        CancellationToken cancellationToken,
        bool allowNotFound = false,
        bool allowConflict = false)
    {
        using var request = new HttpRequestMessage(
            method,
            BuildUri(_options.BaseUrl, path));
        request.Headers.Add("api-key", _options.ApiKey);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload);
        }

        try
        {
            var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode
                || allowNotFound && response.StatusCode == HttpStatusCode.NotFound
                || allowConflict && response.StatusCode == HttpStatusCode.Conflict)
            {
                return response;
            }

            var statusCode = (int)response.StatusCode;
            response.Dispose();
            throw new AiProviderException(
                $"Qdrant request failed with HTTP {statusCode}.",
                statusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiProviderException("Qdrant request timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new AiProviderException(
                "Qdrant request failed.",
                innerException: exception);
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new AiProviderException(
                "Qdrant returned invalid JSON.",
                innerException: exception);
        }
    }

    private static JsonElement GetRequiredProperty(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            throw new AiProviderException(
                $"Qdrant response is missing required property '{name}'.");
        }

        return value;
    }

    private static bool TryReadGuid(
        JsonElement element,
        string name,
        out Guid value)
    {
        value = default;
        return element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String
            && Guid.TryParse(property.GetString(), out value);
    }

    private static bool TryReadString(
        JsonElement element,
        string name,
        out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return value.Length > 0;
    }

    private string CollectionPath() =>
        $"/collections/{Uri.EscapeDataString(_options.CollectionName)}";

    private static Uri BuildUri(string baseUrl, string path) =>
        new($"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}");
}
