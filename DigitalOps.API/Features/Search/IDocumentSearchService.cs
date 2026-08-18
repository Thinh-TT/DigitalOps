using DigitalOps.API.Shared.Api;

namespace DigitalOps.API.Features.Search;

public interface IDocumentSearchService
{
    Task<PagedResponse<DocumentSearchResult>> SearchAsync(
        DocumentSearchQuery query,
        CancellationToken cancellationToken = default);
}
