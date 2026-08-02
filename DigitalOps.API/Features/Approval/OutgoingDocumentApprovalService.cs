using DigitalOps.API.Features.OutgoingDocuments;
using DigitalOps.API.Features.Review;
using DigitalOps.API.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace DigitalOps.API.Features.Approval;

public sealed class OutgoingDocumentApprovalService(
    DigitalOpsDbContext dbContext,
    IOutgoingDocumentService outgoingDocumentService,
    TimeProvider timeProvider) : IOutgoingDocumentApprovalService
{
    public async Task<ApprovalOperationResult<OutgoingDocumentResponse>> DecideAsync(
        Guid outgoingDocumentId,
        ApprovalDecisionRequest request,
        Guid leaderStaffId,
        CancellationToken cancellationToken = default)
    {
        if (!request.HasDecision
            || request.Decision is not (ApprovalDecision.Approve or ApprovalDecision.Return))
        {
            return ApprovalOperationResult<OutgoingDocumentResponse>.Validation(
                SingleError("decision", "Quyết định phê duyệt phải là Approve hoặc Return."));
        }

        var snapshot = await GetSnapshotAsync(outgoingDocumentId, cancellationToken);
        var precondition = ValidatePreconditions(snapshot);
        if (precondition is not null)
        {
            return precondition;
        }

        var decidedAt = timeProvider.GetUtcNow().UtcDateTime;
        var update = dbContext.OutgoingDocuments.Where(document =>
            document.Id == outgoingDocumentId
            && document.Status == OutgoingDocumentStatus.PendingApproval
            && document.UpdatedAt == snapshot!.UpdatedAt);
        var affected = request.Decision == ApprovalDecision.Approve
            ? await update.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(document => document.Status, OutgoingDocumentStatus.Approved)
                    .SetProperty(document => document.ApprovedByStaffId, leaderStaffId)
                    .SetProperty(document => document.ApprovedAt, decidedAt)
                    .SetProperty(document => document.UpdatedAt, decidedAt),
                cancellationToken)
            : await update.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(document => document.Status, OutgoingDocumentStatus.Editing)
                    .SetProperty(document => document.ApprovedByStaffId, (Guid?)null)
                    .SetProperty(document => document.ApprovedAt, (DateTime?)null)
                    .SetProperty(document => document.UpdatedAt, decidedAt),
                cancellationToken);

        if (affected != 1)
        {
            return await ClassifyUpdateFailureAsync(outgoingDocumentId, cancellationToken);
        }

        var response = await outgoingDocumentService.GetByIdAsync(
            outgoingDocumentId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The outgoing document disappeared after an approval update.");
        return ApprovalOperationResult<OutgoingDocumentResponse>.Success(response);
    }

    private async Task<ApprovalOperationResult<OutgoingDocumentResponse>> ClassifyUpdateFailureAsync(
        Guid outgoingDocumentId,
        CancellationToken cancellationToken)
    {
        var current = await GetSnapshotAsync(outgoingDocumentId, cancellationToken);
        var precondition = ValidatePreconditions(current);
        return precondition
            ?? ApprovalOperationResult<OutgoingDocumentResponse>.Conflict(
                "Văn bản đã thay đổi trong khi xử lý quyết định phê duyệt. Vui lòng tải lại hàng chờ.");
    }

    private Task<ApprovalSnapshot?> GetSnapshotAsync(
        Guid outgoingDocumentId,
        CancellationToken cancellationToken) =>
        dbContext.OutgoingDocuments
            .AsNoTracking()
            .Where(document => document.Id == outgoingDocumentId)
            .Select(document => new ApprovalSnapshot(
                document.Status,
                document.UpdatedAt,
                document.ReviewHistory
                    .OrderByDescending(review => review.AttemptNo)
                    .Select(review => (ReviewResult?)review.ReviewResult)
                    .FirstOrDefault()))
            .SingleOrDefaultAsync(cancellationToken);

    private static ApprovalOperationResult<OutgoingDocumentResponse>? ValidatePreconditions(
        ApprovalSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return ApprovalOperationResult<OutgoingDocumentResponse>.NotFound();
        }

        if (snapshot.Status != OutgoingDocumentStatus.PendingApproval)
        {
            return ApprovalOperationResult<OutgoingDocumentResponse>.Conflict(
                "Chỉ văn bản đang chờ duyệt mới có thể được phê duyệt hoặc trả lại.");
        }

        if (snapshot.LatestReviewResult != ReviewResult.Passed)
        {
            return ApprovalOperationResult<OutgoingDocumentResponse>.Conflict(
                "Văn bản chỉ được trình duyệt khi lần thẩm định mới nhất đạt yêu cầu.");
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string[]> SingleError(
        string field,
        string error) =>
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [field] = [error]
        };

    private sealed record ApprovalSnapshot(
        OutgoingDocumentStatus Status,
        DateTime UpdatedAt,
        ReviewResult? LatestReviewResult);
}
