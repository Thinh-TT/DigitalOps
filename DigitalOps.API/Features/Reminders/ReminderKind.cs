using System.Text.Json.Serialization;

namespace DigitalOps.API.Features.Reminders;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReminderKind
{
    BeforeDeadline,
    DueDate,
    Overdue
}
