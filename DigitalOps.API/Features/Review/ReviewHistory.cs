using System.Text.Json;
using DigitalOps.API.Features.OutgoingDocuments;
using DigitalOps.API.Shared.Identity;

namespace DigitalOps.API.Features.Review;

public sealed class ReviewHistory
{
    public Guid Id { get; set; }

    public Guid OutgoingDocumentId { get; set; }

    public OutgoingDocument OutgoingDocument { get; set; } = null!;

    public int AttemptNo { get; set; }

    public ReviewSource ReviewSource { get; set; }

    public Guid? ReviewedByStaffId { get; set; }

    public Staff? ReviewedByStaff { get; set; }

    public string ContentSnapshot { get; set; } = string.Empty;

    public ReviewResult ReviewResult { get; set; }

    public JsonElement ReviewIssues { get; set; } = JsonDocument.Parse("[]").RootElement.Clone();

    public DateTime ReviewedAt { get; set; }
}
