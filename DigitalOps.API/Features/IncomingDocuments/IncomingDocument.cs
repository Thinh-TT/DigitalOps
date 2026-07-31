using DigitalOps.API.Features.Drafting;
using DigitalOps.API.Shared.Data;
using DigitalOps.API.Shared.Identity;

namespace DigitalOps.API.Features.IncomingDocuments;

public sealed class IncomingDocument : IAuditableEntity
{
    public Guid Id { get; set; }

    public string ReferenceNumber { get; set; } = string.Empty;

    public string SenderOrg { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public DateOnly ReceivedDate { get; set; }

    public DateOnly Deadline { get; set; }

    public Guid DocumentTypeId { get; set; }

    public DocumentType DocumentType { get; set; } = null!;

    public Guid? SuggestedStaffId { get; set; }

    public Staff? SuggestedStaff { get; set; }

    public string? AssignmentSuggestionReason { get; set; }

    public decimal? AssignmentConfidence { get; set; }

    public DateTime? AssignmentSuggestedAt { get; set; }

    public Guid? AssignedToStaffId { get; set; }

    public Staff? AssignedToStaff { get; set; }

    public Guid? AssignmentConfirmedByStaffId { get; set; }

    public Staff? AssignmentConfirmedByStaff { get; set; }

    public DateTime? AssignmentConfirmedAt { get; set; }

    public IncomingDocumentStatus Status { get; set; } = IncomingDocumentStatus.New;

    public DateTime? CompletedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
