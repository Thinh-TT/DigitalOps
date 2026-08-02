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

    Task<OutgoingDocumentResult<OutgoingDocumentResponse>> UpdateAsync(
        Guid id,
        OutgoingDocumentUpdateRequest request,
        Guid callerStaffId,
        CancellationToken cancellationToken = default);

    Task<OutgoingDocumentResult<OutgoingDocumentResponse>> GenerateAiDraftAsync(
        Guid id,
        AiDraftRequest request,
        Guid callerStaffId,
        CancellationToken cancellationToken = default);
}
