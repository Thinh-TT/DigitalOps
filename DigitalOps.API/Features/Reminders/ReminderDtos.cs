using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace DigitalOps.API.Features.Reminders;

public sealed class ReminderListQuery
{
    [FromQuery(Name = "deliveryStatus")]
    public ReminderDeliveryStatus? DeliveryStatus { get; init; }

    [FromQuery(Name = "recipientStaffId")]
    public Guid? RecipientStaffId { get; init; }

    [FromQuery(Name = "page")]
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [FromQuery(Name = "pageSize")]
    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}

public sealed record ReminderResponse(
    Guid Id,
    Guid IncomingDocumentId,
    string ReferenceNumber,
    string Summary,
    ReminderKind ReminderKind,
    DateOnly ReminderDate,
    ReminderDeliveryStatus DeliveryStatus,
    DateTime CreatedAt,
    DateTime? ReadAt);
