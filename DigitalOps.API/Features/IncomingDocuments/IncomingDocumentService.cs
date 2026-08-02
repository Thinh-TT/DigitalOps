using DigitalOps.API.Features.Attachments;
using DigitalOps.API.Features.Drafting;
using DigitalOps.API.Shared.AI;
using DigitalOps.API.Shared.Api;
using DigitalOps.API.Shared.Data;
using DigitalOps.API.Shared.Identity;
using Microsoft.EntityFrameworkCore;

namespace DigitalOps.API.Features.IncomingDocuments;

public sealed class IncomingDocumentService(
    DigitalOpsDbContext dbContext,
    TimeProvider timeProvider,
    IAssignmentSuggestionGenerator assignmentSuggestionGenerator,
    ILogger<IncomingDocumentService> logger) : IIncomingDocumentService
{
    public async Task<PagedResponse<IncomingDocumentResponse>> GetListAsync(
        IncomingDocumentListQuery query,
        CancellationToken cancellationToken = default)
    {
        var documents = dbContext.IncomingDocuments.AsNoTracking();
        var normalizedQuery = NormalizeOptional(query.Q)?.ToLowerInvariant();

        if (normalizedQuery is not null)
        {
            documents = documents.Where(document =>
                document.ReferenceNumber.ToLower().Contains(normalizedQuery)
                || document.SenderOrg.ToLower().Contains(normalizedQuery)
                || document.Summary.ToLower().Contains(normalizedQuery));
        }

        if (query.DocumentTypeId is not null)
        {
            documents = documents.Where(document =>
                document.DocumentTypeId == query.DocumentTypeId);
        }

        if (query.Status is not null)
        {
            documents = documents.Where(document => document.Status == query.Status);
        }

        if (query.AssignedToStaffId is not null)
        {
            documents = documents.Where(document =>
                document.AssignedToStaffId == query.AssignedToStaffId);
        }

        if (query.DeadlineFrom is not null)
        {
            documents = documents.Where(document =>
                document.Deadline >= query.DeadlineFrom);
        }

        if (query.DeadlineTo is not null)
        {
            documents = documents.Where(document =>
                document.Deadline <= query.DeadlineTo);
        }

        var totalCount = await documents.CountAsync(cancellationToken);
        var items = await WithReferences(documents)
            .OrderByDescending(document => document.ReceivedDate)
            .ThenByDescending(document => document.CreatedAt)
            .ThenBy(document => document.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArrayAsync(cancellationToken);

        return CreatePage(
            items.Select(ToResponse).ToArray(),
            query.Page,
            query.PageSize,
            totalCount);
    }

    public async Task<IncomingDocumentResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var document = await WithReferences(
                dbContext.IncomingDocuments.AsNoTracking())
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return document is null ? null : ToResponse(document);
    }

    public async Task<IncomingDocumentResult<IncomingDocumentResponse>> CreateAsync(
        IncomingDocumentCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateCreate(request);
        if (errors.Count > 0)
        {
            return IncomingDocumentResult<IncomingDocumentResponse>.Validation(errors);
        }

        var documentType = await GetActiveDocumentTypeAsync(
            request.DocumentTypeId!.Value,
            cancellationToken);
        if (documentType is null)
        {
            return IncomingDocumentResult<IncomingDocumentResponse>.Validation(
                SingleError(
                    "documentTypeId",
                    "Loại văn bản không tồn tại hoặc đã ngừng hoạt động."));
        }

        var document = new IncomingDocument
        {
            Id = Guid.NewGuid(),
            ReferenceNumber = request.ReferenceNumber!.Trim(),
            SenderOrg = request.SenderOrg!.Trim(),
            Summary = request.Summary!.Trim(),
            ReceivedDate = request.ReceivedDate!.Value,
            Deadline = request.Deadline!.Value,
            DocumentTypeId = documentType.Id,
            DocumentType = documentType,
            Status = IncomingDocumentStatus.New
        };

        dbContext.IncomingDocuments.Add(document);
        await dbContext.SaveChangesAsync(cancellationToken);

        return IncomingDocumentResult<IncomingDocumentResponse>.Success(
            ToResponse(document));
    }

    public async Task<IncomingDocumentResult<IncomingDocumentResponse>> UpdateAsync(
        Guid id,
        IncomingDocumentUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateUpdate(request);
        if (errors.Count > 0)
        {
            return IncomingDocumentResult<IncomingDocumentResponse>.Validation(errors);
        }

        var document = await WithReferences(dbContext.IncomingDocuments)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (document is null)
        {
            return IncomingDocumentResult<IncomingDocumentResponse>.NotFound();
        }

        if (document.Status == IncomingDocumentStatus.Completed)
        {
            return IncomingDocumentResult<IncomingDocumentResponse>.Conflict(
                "Văn bản đến đã hoàn tất và không thể chỉnh sửa.");
        }

        var finalReceivedDate = request.HasReceivedDate
            ? request.ReceivedDate!.Value
            : document.ReceivedDate;
        var finalDeadline = request.HasDeadline
            ? request.Deadline!.Value
            : document.Deadline;
        if (finalReceivedDate > finalDeadline)
        {
            return IncomingDocumentResult<IncomingDocumentResponse>.Validation(
                SingleError(
                    "deadline",
                    "Hạn xử lý không được trước ngày tiếp nhận."));
        }

        if (request.HasDocumentTypeId
            && request.DocumentTypeId!.Value != document.DocumentTypeId)
        {
            var documentType = await GetActiveDocumentTypeAsync(
                request.DocumentTypeId.Value,
                cancellationToken);
            if (documentType is null)
            {
                return IncomingDocumentResult<IncomingDocumentResponse>.Validation(
                    SingleError(
                        "documentTypeId",
                        "Loại văn bản không tồn tại hoặc đã ngừng hoạt động."));
            }

            document.DocumentTypeId = documentType.Id;
            document.DocumentType = documentType;
        }

        if (request.HasReferenceNumber)
        {
            document.ReferenceNumber = request.ReferenceNumber!.Trim();
        }

        if (request.HasSenderOrg)
        {
            document.SenderOrg = request.SenderOrg!.Trim();
        }

        if (request.HasSummary)
        {
            document.Summary = request.Summary!.Trim();
        }

        if (request.HasReceivedDate)
        {
            document.ReceivedDate = request.ReceivedDate!.Value;
        }

        if (request.HasDeadline)
        {
            document.Deadline = request.Deadline!.Value;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return IncomingDocumentResult<IncomingDocumentResponse>.Success(
            ToResponse(document));
    }

    public async Task<IncomingDocumentResult<AssignmentSuggestionResponse>> SuggestAssignmentAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await dbContext.IncomingDocuments
            .AsNoTracking()
            .Include(document => document.DocumentType)
            .SingleOrDefaultAsync(document => document.Id == id, cancellationToken);
        if (snapshot is null)
        {
            return IncomingDocumentResult<AssignmentSuggestionResponse>.NotFound();
        }

        if (snapshot.Status == IncomingDocumentStatus.Completed)
        {
            return IncomingDocumentResult<AssignmentSuggestionResponse>.Conflict(
                "Văn bản đến đã hoàn tất và không thể chạy gợi ý điều phối.");
        }

        AssignmentSuggestionDecision suggestion;
        try
        {
            suggestion = await assignmentSuggestionGenerator.SuggestAsync(
                new AssignmentSuggestionInput(
                    snapshot.Summary,
                    snapshot.DocumentType.Code,
                    snapshot.DocumentType.Name),
                cancellationToken);
        }
        catch (AiProviderException exception)
        {
            logger.LogWarning(
                exception,
                "Assignment suggestion failed for incoming document {IncomingDocumentId}",
                id);
            return IncomingDocumentResult<AssignmentSuggestionResponse>.ServiceUnavailable(
                "Dịch vụ AI hiện không khả dụng. Bạn vẫn có thể chọn cán bộ xử lý thủ công.");
        }

        var document = await WithReferences(dbContext.IncomingDocuments)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (document is null)
        {
            return IncomingDocumentResult<AssignmentSuggestionResponse>.NotFound();
        }

        if (document.Status == IncomingDocumentStatus.Completed)
        {
            return IncomingDocumentResult<AssignmentSuggestionResponse>.Conflict(
                "Văn bản đến đã hoàn tất trong khi AI đang xử lý; gợi ý không được lưu.");
        }

        if (suggestion.Decision == AssignmentSuggestionDecisionKind.InsufficientEvidence)
        {
            document.SuggestedStaffId = null;
            document.SuggestedStaff = null;
            document.AssignmentSuggestionReason = null;
            document.AssignmentConfidence = null;
            document.AssignmentSuggestedAt = null;
            await dbContext.SaveChangesAsync(cancellationToken);

            return IncomingDocumentResult<AssignmentSuggestionResponse>.Success(
                new AssignmentSuggestionResponse(
                    document.Id,
                    null,
                    suggestion.Reason,
                    null,
                    null));
        }

        var suggestedStaff = await dbContext.Staff.SingleOrDefaultAsync(
            staff => staff.Id == suggestion.SuggestedStaffId && staff.IsActive,
            cancellationToken);
        if (suggestedStaff is null)
        {
            return IncomingDocumentResult<AssignmentSuggestionResponse>.ServiceUnavailable(
                "Kết quả AI không còn hợp lệ vì cán bộ được gợi ý đã thay đổi trạng thái.");
        }

        var suggestedAt = timeProvider.GetUtcNow().UtcDateTime;
        document.SuggestedStaffId = suggestedStaff.Id;
        document.SuggestedStaff = suggestedStaff;
        document.AssignmentSuggestionReason = suggestion.Reason;
        document.AssignmentConfidence = null;
        document.AssignmentSuggestedAt = suggestedAt;
        await dbContext.SaveChangesAsync(cancellationToken);

        return IncomingDocumentResult<AssignmentSuggestionResponse>.Success(
            new AssignmentSuggestionResponse(
                document.Id,
                ToStaffReference(suggestedStaff),
                suggestion.Reason,
                null,
                suggestedAt));
    }

    public async Task<IncomingDocumentResult<IncomingDocumentResponse>> ConfirmAssignmentAsync(
        Guid id,
        AssignmentConfirmRequest request,
        Guid confirmedByStaffId,
        CancellationToken cancellationToken = default)
    {
        if (request.AssignedToStaffId == Guid.Empty)
        {
            return IncomingDocumentResult<IncomingDocumentResponse>.Validation(
                SingleError(
                    "assignedToStaffId",
                    "Vui lòng chọn cán bộ xử lý."));
        }

        var document = await WithReferences(dbContext.IncomingDocuments)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (document is null)
        {
            return IncomingDocumentResult<IncomingDocumentResponse>.NotFound();
        }

        if (document.Status == IncomingDocumentStatus.Completed)
        {
            return IncomingDocumentResult<IncomingDocumentResponse>.Conflict(
                "Văn bản đến đã hoàn tất và không thể điều phối lại.");
        }

        var assignedStaff = await dbContext.Staff.SingleOrDefaultAsync(
            staff => staff.Id == request.AssignedToStaffId && staff.IsActive,
            cancellationToken);
        if (assignedStaff is null)
        {
            return IncomingDocumentResult<IncomingDocumentResponse>.Validation(
                SingleError(
                    "assignedToStaffId",
                    "Cán bộ xử lý không tồn tại hoặc đã ngừng hoạt động."));
        }

        var confirmingStaff = await dbContext.Staff.SingleOrDefaultAsync(
            staff => staff.Id == confirmedByStaffId && staff.IsActive,
            cancellationToken);
        if (confirmingStaff is null)
        {
            return IncomingDocumentResult<IncomingDocumentResponse>.Forbidden();
        }

        document.AssignedToStaffId = assignedStaff.Id;
        document.AssignedToStaff = assignedStaff;
        document.AssignmentConfirmedByStaffId = confirmingStaff.Id;
        document.AssignmentConfirmedByStaff = confirmingStaff;
        document.AssignmentConfirmedAt = timeProvider.GetUtcNow().UtcDateTime;
        if (document.Status == IncomingDocumentStatus.New)
        {
            document.Status = IncomingDocumentStatus.InProgress;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return IncomingDocumentResult<IncomingDocumentResponse>.Success(
            ToResponse(document));
    }

    public async Task<IncomingDocumentResult<IncomingDocumentResponse>> CompleteAsync(
        Guid id,
        Guid callerStaffId,
        bool callerIsClerk,
        CancellationToken cancellationToken = default)
    {
        var document = await WithReferences(dbContext.IncomingDocuments)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (document is null)
        {
            return IncomingDocumentResult<IncomingDocumentResponse>.NotFound();
        }

        if (!callerIsClerk && document.AssignedToStaffId != callerStaffId)
        {
            return IncomingDocumentResult<IncomingDocumentResponse>.Forbidden();
        }

        if (document.AssignedToStaffId is null
            || document.Status is not (
                IncomingDocumentStatus.InProgress
                or IncomingDocumentStatus.Overdue))
        {
            return IncomingDocumentResult<IncomingDocumentResponse>.Conflict(
                "Chỉ văn bản đang xử lý hoặc quá hạn và đã được giao mới có thể hoàn tất.");
        }

        document.Status = IncomingDocumentStatus.Completed;
        document.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);

        return IncomingDocumentResult<IncomingDocumentResponse>.Success(
            ToResponse(document));
    }

    private async Task<DocumentType?> GetActiveDocumentTypeAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        await dbContext.DocumentTypes.SingleOrDefaultAsync(
            item => item.Id == id && item.IsActive,
            cancellationToken);

    private static IQueryable<IncomingDocument> WithReferences(
        IQueryable<IncomingDocument> query) =>
        query
            .Include(document => document.DocumentType)
            .Include(document => document.SuggestedStaff)
            .Include(document => document.AssignedToStaff)
            .Include(document => document.AssignmentConfirmedByStaff)
            .Include(document => document.Attachments)
                .ThenInclude(attachment => attachment.UploadedByStaff);

    private static Dictionary<string, string[]> ValidateCreate(
        IncomingDocumentCreateRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        ValidateRequiredString(
            errors,
            "referenceNumber",
            request.ReferenceNumber,
            100,
            "Vui lòng nhập số, ký hiệu văn bản.");
        ValidateRequiredString(
            errors,
            "senderOrg",
            request.SenderOrg,
            255,
            "Vui lòng nhập cơ quan gửi.");
        ValidateRequiredString(
            errors,
            "summary",
            request.Summary,
            null,
            "Vui lòng nhập trích yếu văn bản.");

        if (request.ReceivedDate is null)
        {
            errors["receivedDate"] = ["Vui lòng nhập ngày tiếp nhận."];
        }

        if (request.Deadline is null)
        {
            errors["deadline"] = ["Vui lòng nhập hạn xử lý."];
        }
        else if (request.ReceivedDate is not null
            && request.ReceivedDate > request.Deadline)
        {
            errors["deadline"] = ["Hạn xử lý không được trước ngày tiếp nhận."];
        }

        if (request.DocumentTypeId is null
            || request.DocumentTypeId == Guid.Empty)
        {
            errors["documentTypeId"] = ["Vui lòng chọn loại văn bản."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateUpdate(
        IncomingDocumentUpdateRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!request.HasAnyField)
        {
            errors["body"] = ["Vui lòng cung cấp ít nhất một trường cần cập nhật."];
            return errors;
        }

        if (request.HasReferenceNumber)
        {
            ValidateRequiredString(
                errors,
                "referenceNumber",
                request.ReferenceNumber,
                100,
                "Số, ký hiệu văn bản không được để trống.");
        }

        if (request.HasSenderOrg)
        {
            ValidateRequiredString(
                errors,
                "senderOrg",
                request.SenderOrg,
                255,
                "Cơ quan gửi không được để trống.");
        }

        if (request.HasSummary)
        {
            ValidateRequiredString(
                errors,
                "summary",
                request.Summary,
                null,
                "Trích yếu văn bản không được để trống.");
        }

        if (request.HasReceivedDate && request.ReceivedDate is null)
        {
            errors["receivedDate"] = ["Ngày tiếp nhận không được để trống."];
        }

        if (request.HasDeadline && request.Deadline is null)
        {
            errors["deadline"] = ["Hạn xử lý không được để trống."];
        }

        if (request.HasDocumentTypeId
            && (request.DocumentTypeId is null
                || request.DocumentTypeId == Guid.Empty))
        {
            errors["documentTypeId"] = ["Loại văn bản không được để trống."];
        }

        return errors;
    }

    private static void ValidateRequiredString(
        IDictionary<string, string[]> errors,
        string field,
        string? value,
        int? maxLength,
        string requiredMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = [requiredMessage];
        }
        else if (maxLength is not null && value.Trim().Length > maxLength)
        {
            errors[field] = [$"Giá trị không được vượt quá {maxLength} ký tự."];
        }
    }

    private static IReadOnlyDictionary<string, string[]> SingleError(
        string field,
        string error) =>
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [field] = [error]
        };

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static PagedResponse<T> CreatePage<T>(
        IReadOnlyList<T> items,
        int page,
        int pageSize,
        int totalCount) =>
        new(
            items,
            page,
            pageSize,
            totalCount,
            totalCount == 0
                ? 0
                : (int)Math.Ceiling((double)totalCount / pageSize));

    private static IncomingDocumentResponse ToResponse(IncomingDocument document) =>
        new(
            document.Id,
            document.ReferenceNumber,
            document.SenderOrg,
            document.Summary,
            document.ReceivedDate,
            document.Deadline,
            new DocumentTypeReference(
                document.DocumentType.Id,
                document.DocumentType.Code,
                document.DocumentType.Name),
            ToStaffReference(document.SuggestedStaff),
            document.AssignmentSuggestionReason,
            document.AssignmentConfidence,
            document.AssignmentSuggestedAt,
            ToStaffReference(document.AssignedToStaff),
            ToStaffReference(document.AssignmentConfirmedByStaff),
            document.AssignmentConfirmedAt,
            document.Status,
            document.CompletedAt,
            document.Attachments
                .OrderByDescending(attachment => attachment.UploadedAt)
                .ThenBy(attachment => attachment.Id)
                .Select(AttachmentMappings.ToResponse)
                .ToArray(),
            document.CreatedAt,
            document.UpdatedAt);

    private static IncomingStaffReference? ToStaffReference(Staff? staff) =>
        staff is null
            ? null
            : new IncomingStaffReference(
                staff.Id,
                staff.FullName,
                staff.Position,
                staff.Department);
}
