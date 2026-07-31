namespace DigitalOps.API.Shared.Identity;

public interface IStaffAccessChecker
{
    Task<StaffAccessState?> GetAccessStateAsync(
        Guid identityUserId,
        Guid staffId,
        CancellationToken cancellationToken = default);
}

public sealed record StaffAccessState(bool MustChangePassword);
