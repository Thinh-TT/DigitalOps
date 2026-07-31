namespace DigitalOps.API.Shared.Data;

public interface IAuditableEntity
{
    DateTime CreatedAt { get; set; }

    DateTime UpdatedAt { get; set; }
}
