using DigitalOps.API.Features.IncomingDocuments;

namespace DigitalOps.API.Features.Attachments;

internal static class AttachmentMappings
{
    public static AttachmentResponse ToResponse(Attachment attachment) =>
        new(
            attachment.Id,
            attachment.FileName,
            new IncomingStaffReference(
                attachment.UploadedByStaff.Id,
                attachment.UploadedByStaff.FullName,
                attachment.UploadedByStaff.Position,
                attachment.UploadedByStaff.Department),
            attachment.UploadedAt,
            attachment.ExtractionStatus,
            attachment.ExtractedAt);
}
