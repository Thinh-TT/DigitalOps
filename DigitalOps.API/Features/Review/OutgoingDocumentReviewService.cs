using System.Data;
using System.Text.Json;
using DigitalOps.API.Features.OutgoingDocuments;
using DigitalOps.API.Shared.AI;
using DigitalOps.API.Shared.AI.Retrieval;
using DigitalOps.API.Shared.Api;
using DigitalOps.API.Shared.Data;
using DigitalOps.API.Shared.Identity;
using Microsoft.EntityFrameworkCore;

namespace DigitalOps.API.Features.Review;

public sealed class OutgoingDocumentReviewService(
    DigitalOpsDbContext dbContext,
    IDocumentReviewGenerator reviewGenerator,
    ICitationSnapshotService citationSnapshotService,
    ILogger<OutgoingDocumentReviewService> logger)
    : IOutgoingDocumentReviewService
{
    public async Task<ReviewOperationResult<ReviewResponse>> CreateAsync(
        Guid outgoingDocumentId,
        Guid callerStaffId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await ReviewDocumentQuery(dbContext.OutgoingDocuments.AsNoTracking())
            .SingleOrDefaultAsync(document => document.Id == outgoingDocumentId, cancellationToken);
        var precondition = ValidateReviewPreconditions(snapshot, callerStaffId);
        if (precondition is not null)
        {
            return precondition;
        }

        var documentSnapshot = snapshot!;

        DocumentReviewGenerationResult generated;
        try
        {
            generated = await reviewGenerator.ReviewAsync(
                ToReviewInput(documentSnapshot),
                cancellationToken);
        }
        catch (AiProviderException exception)
        {
            logger.LogWarning(
                exception,
                "Document review failed before persistence for outgoing document {OutgoingDocumentId}",
                outgoingDocumentId);
            return ReviewOperationResult<ReviewResponse>.ServiceUnavailable(
                "Dịch vụ thẩm định hiện không khả dụng. Nội dung và lịch sử review đã lưu không bị thay đổi.");
        }

        var hasError = generated.Issues.Any(issue =>
            string.Equals(issue.Severity, "Error", StringComparison.Ordinal));
        if (generated.ReviewSource != ReviewSource.Rule && hasError)
        {
            logger.LogError(
                "Document review generator returned Error severity outside deterministic rules for outgoing document {OutgoingDocumentId}",
                outgoingDocumentId);
            return ReviewOperationResult<ReviewResponse>.ServiceUnavailable(
                "Dịch vụ thẩm định trả kết quả không hợp lệ. Nội dung và lịch sử review đã lưu không bị thay đổi.");
        }

        var finalStatus = hasError
            ? OutgoingDocumentStatus.ReviewFailed
            : OutgoingDocumentStatus.PendingApproval;
        var reviewResult = hasError ? ReviewResult.Failed : ReviewResult.Passed;
        var reviewIssues = JsonSerializer.SerializeToElement(
            generated.Issues,
            JsonSerializerOptions.Web);
        var citations = generated.Citations ?? [];

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        try
        {
            var pendingReviewAt = DateTime.UtcNow;
            var claimed = await dbContext.OutgoingDocuments
                .Where(document => document.Id == outgoingDocumentId
                    && document.DraftedByStaffId == callerStaffId
                    && document.Status == documentSnapshot.Status
                    && document.UpdatedAt == documentSnapshot.UpdatedAt
                    && document.Template.UpdatedAt == documentSnapshot.Template.UpdatedAt)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(document => document.Status, OutgoingDocumentStatus.PendingReview)
                        .SetProperty(document => document.UpdatedAt, pendingReviewAt),
                    cancellationToken);
            if (claimed == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return await ClassifyClaimFailureAsync(
                    outgoingDocumentId,
                    callerStaffId,
                    cancellationToken);
            }

            var lastAttempt = await dbContext.ReviewHistory
                .Where(review => review.OutgoingDocumentId == outgoingDocumentId)
                .Select(review => (int?)review.AttemptNo)
                .MaxAsync(cancellationToken) ?? 0;
            var reviewedAt = DateTime.UtcNow;
            var history = new ReviewHistory
            {
                Id = Guid.NewGuid(),
                OutgoingDocumentId = outgoingDocumentId,
                AttemptNo = lastAttempt + 1,
                ReviewSource = generated.ReviewSource,
                ReviewedByStaffId = callerStaffId,
                ContentSnapshot = documentSnapshot.Content,
                ReviewResult = reviewResult,
                ReviewIssues = reviewIssues.Clone(),
                ReviewedAt = reviewedAt
            };
            dbContext.ReviewHistory.Add(history);
            if (citations.Count > 0)
            {
                await citationSnapshotService.SaveCitationSnapshotAsync(
                    "ReviewHistory",
                    history.Id,
                    generated.CitationQuery ?? string.Empty,
                    citations.Select(citation => citation.ChunkId).ToArray(),
                    citations,
                    cancellationToken);
            }

            var finalUpdatedAt = DateTime.UtcNow;
            var finalized = await dbContext.OutgoingDocuments
                .Where(document => document.Id == outgoingDocumentId
                    && document.Status == OutgoingDocumentStatus.PendingReview
                    && document.UpdatedAt == pendingReviewAt)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(document => document.ReviewIssues, reviewIssues)
                        .SetProperty(document => document.Status, finalStatus)
                        .SetProperty(document => document.UpdatedAt, finalUpdatedAt),
                    cancellationToken);
            if (finalized != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ReviewOperationResult<ReviewResponse>.Conflict(
                    "Văn bản đã thay đổi trong khi lưu kết quả thẩm định. Kết quả review không được lưu.");
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ReviewOperationResult<ReviewResponse>.Success(new ReviewResponse(
                history.Id,
                outgoingDocumentId,
                history.AttemptNo,
                history.ReviewSource,
                ToStaffReference(documentSnapshot.DraftedByStaff),
                history.ContentSnapshot,
                history.ReviewResult,
                generated.Issues,
                citations,
                history.ReviewedAt,
                finalStatus));
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning(
                exception,
                "Document review persistence conflicted for outgoing document {OutgoingDocumentId}",
                outgoingDocumentId);
            await transaction.RollbackAsync(cancellationToken);
            return ReviewOperationResult<ReviewResponse>.Conflict(
                "Có một lượt thẩm định khác vừa được lưu. Vui lòng tải lại văn bản.");
        }
    }

    public async Task<ReviewOperationResult<PagedResponse<ReviewResponse>>> GetListAsync(
        Guid outgoingDocumentId,
        ReviewListQuery query,
        CancellationToken cancellationToken = default)
    {
        var documentExists = await dbContext.OutgoingDocuments
            .AsNoTracking()
            .AnyAsync(document => document.Id == outgoingDocumentId, cancellationToken);
        if (!documentExists)
        {
            return ReviewOperationResult<PagedResponse<ReviewResponse>>.NotFound();
        }

        var reviews = dbContext.ReviewHistory
            .AsNoTracking()
            .Where(review => review.OutgoingDocumentId == outgoingDocumentId);
        var totalCount = await reviews.CountAsync(cancellationToken);
        var items = await reviews
            .Include(review => review.ReviewedByStaff)
            .OrderByDescending(review => review.ReviewedAt)
            .ThenByDescending(review => review.AttemptNo)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArrayAsync(cancellationToken);
        var reviewIds = items.Select(review => review.Id).ToArray();
        var citationPayloads = await dbContext.RagCitationSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.BusinessEntityType == "ReviewHistory"
                && reviewIds.Contains(snapshot.BusinessEntityId))
            .OrderByDescending(snapshot => snapshot.CreatedAt)
            .ToArrayAsync(cancellationToken);
        var citationsByReviewId = citationPayloads
            .GroupBy(snapshot => snapshot.BusinessEntityId)
            .ToDictionary(
                group => group.Key,
                group => DeserializeCitations(
                    group.First().CitationPayloadJson));
        var responses = items
            .Select(review => ToResponse(
                review,
                documentStatus: null,
                citationsByReviewId.GetValueOrDefault(review.Id) ?? []))
            .ToArray();
        return ReviewOperationResult<PagedResponse<ReviewResponse>>.Success(
            new PagedResponse<ReviewResponse>(
                responses,
                query.Page,
                query.PageSize,
                totalCount,
                (int)Math.Ceiling(totalCount / (double)query.PageSize)));
    }

    private async Task<ReviewOperationResult<ReviewResponse>> ClassifyClaimFailureAsync(
        Guid outgoingDocumentId,
        Guid callerStaffId,
        CancellationToken cancellationToken)
    {
        var current = await ReviewDocumentQuery(dbContext.OutgoingDocuments.AsNoTracking())
            .SingleOrDefaultAsync(document => document.Id == outgoingDocumentId, cancellationToken);
        var precondition = ValidateReviewPreconditions(current, callerStaffId);
        return precondition
            ?? ReviewOperationResult<ReviewResponse>.Conflict(
                "Văn bản hoặc FormatRules đã thay đổi trong khi thẩm định. Kết quả review không được lưu.");
    }

    private static ReviewOperationResult<ReviewResponse>? ValidateReviewPreconditions(
        OutgoingDocument? document,
        Guid callerStaffId)
    {
        if (document is null)
        {
            return ReviewOperationResult<ReviewResponse>.NotFound();
        }

        if (document.DraftedByStaffId != callerStaffId)
        {
            return ReviewOperationResult<ReviewResponse>.Forbidden(
                "Chỉ cán bộ soạn văn bản mới được gửi thẩm định.");
        }

        if (document.Status is not (OutgoingDocumentStatus.Editing or OutgoingDocumentStatus.ReviewFailed))
        {
            return ReviewOperationResult<ReviewResponse>.Conflict(
                "Trạng thái hiện tại không cho phép gửi thẩm định.");
        }

        return null;
    }

    private static IQueryable<OutgoingDocument> ReviewDocumentQuery(
        IQueryable<OutgoingDocument> query) =>
        query
            .Include(document => document.Template)
                .ThenInclude(template => template.DocumentType)
            .Include(document => document.DraftedByStaff);

    private static DocumentReviewInput ToReviewInput(OutgoingDocument document) =>
        new(
            document.TemplateId,
            document.Template.Name,
            document.Template.DocumentType.Code,
            document.Template.DocumentType.Name,
            document.Template.UpdatedAt,
            document.Template.FormatRules.Clone(),
            document.Content);

    private static ReviewResponse ToResponse(
        ReviewHistory review,
        OutgoingDocumentStatus? documentStatus,
        IReadOnlyList<ReviewCitationResponse> citations) =>
        new(
            review.Id,
            review.OutgoingDocumentId,
            review.AttemptNo,
            review.ReviewSource,
            review.ReviewedByStaff is null ? null : ToStaffReference(review.ReviewedByStaff),
            review.ContentSnapshot,
            review.ReviewResult,
            DeserializeReviewIssues(review.ReviewIssues),
            citations,
            review.ReviewedAt,
            documentStatus ?? (review.ReviewResult == ReviewResult.Failed
                ? OutgoingDocumentStatus.ReviewFailed
                : OutgoingDocumentStatus.PendingApproval));

    private static OutgoingStaffReference ToStaffReference(Staff staff) =>
        new(staff.Id, staff.FullName, staff.Position, staff.Department);

    private static IReadOnlyList<ReviewIssueResponse> DeserializeReviewIssues(
        JsonElement reviewIssues)
    {
        try
        {
            return JsonSerializer.Deserialize<List<ReviewIssueResponse>>(
                reviewIssues.GetRawText()) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<ReviewCitationResponse> DeserializeCitations(
        string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<List<ReviewCitationResponse>>(
                payload,
                JsonSerializerOptions.Web) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
