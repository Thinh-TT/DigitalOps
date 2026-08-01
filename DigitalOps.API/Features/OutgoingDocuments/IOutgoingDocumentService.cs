using DigitalOps.API.Shared.Api;

namespace DigitalOps.API.Features.OutgoingDocuments;

public interface IOutgoingDocumentService
{
    Task<PagedResponse<OutgoingDocumentResponse>> GetListAsync(
        OutgoingDocumentListQuery query,
        CancellationToken cancellationToken = default);

    Task<OutgoingDocumentResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<OutgoingDocumentResult<OutgoingDocumentResponse>> CreateAsync(
        OutgoingDocumentCreateRequest request,
        Guid draftedByStaffId,
        CancellationToken cancellationToken = default);
}
