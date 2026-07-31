using DigitalOps.API.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace DigitalOps.API.Shared.Identity;

public sealed class StaffAccessChecker(DigitalOpsDbContext dbContext) : IStaffAccessChecker
{
    public Task<bool> IsActiveAsync(
        Guid identityUserId,
        Guid staffId,
        CancellationToken cancellationToken = default) =>
        dbContext.Staff
            .AsNoTracking()
            .AnyAsync(
                staff =>
                    staff.Id == staffId
                    && staff.IdentityUserId == identityUserId
                    && staff.IsActive,
                cancellationToken);
}
