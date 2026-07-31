using System.Text.Json.Serialization;

namespace DigitalOps.API.Features.Attachments;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExtractionStatus
{
    Pending,
    Processing,
    Succeeded,
    Failed,
    Unsupported
}
