using Microsoft.Extensions.Options;

namespace DigitalOps.API.Features.Drafting;

public sealed class DocumentCatalogSeedHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<DocumentCatalogSeedOptions> seedOptions,
    ILogger<DocumentCatalogSeedHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!seedOptions.Value.Enabled)
        {
            logger.LogDebug("Document catalog seed is disabled.");
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IDocumentCatalogSeeder>();
        await seeder.SeedAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
