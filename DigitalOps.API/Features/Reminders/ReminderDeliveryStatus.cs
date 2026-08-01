using System.Text.Json.Serialization;

namespace DigitalOps.API.Features.Reminders;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReminderDeliveryStatus
{
    Unread,
    Read
}
