using DigitalOps.API.Features.IncomingDocuments;
using DigitalOps.API.Shared.Identity;

namespace DigitalOps.API.Features.Reminders;

public sealed class ReminderHistory
{
    public Guid Id { get; set; }

    public Guid IncomingDocumentId { get; set; }

    public IncomingDocument IncomingDocument { get; set; } = null!;

    public Guid RecipientStaffId { get; set; }

    public Staff RecipientStaff { get; set; } = null!;

    public ReminderKind ReminderKind { get; set; }

    public DateOnly ReminderDate { get; set; }

    public ReminderDeliveryStatus DeliveryStatus { get; set; } = ReminderDeliveryStatus.Unread;

    public DateTime CreatedAt { get; set; }

    public DateTime? ReadAt { get; set; }
}
