namespace DigitalOps.API.Features.OutgoingDocuments;

public enum OutgoingDocumentStatus
{
    Editing,
    AiDraft,
    PendingReview,
    ReviewFailed,
    PendingApproval,
    Approved,
    Archived
}
