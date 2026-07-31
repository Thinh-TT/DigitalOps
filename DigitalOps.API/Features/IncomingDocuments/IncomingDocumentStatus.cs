using System.Text.Json.Serialization;

namespace DigitalOps.API.Features.IncomingDocuments;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IncomingDocumentStatus
{
    New,
    InProgress,
    Completed,
    Overdue
}
