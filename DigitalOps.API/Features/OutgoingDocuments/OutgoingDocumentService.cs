using System.Data;
using System.Text.Json;
using DigitalOps.API.Features.Attachments;
using DigitalOps.API.Features.Drafting;
using DigitalOps.API.Features.IncomingDocuments;
using DigitalOps.API.Features.Members;
using DigitalOps.API.Shared.AI;
using DigitalOps.API.Shared.Api;
using DigitalOps.API.Shared.Data;
using DigitalOps.API.Shared.Identity;
using Microsoft.EntityFrameworkCore;

namespace DigitalOps.API.Features.OutgoingDocuments;

public sealed class OutgoingDocumentService(
    DigitalOpsDbContext dbContext,
    IAiDraftGenerator aiDraftGenerator,
    ILogger<OutgoingDocumentService> logger)
    : IOutgoingDocumentService
{
    public async Task<PagedResponse<OutgoingDocumentResponse>> GetListAsync(
        OutgoingDocumentListQuery query,
        CancellationToken cancellationToken = default)
    {
        var documents = dbContext.OutgoingDocuments.AsNoTracking();
        var normalizedQuery = query.Q?.Trim();

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            var search = normalizedQuery.ToLowerInvariant();
            documents = documents.Where(document =>
                document.Title.ToLower().Contains(search)
                || (document.ReferenceNumber != null
                    && document.ReferenceNumber.ToLower().Contains(search)));
        }

        if (query.TemplateId is not null)
        {
            documents = documents.Where(document => document.TemplateId == query.TemplateId);
        }

        if (query.RelatedIncomingDocumentId is not null)
        {
            documents = documents.Where(document =>
                document.RelatedIncomingDocumentId == query.RelatedIncomingDocumentId);
        }

        if (query.RelatedMemberId is not null)
        {
            documents = documents.Where(document => document.RelatedMemberId == query.RelatedMemberId);
        }

        if (query.Status is not null)
        {
            documents = documents.Where(document => document.Status == query.Status);
        }

        if (query.DraftedByStaffId is not null)
        {
            documents = documents.Where(document => document.DraftedByStaffId == query.DraftedByStaffId);
        }

        if (query.DateFrom is not null)
        {
            documents = documents.Where(document =>
                document.CreatedAt >= query.DateFrom.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        }

        if (query.DateTo is not null)
        {
            var exclusiveTo = query.DateTo.Value.AddDays(1)
                .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            documents = documents.Where(document => document.CreatedAt < exclusiveTo);
        }

        var totalCount = await documents.CountAsync(cancellationToken);
        var items = await WithReferences(documents)
            .OrderByDescending(document => document.UpdatedAt)
            .ThenBy(document => document.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArrayAsync(cancellationToken);

        return new PagedResponse<OutgoingDocumentResponse>(
            items.Select(ToResponse).ToArray(),
            query.Page,
            query.PageSize,
            totalCount,
            (int)Math.Ceiling(totalCount / (double)query.PageSize));
    }

    public async Task<OutgoingDocumentResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var document = await WithReferences(dbContext.OutgoingDocuments.AsNoTracking())
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return document is null ? null : ToResponse(document);
    }

    public async Task<OutgoingDocumentResult<OutgoingDocumentResponse>> CreateAsync(
        OutgoingDocumentCreateRequest request,
        Guid draftedByStaffId,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateRequest(request);
        if (errors.Count > 0)
        {
            return OutgoingDocumentResult<OutgoingDocumentResponse>.Validation(errors);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var template = await dbContext.DocumentTemplates
            .Include(item => item.DocumentType)
            .SingleOrDefaultAsync(item => item.Id == request.TemplateId, cancellationToken);
        if (template is null)
        {
            return OutgoingDocumentResult<OutgoingDocumentResponse>.Validation(
                SingleError("templateId", "Mẫu văn bản không tồn tại."));
        }

        if (!template.IsActive || !template.DocumentType.IsActive)
        {
            return OutgoingDocumentResult<OutgoingDocumentResponse>.Conflict(
                "Mẫu văn bản hoặc loại văn bản cha đã ngừng hoạt động.");
        }

        Member? member = null;
        if (request.RelatedMemberId is not null)
        {
            member = await dbContext.Members.SingleOrDefaultAsync(
                item => item.Id == request.RelatedMemberId,
                cancellationToken);
            if (member is null)
            {
                return OutgoingDocumentResult<OutgoingDocumentResponse>.Validation(
                    SingleError("relatedMemberId", "Hội viên không tồn tại."));
            }

            if (member.Status != MemberStatus.Active)
            {
                return OutgoingDocumentResult<OutgoingDocumentResponse>.Conflict(
                    "Hội viên đã ngừng hoạt động.");
            }
        }

        IncomingDocument? incoming = null;
        if (request.RelatedIncomingDocumentId is not null)
        {
            incoming = await dbContext.IncomingDocuments.SingleOrDefaultAsync(
                item => item.Id == request.RelatedIncomingDocumentId,
                cancellationToken);
            if (incoming is null)
            {
                return OutgoingDocumentResult<OutgoingDocumentResponse>.Validation(
                    SingleError("relatedIncomingDocumentId", "Văn bản đến không tồn tại."));
            }
        }

        var draftedByStaff = await dbContext.Staff.SingleOrDefaultAsync(
            item => item.Id == draftedByStaffId && item.IsActive,
            cancellationToken);
        if (draftedByStaff is null)
        {
            return OutgoingDocumentResult<OutgoingDocumentResponse>.Forbidden(
                "Cán bộ soạn thảo hiện tại không còn hoạt động.");
        }

        var document = new OutgoingDocument
        {
            Id = Guid.NewGuid(),
            TemplateId = template.Id,
            Template = template,
            RelatedIncomingDocumentId = incoming?.Id,
            RelatedIncomingDocument = incoming,
            RelatedMemberId = member?.Id,
            RelatedMember = member,
            Title = request.Title!.Trim(),
            Content = OutgoingTemplateRenderer.Render(template.TemplateContent, member, incoming),
            ReviewIssues = JsonDocument.Parse("[]").RootElement.Clone(),
            DraftedByStaffId = draftedByStaff.Id,
            DraftedByStaff = draftedByStaff,
            Status = OutgoingDocumentStatus.Editing
        };

        dbContext.OutgoingDocuments.Add(document);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return OutgoingDocumentResult<OutgoingDocumentResponse>.Success(ToResponse(document));
    }

    public async Task<OutgoingDocumentResult<OutgoingDocumentResponse>> UpdateAsync(
        Guid id,
        OutgoingDocumentUpdateRequest request,
        Guid callerStaffId,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateUpdateRequest(request);
        if (errors.Count > 0)
        {
            return OutgoingDocumentResult<OutgoingDocumentResponse>.Validation(errors);
        }

        var document = await WithReferences(dbContext.OutgoingDocuments)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (document is null)
        {
            return OutgoingDocumentResult<OutgoingDocumentResponse>.NotFound();
        }

        if (document.DraftedByStaffId != callerStaffId)
        {
            return OutgoingDocumentResult<OutgoingDocumentResponse>.Forbidden(
                "Chỉ cán bộ soạn văn bản mới được chỉnh sửa nội dung.");
        }

        if (!IsEditable(document.Status))
        {
            return OutgoingDocumentResult<OutgoingDocumentResponse>.Conflict(
                "Trạng thái hiện tại không cho phép chỉnh sửa văn bản.");
        }

        if (request.HasTitle)
        {
            document.Title = request.Title!.Trim();
        }

        if (request.HasContent)
        {
            document.Content = request.Content!.Trim();
        }

        document.Status = OutgoingDocumentStatus.Editing;
        await dbContext.SaveChangesAsync(cancellationToken);
        return OutgoingDocumentResult<OutgoingDocumentResponse>.Success(ToResponse(document));
    }

    public async Task<OutgoingDocumentResult<OutgoingDocumentResponse>> GenerateAiDraftAsync(
        Guid id,
        AiDraftRequest request,
        Guid callerStaffId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await WithReferences(dbContext.OutgoingDocuments.AsNoTracking())
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        var precondition = ValidateAiDraftPreconditions(snapshot, callerStaffId);
        if (precondition is not null)
        {
            return precondition;
        }

        AiDraftGenerationResult generated;
        try
        {
            generated = await aiDraftGenerator.GenerateAsync(
                ToAiDraftInput(snapshot!, request.Instruction),
                cancellationToken);
        }
        catch (AiProviderException exception)
        {
            logger.LogWarning(
                exception,
                "AI draft failed for outgoing document {OutgoingDocumentId}",
                id);
            return OutgoingDocumentResult<OutgoingDocumentResponse>.ServiceUnavailable(
                "Dịch vụ AI hiện không khả dụng. Nội dung văn bản đã lưu không bị thay đổi.");
        }

        var updatedAt = DateTime.UtcNow;
        var affected = await dbContext.OutgoingDocuments
            .Where(document => document.Id == id
                && document.DraftedByStaffId == callerStaffId
                && document.UpdatedAt == snapshot!.UpdatedAt
                && document.Template.UpdatedAt == snapshot.Template.UpdatedAt
                && document.Template.IsActive
                && document.Template.DocumentType.IsActive
                && (document.Status == OutgoingDocumentStatus.AiDraft
                    || document.Status == OutgoingDocumentStatus.Editing
                    || document.Status == OutgoingDocumentStatus.ReviewFailed))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(document => document.Content, generated.Content)
                    .SetProperty(
                        document => document.AiDraftContent,
                        document => document.AiDraftContent == null
                            ? generated.Content
                            : document.AiDraftContent)
                    .SetProperty(
                        document => document.Status,
                        document => document.AiDraftContent == null
                            ? OutgoingDocumentStatus.AiDraft
                            : OutgoingDocumentStatus.Editing)
                    .SetProperty(document => document.UpdatedAt, updatedAt),
                cancellationToken);

        if (affected == 0)
        {
            return await ClassifyAiDraftApplyFailureAsync(
                id,
                callerStaffId,
                cancellationToken);
        }

        var response = await GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException(
                "The outgoing document disappeared after an AI draft update.");
        return OutgoingDocumentResult<OutgoingDocumentResponse>.Success(response);
    }

    private async Task<OutgoingDocumentResult<OutgoingDocumentResponse>> ClassifyAiDraftApplyFailureAsync(
        Guid id,
        Guid callerStaffId,
        CancellationToken cancellationToken)
    {
        var current = await dbContext.OutgoingDocuments
            .AsNoTracking()
            .Include(document => document.Template)
                .ThenInclude(template => template.DocumentType)
            .SingleOrDefaultAsync(document => document.Id == id, cancellationToken);
        var precondition = ValidateAiDraftPreconditions(current, callerStaffId);
        return precondition
            ?? OutgoingDocumentResult<OutgoingDocumentResponse>.Conflict(
                "Văn bản hoặc mẫu đã thay đổi trong khi AI đang xử lý. Kết quả AI không được lưu.");
    }

    private static OutgoingDocumentResult<OutgoingDocumentResponse>? ValidateAiDraftPreconditions(
        OutgoingDocument? document,
        Guid callerStaffId)
    {
        if (document is null)
        {
            return OutgoingDocumentResult<OutgoingDocumentResponse>.NotFound();
        }

        if (document.DraftedByStaffId != callerStaffId)
        {
            return OutgoingDocumentResult<OutgoingDocumentResponse>.Forbidden(
                "Chỉ cán bộ soạn văn bản mới được sinh nháp AI.");
        }

        if (!IsEditable(document.Status))
        {
            return OutgoingDocumentResult<OutgoingDocumentResponse>.Conflict(
                "Trạng thái hiện tại không cho phép sinh nháp AI.");
        }

        if (!document.Template.IsActive || !document.Template.DocumentType.IsActive)
        {
            return OutgoingDocumentResult<OutgoingDocumentResponse>.Conflict(
                "Mẫu văn bản hoặc loại văn bản cha đã ngừng hoạt động; không thể sinh nháp AI.");
        }

        return null;
    }

    private static AiDraftGenerationInput ToAiDraftInput(
        OutgoingDocument document,
        string? instruction) =>
        new(
            document.TemplateId,
            document.Template.Name,
            document.Template.DocumentType.Code,
            document.Template.DocumentType.Name,
            document.Title,
            document.Content,
            document.RelatedMember is null
                ? null
                : new AiDraftMemberContext(
                    document.RelatedMember.FullName,
                    document.RelatedMember.DateOfBirth,
                    document.RelatedMember.Gender,
                    document.RelatedMember.Address,
                    document.RelatedMember.Phone,
                    document.RelatedMember.Email,
                    document.RelatedMember.Position,
                    document.RelatedMember.JoinDate),
            document.RelatedIncomingDocument is null
                ? null
                : new AiDraftIncomingContext(
                    document.RelatedIncomingDocument.ReferenceNumber,
                    document.RelatedIncomingDocument.SenderOrg,
                    document.RelatedIncomingDocument.Summary,
                    document.RelatedIncomingDocument.ReceivedDate,
                    document.RelatedIncomingDocument.Deadline),
            string.IsNullOrWhiteSpace(instruction) ? null : instruction.Trim());

    private static IQueryable<OutgoingDocument> WithReferences(
        IQueryable<OutgoingDocument> query) =>
        query
            .Include(document => document.Template)
                .ThenInclude(template => template.DocumentType)
            .Include(document => document.RelatedIncomingDocument)
            .Include(document => document.RelatedMember)
            .Include(document => document.DraftedByStaff)
            .Include(document => document.ApprovedByStaff)
            .Include(document => document.Attachments)
                .ThenInclude(attachment => attachment.UploadedByStaff);

    private static OutgoingDocumentResponse ToResponse(OutgoingDocument document) =>
        new(
            document.Id,
            new OutgoingTemplateReference(
                document.Template.Id,
                document.Template.Name,
                new DocumentTypeReference(
                    document.Template.DocumentType.Id,
                    document.Template.DocumentType.Code,
                    document.Template.DocumentType.Name)),
            document.RelatedIncomingDocument is null
                ? null
                : new OutgoingIncomingReference(
                    document.RelatedIncomingDocument.Id,
                    document.RelatedIncomingDocument.ReferenceNumber,
                    document.RelatedIncomingDocument.Summary),
            document.RelatedMember is null
                ? null
                : new OutgoingMemberReference(
                    document.RelatedMember.Id,
                    document.RelatedMember.FullName,
                    document.RelatedMember.Position),
            document.Title,
            document.Content,
            document.AiDraftContent,
            ToStaffReference(document.DraftedByStaff),
            document.Status,
            DeserializeReviewIssues(document.ReviewIssues),
            document.ApprovedByStaff is null ? null : ToStaffReference(document.ApprovedByStaff),
            document.ApprovedAt,
            document.ReferenceNumber,
            document.IssuedDate,
            document.ArchivedAt,
            document.Attachments
                .OrderByDescending(attachment => attachment.UploadedAt)
                .ThenBy(attachment => attachment.Id)
                .Select(AttachmentMappings.ToResponse)
                .ToArray(),
            document.CreatedAt,
            document.UpdatedAt);

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

    private static Dictionary<string, string[]> ValidateRequest(
        OutgoingDocumentCreateRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            errors["title"] = ["Vui lòng nhập tiêu đề văn bản."];
        }

        if (request.TemplateId is null || request.TemplateId == Guid.Empty)
        {
            errors["templateId"] = ["Vui lòng chọn mẫu văn bản."];
        }

        if (request.RelatedIncomingDocumentId == Guid.Empty)
        {
            errors["relatedIncomingDocumentId"] = ["Văn bản đến liên quan không hợp lệ."];
        }

        if (request.RelatedMemberId == Guid.Empty)
        {
            errors["relatedMemberId"] = ["Hội viên liên quan không hợp lệ."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateUpdateRequest(
        OutgoingDocumentUpdateRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!request.HasAnyField)
        {
            errors["body"] = ["Vui lòng cung cấp ít nhất một trường cần cập nhật."];
        }

        if (request.HasTitle && string.IsNullOrWhiteSpace(request.Title))
        {
            errors["title"] = ["Vui lòng nhập tiêu đề văn bản."];
        }

        if (request.HasContent && string.IsNullOrWhiteSpace(request.Content))
        {
            errors["content"] = ["Vui lòng nhập nội dung văn bản."];
        }

        return errors;
    }

    private static bool IsEditable(OutgoingDocumentStatus status) =>
        status is OutgoingDocumentStatus.AiDraft
            or OutgoingDocumentStatus.Editing
            or OutgoingDocumentStatus.ReviewFailed;

    private static IReadOnlyDictionary<string, string[]> SingleError(
        string field,
        string message) =>
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [field] = [message]
        };
}
