using DigitalOps.API.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace DigitalOps.API.Shared.Identity;

public sealed class StaffAccessChecker(DigitalOpsDbContext dbContext) : IStaffAccessChecker
{
    public Task<StaffAccessState?> GetAccessStateAsync(
        Guid identityUserId,
        Guid staffId,
        CancellationToken cancellationToken = default) =>
        dbContext.Staff
            .AsNoTracking()
            .Where(
                staff =>
                    staff.Id == staffId
                    && staff.IdentityUserId == identityUserId
                    && staff.IsActive)
            .Select(staff => new StaffAccessState(
                staff.IdentityUser.MustChangePassword))
            .SingleOrDefaultAsync(
                cancellationToken);
}
