using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DigitalOps.API.Features.Attachments;
using DigitalOps.API.Features.Drafting;
using DigitalOps.API.Features.IncomingDocuments;
using Microsoft.AspNetCore.Mvc;

namespace DigitalOps.API.Features.OutgoingDocuments;

public sealed class OutgoingDocumentCreateRequest
{
    [StringLength(500)]
    public string? Title { get; init; }

    public Guid? TemplateId { get; init; }

    public Guid? RelatedIncomingDocumentId { get; init; }

    public Guid? RelatedMemberId { get; init; }
}

public sealed class OutgoingDocumentUpdateRequest
{
    private string? _title;
    private string? _content;

    [StringLength(500)]
    public string? Title
    {
        get => _title;
        set
        {
            _title = value;
            HasTitle = true;
        }
    }

    public string? Content
    {
        get => _content;
        set
        {
            _content = value;
            HasContent = true;
        }
    }

    [JsonIgnore]
    public bool HasTitle { get; private set; }

    [JsonIgnore]
    public bool HasContent { get; private set; }

    [JsonIgnore]
    public bool HasAnyField => HasTitle || HasContent;
}

public sealed class AiDraftRequest
{
    public string? Instruction { get; init; }
}

public sealed class OutgoingDocumentListQuery : IValidatableObject
{
    [FromQuery(Name = "q")]
    [StringLength(200)]
    public string? Q { get; init; }

    [FromQuery(Name = "templateId")]
    public Guid? TemplateId { get; init; }

    [FromQuery(Name = "relatedIncomingDocumentId")]
    public Guid? RelatedIncomingDocumentId { get; init; }

    [FromQuery(Name = "relatedMemberId")]
    public Guid? RelatedMemberId { get; init; }

    [FromQuery(Name = "status")]
    public OutgoingDocumentStatus? Status { get; init; }

    [FromQuery(Name = "draftedByStaffId")]
    public Guid? DraftedByStaffId { get; init; }

    [FromQuery(Name = "dateFrom")]
    public DateOnly? DateFrom { get; init; }

    [FromQuery(Name = "dateTo")]
    public DateOnly? DateTo { get; init; }

    [FromQuery(Name = "page")]
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [FromQuery(Name = "pageSize")]
    [Range(1, 100)]
    public int PageSize { get; init; } = 20;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DateFrom is not null && DateTo is not null && DateFrom > DateTo)
        {
            yield return new ValidationResult(
                "Ngày bắt đầu không được sau ngày kết thúc.",
                [nameof(DateFrom)]);
        }
    }
}

public sealed record OutgoingTemplateReference(
    Guid Id,
    string Name,
    DocumentTypeReference DocumentType);

public sealed record OutgoingIncomingReference(
    Guid Id,
    string ReferenceNumber,
    string Summary);

public sealed record OutgoingMemberReference(
    Guid Id,
    string FullName,
    string? Position);

public sealed record OutgoingStaffReference(
    Guid Id,
    string FullName,
    string? Position,
    string? Department);

public sealed record ReviewIssueResponse(
    string RuleCode,
    string Severity,
    string Message,
    string? Location);

public sealed record OutgoingDocumentResponse(
    Guid Id,
    OutgoingTemplateReference Template,
    OutgoingIncomingReference? RelatedIncomingDocument,
    OutgoingMemberReference? RelatedMember,
    string Title,
    string Content,
    string? AiDraftContent,
    OutgoingStaffReference DraftedByStaff,
    OutgoingDocumentStatus Status,
    IReadOnlyList<ReviewIssueResponse> ReviewIssues,
    OutgoingStaffReference? ApprovedByStaff,
    DateTime? ApprovedAt,
    string? ReferenceNumber,
    DateOnly? IssuedDate,
    DateTime? ArchivedAt,
    IReadOnlyList<AttachmentResponse> Attachments,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public enum OutgoingDocumentFailure
{
    None,
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    ServiceUnavailable
}

public sealed record OutgoingDocumentResult<T>(
    T? Value,
    OutgoingDocumentFailure Failure,
    string? Detail,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public bool Succeeded => Failure == OutgoingDocumentFailure.None;

    public static OutgoingDocumentResult<T> Success(T value) =>
        new(value, OutgoingDocumentFailure.None, null, EmptyErrors());

    public static OutgoingDocumentResult<T> Validation(
        IReadOnlyDictionary<string, string[]> errors) =>
        new(default, OutgoingDocumentFailure.Validation, null, errors);

    public static OutgoingDocumentResult<T> NotFound() =>
        new(default, OutgoingDocumentFailure.NotFound, null, EmptyErrors());

    public static OutgoingDocumentResult<T> Conflict(string detail) =>
        new(default, OutgoingDocumentFailure.Conflict, detail, EmptyErrors());

    public static OutgoingDocumentResult<T> Forbidden(string detail) =>
        new(default, OutgoingDocumentFailure.Forbidden, detail, EmptyErrors());

    public static OutgoingDocumentResult<T> ServiceUnavailable(string detail) =>
        new(default, OutgoingDocumentFailure.ServiceUnavailable, detail, EmptyErrors());

    private static IReadOnlyDictionary<string, string[]> EmptyErrors() =>
        new Dictionary<string, string[]>();
}
