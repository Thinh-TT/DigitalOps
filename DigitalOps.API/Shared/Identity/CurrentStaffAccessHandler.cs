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

        var accessState = await staffAccessChecker.GetAccessStateAsync(
            claims.IdentityUserId,
            claims.StaffId);
        if (accessState is null)
        {
            return;
        }

        if (!requirement.MustChangePassword.HasValue)
        {
            context.Succeed(requirement);
            return;
        }

        if (requirement.MustChangePassword == false
            && (claims.MustChangePassword || accessState.MustChangePassword))
        {
            context.Fail(new AuthorizationFailureReason(
                this,
                PasswordChangeRequiredFailureReason));
            return;
        }

        if (requirement.MustChangePassword == false
            || claims.MustChangePassword
            || accessState.MustChangePassword)
        {
            context.Succeed(requirement);
        }
    }
}
