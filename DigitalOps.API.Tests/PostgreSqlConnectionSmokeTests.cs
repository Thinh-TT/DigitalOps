using DigitalOps.API.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace DigitalOps.API.Tests;

public sealed class PostgreSqlConnectionSmokeTests
{
    [Fact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Can_connect_when_a_development_connection_string_is_provided()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DigitalOps");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<DigitalOpsDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var dbContext = new DigitalOpsDbContext(options);

        Assert.True(await dbContext.Database.CanConnectAsync());
    }
}
