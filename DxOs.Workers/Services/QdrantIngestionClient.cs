using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace DxOs.Workers.Services;

public sealed record QdrantIngestionPoint(
    Guid PointId,
    Guid ChunkId,
    Guid ChunkSetId,
    Guid VersionId,
    Guid DocumentId,
    string CanonicalDocumentKey,
    string SecurityClassification,
    IReadOnlyList<string> AllowedRoles,
    IReadOnlyList<string> DeniedRoles,
    float[] Vector);

public interface IQdrantIngestionClient
{
    Task EnsureCollectionAsync(
        string collectionName,
        uint dimensions,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        string collectionName,
        IReadOnlyList<QdrantIngestionPoint> points,
        CancellationToken cancellationToken = default);
}

public sealed class QdrantIngestionClient(
    QdrantClient client) : IQdrantIngestionClient
{
    public async Task EnsureCollectionAsync(
        string collectionName,
        uint dimensions,
        CancellationToken cancellationToken = default)
    {
        if (await client.CollectionExistsAsync(collectionName, cancellationToken))
        {
            var collection = await client.GetCollectionInfoAsync(
                collectionName,
                cancellationToken);
            var vectorParams = collection.Config.Params.VectorsConfig.Params;
            if (vectorParams is null
                || vectorParams.Size != dimensions
                || vectorParams.Distance != Distance.Cosine)
            {
                throw new InvalidOperationException(
                    $"Qdrant collection '{collectionName}' must use "
                    + $"{dimensions} dimensions and cosine distance.");
            }
            return;
        }

        await client.CreateCollectionAsync(
            collectionName,
            new VectorParams
            {
                Size = dimensions,
                Distance = Distance.Cosine
            },
            cancellationToken: cancellationToken);
    }

    public async Task UpsertAsync(
        string collectionName,
        IReadOnlyList<QdrantIngestionPoint> points,
        CancellationToken cancellationToken = default)
    {
        if (points.Count == 0)
        {
            return;
        }

        var qdrantPoints = points.Select(point =>
        {
            var value = new PointStruct
            {
                Id = point.PointId,
                Vectors = point.Vector
            };
            value.Payload["sourceType"] = "RagChunk";
            value.Payload["isActive"] = true;
            value.Payload["chunk_id"] = point.ChunkId.ToString("D");
            value.Payload["chunk_set_id"] = point.ChunkSetId.ToString("D");
            value.Payload["version_id"] = point.VersionId.ToString("D");
            value.Payload["document_id"] = point.DocumentId.ToString("D");
            value.Payload["canonical_document_key"] =
                point.CanonicalDocumentKey;
            value.Payload["security_classification"] =
                point.SecurityClassification;
            value.Payload["allowed_roles"] = new Value
            {
                ListValue = new ListValue()
            };
            value.Payload["allowed_roles"].ListValue.Values.AddRange(
                point.AllowedRoles.Select(role => new Value { StringValue = role }));
            value.Payload["denied_roles"] = new Value
            {
                ListValue = new ListValue()
            };
            value.Payload["denied_roles"].ListValue.Values.AddRange(
                point.DeniedRoles.Select(role => new Value { StringValue = role }));
            return value;
        }).ToArray();

        await client.UpsertAsync(
            collectionName,
            qdrantPoints,
            wait: true,
            cancellationToken: cancellationToken);
    }
}
