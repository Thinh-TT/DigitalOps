using DigitalOps.API.Features.OutgoingDocuments;

namespace DigitalOps.API.Features.Approval;

public interface IOutgoingDocumentApprovalService
{
    Task<ApprovalOperationResult<OutgoingDocumentResponse>> DecideAsync(
        Guid outgoingDocumentId,
        ApprovalDecisionRequest request,
        Guid leaderStaffId,
        CancellationToken cancellationToken = default);
}
