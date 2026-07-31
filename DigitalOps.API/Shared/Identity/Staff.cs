using DigitalOps.API.Shared.Data;

namespace DigitalOps.API.Shared.Identity;

public sealed class Staff : IAuditableEntity
{
    public Guid Id { get; set; }

    public Guid IdentityUserId { get; set; }

    public ApplicationUser IdentityUser { get; set; } = null!;

    public string FullName { get; set; } = string.Empty;

    public string? Position { get; set; }

    public string? Department { get; set; }

    public string Email { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
