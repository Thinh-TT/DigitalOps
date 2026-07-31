namespace DigitalOps.API.Shared.Identity;

public interface IStaffAccessChecker
{
    Task<bool> IsActiveAsync(
        Guid identityUserId,
        Guid staffId,
        CancellationToken cancellationToken = default);
}
