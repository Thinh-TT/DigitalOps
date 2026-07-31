using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DigitalOps.API.Features.Attachments;
using DigitalOps.API.Features.Drafting;
using Microsoft.AspNetCore.Mvc;

namespace DigitalOps.API.Features.IncomingDocuments;

public sealed class IncomingDocumentCreateRequest
{
    [StringLength(100)]
    public string? ReferenceNumber { get; init; }

    [StringLength(255)]
    public string? SenderOrg { get; init; }

    public string? Summary { get; init; }

    public DateOnly? ReceivedDate { get; init; }

    public DateOnly? Deadline { get; init; }

    public Guid? DocumentTypeId { get; init; }
}

public sealed class IncomingDocumentUpdateRequest
{
    private string? _referenceNumber;
    private string? _senderOrg;
    private string? _summary;
    private DateOnly? _receivedDate;
    private DateOnly? _deadline;
    private Guid? _documentTypeId;

    [StringLength(100)]
    public string? ReferenceNumber
    {
        get => _referenceNumber;
        set
        {
            _referenceNumber = value;
            HasReferenceNumber = true;
        }
    }

    [StringLength(255)]
    public string? SenderOrg
    {
        get => _senderOrg;
        set
        {
            _senderOrg = value;
            HasSenderOrg = true;
        }
    }

    public string? Summary
    {
        get => _summary;
        set
        {
            _summary = value;
            HasSummary = true;
        }
    }

    public DateOnly? ReceivedDate
    {
        get => _receivedDate;
        set
        {
            _receivedDate = value;
            HasReceivedDate = true;
        }
    }

    public DateOnly? Deadline
    {
        get => _deadline;
        set
        {
            _deadline = value;
            HasDeadline = true;
        }
    }

    public Guid? DocumentTypeId
    {
        get => _documentTypeId;
        set
        {
            _documentTypeId = value;
            HasDocumentTypeId = true;
        }
    }

    [JsonIgnore]
    public bool HasReferenceNumber { get; private set; }

    [JsonIgnore]
    public bool HasSenderOrg { get; private set; }

    [JsonIgnore]
    public bool HasSummary { get; private set; }

    [JsonIgnore]
    public bool HasReceivedDate { get; private set; }

    [JsonIgnore]
    public bool HasDeadline { get; private set; }

    [JsonIgnore]
    public bool HasDocumentTypeId { get; private set; }

    [JsonIgnore]
    public bool HasAnyField =>
        HasReferenceNumber
        || HasSenderOrg
        || HasSummary
        || HasReceivedDate
        || HasDeadline
        || HasDocumentTypeId;
}

public sealed class IncomingDocumentListQuery : IValidatableObject
{
    [FromQuery(Name = "q")]
    [StringLength(200)]
    public string? Q { get; init; }

    [FromQuery(Name = "documentTypeId")]
    public Guid? DocumentTypeId { get; init; }

    [FromQuery(Name = "status")]
    public IncomingDocumentStatus? Status { get; init; }

    [FromQuery(Name = "assignedToStaffId")]
    public Guid? AssignedToStaffId { get; init; }

    [FromQuery(Name = "deadlineFrom")]
    public DateOnly? DeadlineFrom { get; init; }

    [FromQuery(Name = "deadlineTo")]
    public DateOnly? DeadlineTo { get; init; }

    [FromQuery(Name = "page")]
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [FromQuery(Name = "pageSize")]
    [Range(1, 100)]
    public int PageSize { get; init; } = 20;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DeadlineFrom is not null
            && DeadlineTo is not null
            && DeadlineFrom > DeadlineTo)
        {
            yield return new ValidationResult(
                "Ngày bắt đầu hạn xử lý không được sau ngày kết thúc.",
                [nameof(DeadlineFrom)]);
        }
    }
}

public sealed record IncomingStaffReference(
    Guid Id,
    string FullName,
    string? Position,
    string? Department);

public sealed record IncomingDocumentResponse(
    Guid Id,
    string ReferenceNumber,
    string SenderOrg,
    string Summary,
    DateOnly ReceivedDate,
    DateOnly Deadline,
    DocumentTypeReference DocumentType,
    IncomingStaffReference? SuggestedStaff,
    string? AssignmentSuggestionReason,
    decimal? AssignmentConfidence,
    DateTime? AssignmentSuggestedAt,
    IncomingStaffReference? AssignedToStaff,
    IncomingStaffReference? AssignmentConfirmedBy,
    DateTime? AssignmentConfirmedAt,
    IncomingDocumentStatus Status,
    DateTime? CompletedAt,
    IReadOnlyList<AttachmentResponse> Attachments,
    DateTime CreatedAt,
    DateTime UpdatedAt);
