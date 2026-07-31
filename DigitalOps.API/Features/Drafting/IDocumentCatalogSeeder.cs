namespace DigitalOps.API.Features.Drafting;

public interface IDocumentCatalogSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}
