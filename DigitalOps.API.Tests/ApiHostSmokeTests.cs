using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalOps.API.Shared.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DigitalOps.API.Tests;

public sealed class ApiHostSmokeTests(DigitalOpsApiFactory factory) : IClassFixture<DigitalOpsApiFactory>
{
    [Fact]
    public void Host_registers_a_postgresql_db_context()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DigitalOpsDbContext>();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", dbContext.Database.ProviderName);
        Assert.NotNull(scope.ServiceProvider.GetService<IProblemDetailsService>());

        var jsonOptions = scope.ServiceProvider.GetRequiredService<IOptions<JsonOptions>>().Value;
        Assert.Same(JsonNamingPolicy.CamelCase, jsonOptions.JsonSerializerOptions.PropertyNamingPolicy);
        Assert.Contains(jsonOptions.JsonSerializerOptions.Converters, converter => converter is JsonStringEnumConverter);
    }
}

public sealed class DigitalOpsApiFactory : WebApplicationFactory<Program>
{
    private const string TestConnectionString =
        "Host=localhost;Port=5432;Database=digitalops_test;Username=test;Password=test";

    private readonly string? _previousConnectionString;

    public DigitalOpsApiFactory()
    {
        _previousConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DigitalOps");
        Environment.SetEnvironmentVariable("ConnectionStrings__DigitalOps", TestConnectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>(
                    "ConnectionStrings:DigitalOps",
                    "Host=localhost;Port=5432;Database=digitalops_test;Username=test;Password=test")
            ]);
        });
    }

    protected override void Dispose(bool disposing)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__DigitalOps", _previousConnectionString);
        base.Dispose(disposing);
    }
}
