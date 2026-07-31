using DigitalOps.API.Shared.Data;

namespace DigitalOps.API.Features.Drafting;

public sealed class DocumentType : IAuditableEntity
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<DocumentTemplate> Templates { get; set; } = new List<DocumentTemplate>();

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
