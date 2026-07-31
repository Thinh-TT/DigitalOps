using DigitalOps.API.Features.IncomingDocuments;
using Microsoft.AspNetCore.Mvc;

namespace DigitalOps.API.Features.Attachments;

public sealed class AttachmentUploadForm
{
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }
}

public sealed record AttachmentResponse(
    Guid Id,
    string FileName,
    IncomingStaffReference UploadedBy,
    DateTime UploadedAt,
    ExtractionStatus ExtractionStatus,
    DateTime? ExtractedAt);

public sealed record AttachmentDownload(
    Stream Content,
    string FileName,
    string ContentType);
