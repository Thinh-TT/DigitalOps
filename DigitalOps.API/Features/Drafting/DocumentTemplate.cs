using System.Text.Json;
using DigitalOps.API.Shared.Data;

namespace DigitalOps.API.Features.Drafting;

public sealed class DocumentTemplate : IAuditableEntity
{
    public Guid Id { get; set; }

    public Guid DocumentTypeId { get; set; }

    public DocumentType DocumentType { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public string TemplateContent { get; set; } = string.Empty;

    public JsonElement FormatRules { get; set; } = JsonDocument.Parse("{}").RootElement.Clone();

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
