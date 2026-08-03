using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DigitalOps.API.Shared.Data;
using DigitalOps.API.Shared.Data.Entities;
using Microsoft.Extensions.Logging;

namespace DigitalOps.API.Shared.AI.Retrieval;

public interface ICitationSnapshotService
{
    Task<Guid> SaveCitationSnapshotAsync(
        string businessEntityType,
        Guid businessEntityId,
        string queryText,
        IReadOnlyList<Guid> retrievedChunkIds,
        object citationPayload,
        CancellationToken cancellationToken = default);
}

public sealed class CitationSnapshotService : ICitationSnapshotService
{
    private readonly DigitalOpsDbContext _dbContext;
    private readonly ILogger<CitationSnapshotService> _logger;

    public CitationSnapshotService(
        DigitalOpsDbContext dbContext,
        ILogger<CitationSnapshotService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<Guid> SaveCitationSnapshotAsync(
        string businessEntityType,
        Guid businessEntityId,
        string queryText,
        IReadOnlyList<Guid> retrievedChunkIds,
        object citationPayload,
        CancellationToken cancellationToken = default)
    {
        var snapshot = new RagCitationSnapshot
        {
            Id = Guid.NewGuid(),
            BusinessEntityType = businessEntityType,
            BusinessEntityId = businessEntityId,
            QueryText = queryText,
            RetrievedChunkIds = [.. retrievedChunkIds],
            CitationPayloadJson = JsonSerializer.Serialize(citationPayload),
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.RagCitationSnapshots.Add(snapshot);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Saved immutable citation snapshot {SnapshotId} for entity {EntityType}:{EntityId}",
            snapshot.Id, businessEntityType, businessEntityId);

        return snapshot.Id;
    }
}
