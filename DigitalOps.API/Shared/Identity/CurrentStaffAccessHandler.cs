using Microsoft.AspNetCore.Authorization;

namespace DigitalOps.API.Shared.Identity;

public sealed class CurrentStaffAccessHandler(IStaffAccessChecker staffAccessChecker)
    : AuthorizationHandler<CurrentStaffAccessRequirement>
{
    public const string PasswordChangeRequiredFailureReason =
        "password-change-required";

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CurrentStaffAccessRequirement requirement)
    {
        if (!CurrentStaffClaims.TryRead(context.User, out var claims))
        {
            return;
        }

        if (!await staffAccessChecker.IsActiveAsync(
                claims.IdentityUserId,
                claims.StaffId))
        {
            return;
        }

        if (!requirement.MustChangePassword.HasValue
            || claims.MustChangePassword == requirement.MustChangePassword.Value)
        {
            context.Succeed(requirement);
            return;
        }

        if (requirement.MustChangePassword == false
            && claims.MustChangePassword)
        {
            context.Fail(new AuthorizationFailureReason(
                this,
                PasswordChangeRequiredFailureReason));
        }
    }
}
