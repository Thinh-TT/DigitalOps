using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DigitalOps.API.Shared.AI.Retrieval;
using DigitalOps.API.Shared.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalOps.API.Tests;

public sealed class CitationSnapshotServiceTests
{
    private static async Task<(SqliteConnection Connection, DigitalOpsDbContext DbContext)> CreateSqliteDbContextAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<DigitalOpsDbContext>()
            .UseSqlite(connection)
            .ReplaceService<IModelCustomizer, AuthenticationTestModelCustomizer>()
            .Options;

        var dbContext = new DigitalOpsDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        return (connection, dbContext);
    }

    [Fact]
    public async Task SaveCitationSnapshotAsync_PersistsCitationSnapshotSuccessfully()
    {
        // Arrange
        var (connection, dbContext) = await CreateSqliteDbContextAsync();
        await using var conn = connection;
        await using var db = dbContext;

        var logger = NullLogger<CitationSnapshotService>.Instance;
        var service = new CitationSnapshotService(db, logger);

        var businessId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();
        var retrievedChunkIds = new List<Guid> { chunkId };
        var citationPayload = new
        {
            Title = "Nghị định 15/2023/NĐ-CP",
            Snippet = "Trích dẫn điều 1 khoản 2..."
        };

        // Act
        var snapshotId = await service.SaveCitationSnapshotAsync(
            businessEntityType: "AiDraftContent",
            businessEntityId: businessId,
            queryText: "Tra cứu quy định Nghị định 15",
            retrievedChunkIds: retrievedChunkIds,
            citationPayload: citationPayload
        );

        // Assert
        Assert.NotEqual(Guid.Empty, snapshotId);

        var persistedSnapshot = await db.RagCitationSnapshots.FirstOrDefaultAsync(s => s.Id == snapshotId);
        Assert.NotNull(persistedSnapshot);
        Assert.Equal("AiDraftContent", persistedSnapshot.BusinessEntityType);
        Assert.Equal(businessId, persistedSnapshot.BusinessEntityId);
        Assert.Equal("Tra cứu quy định Nghị định 15", persistedSnapshot.QueryText);
        Assert.Contains(chunkId, persistedSnapshot.RetrievedChunkIds);
    }
}
