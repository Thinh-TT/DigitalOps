using DigitalOps.API.Shared.Data;

namespace DigitalOps.API.Features.Members;

public sealed class Member : IAuditableEntity
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public DateOnly? DateOfBirth { get; set; }

    public string? Gender { get; set; }

    public string? Address { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Position { get; set; }

    public DateOnly? JoinDate { get; set; }

    public MemberStatus Status { get; set; } = MemberStatus.Active;

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
