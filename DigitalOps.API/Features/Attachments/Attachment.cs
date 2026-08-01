using DigitalOps.API.Features.IncomingDocuments;
using DigitalOps.API.Features.OutgoingDocuments;
using DigitalOps.API.Shared.Identity;

namespace DigitalOps.API.Features.Attachments;

public sealed class Attachment
{
    public Guid Id { get; set; }

    public Guid? IncomingDocumentId { get; set; }

    public IncomingDocument? IncomingDocument { get; set; }

    public Guid? OutgoingDocumentId { get; set; }

    public OutgoingDocument? OutgoingDocument { get; set; }

    public string StorageKey { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public Guid UploadedByStaffId { get; set; }

    public Staff UploadedByStaff { get; set; } = null!;

    public ExtractionStatus ExtractionStatus { get; set; }

    public string? ExtractedText { get; set; }

    public string? ExtractionError { get; set; }

    public DateTime? ExtractedAt { get; set; }

    public DateTime UploadedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
