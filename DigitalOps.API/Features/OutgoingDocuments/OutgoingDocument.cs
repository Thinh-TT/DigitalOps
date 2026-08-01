using System.Text.Json;
using DigitalOps.API.Features.Attachments;
using DigitalOps.API.Features.Drafting;
using DigitalOps.API.Features.IncomingDocuments;
using DigitalOps.API.Features.Members;
using DigitalOps.API.Shared.Data;
using DigitalOps.API.Shared.Identity;

namespace DigitalOps.API.Features.OutgoingDocuments;

public sealed class OutgoingDocument : IAuditableEntity
{
    public Guid Id { get; set; }

    public Guid TemplateId { get; set; }

    public DocumentTemplate Template { get; set; } = null!;

    public Guid? RelatedIncomingDocumentId { get; set; }

    public IncomingDocument? RelatedIncomingDocument { get; set; }

    public Guid? RelatedMemberId { get; set; }

    public Member? RelatedMember { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string? AiDraftContent { get; set; }

    public Guid DraftedByStaffId { get; set; }

    public Staff DraftedByStaff { get; set; } = null!;

    public OutgoingDocumentStatus Status { get; set; } = OutgoingDocumentStatus.Editing;

    public JsonElement ReviewIssues { get; set; } = JsonDocument.Parse("[]").RootElement.Clone();

    public Guid? ApprovedByStaffId { get; set; }

    public Staff? ApprovedByStaff { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public string? ReferenceNumber { get; set; }

    public DateOnly? IssuedDate { get; set; }

    public DateTime? ArchivedAt { get; set; }

    public ICollection<Attachment> Attachments { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
