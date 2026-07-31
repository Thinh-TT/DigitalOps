using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace DigitalOps.API.Shared.Identity;

public sealed class CurrentStaffAccessHandler(IStaffAccessChecker staffAccessChecker)
    : AuthorizationHandler<CurrentStaffAccessRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CurrentStaffAccessRequirement requirement)
    {
        if (!TryReadGuidClaim(context.User, JwtClaimNames.Subject, out var identityUserId)
            || !TryReadGuidClaim(context.User, JwtClaimNames.StaffId, out var staffId)
            || !bool.TryParse(
                context.User.FindFirstValue(JwtClaimNames.MustChangePassword),
                out var mustChangePassword)
            || mustChangePassword != requirement.MustChangePassword)
        {
            return;
        }

        if (await staffAccessChecker.IsActiveAsync(identityUserId, staffId))
        {
            context.Succeed(requirement);
        }
    }

    private static bool TryReadGuidClaim(
        ClaimsPrincipal principal,
        string claimType,
        out Guid value) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out value)
        && value != Guid.Empty;
}
