using DigitalOps.API.Shared.Data;
using DigitalOps.API.Shared.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DigitalOps.API.Features.Authentication;

public sealed class AuthenticationService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    DigitalOpsDbContext dbContext,
    IAccessTokenService accessTokenService) : IAuthenticationService
{
    private const string CurrentPasswordField = "currentPassword";
    private const string NewPasswordField = "newPassword";

    public async Task<LoginResponse?> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var identifier = request.UserNameOrEmail.Trim();
        var user = await userManager.FindByNameAsync(identifier)
            ?? await userManager.FindByEmailAsync(identifier);

        if (user is null)
        {
            return null;
        }

        var signInResult = await signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: true);

        if (!signInResult.Succeeded)
        {
            return null;
        }

        var staff = await FindActiveStaffAsync(
            user.Id,
            staffId: null,
            cancellationToken);

        if (staff is null)
        {
            return null;
        }

        var roles = await userManager.GetRolesAsync(user);
        return CreateLoginResponse(user, staff, roles.ToArray());
    }

    public async Task<CurrentUserResponse?> GetCurrentUserAsync(
        Guid identityUserId,
        Guid staffId,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(identityUserId.ToString());
        if (user is null)
        {
            return null;
        }

        var staff = await FindActiveStaffAsync(identityUserId, staffId, cancellationToken);
        if (staff is null)
        {
            return null;
        }

        var roles = await userManager.GetRolesAsync(user);
        return new CurrentUserResponse(
            ToStaffReference(staff),
            NormalizeRoles(roles),
            user.MustChangePassword);
    }

    public async Task<ChangePasswordResult> ChangePasswordAsync(
        Guid identityUserId,
        Guid staffId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(identityUserId.ToString());
        if (user is null)
        {
            return ChangePasswordResult.Forbidden();
        }

        var staff = await FindActiveStaffAsync(identityUserId, staffId, cancellationToken);
        if (staff is null)
        {
            return ChangePasswordResult.Forbidden();
        }

        var roles = await userManager.GetRolesAsync(user);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);

        var passwordResult = await userManager.ChangePasswordAsync(
            user,
            request.CurrentPassword,
            request.NewPassword);

        if (!passwordResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);

            var currentPasswordErrors = passwordResult.Errors
                .Where(error => string.Equals(
                    error.Code,
                    nameof(IdentityErrorDescriber.PasswordMismatch),
                    StringComparison.Ordinal))
                .Select(_ => "Mật khẩu hiện tại không đúng.")
                .ToArray();

            return currentPasswordErrors.Length > 0
                ? ChangePasswordResult.ValidationFailure(
                    CurrentPasswordField,
                    currentPasswordErrors)
                : ChangePasswordResult.ValidationFailure(
                    NewPasswordField,
                    passwordResult.Errors.Select(error => error.Description));
        }

        user.MustChangePassword = false;
        var updateResult = await userManager.UpdateAsync(user);

        if (!updateResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException(
                "The password was changed but the forced-password flag could not be updated.");
        }

        var response = CreateLoginResponse(user, staff, roles.ToArray());
        await transaction.CommitAsync(cancellationToken);

        return ChangePasswordResult.Success(response);
    }

    private Task<Staff?> FindActiveStaffAsync(
        Guid identityUserId,
        Guid? staffId,
        CancellationToken cancellationToken) =>
        dbContext.Staff
            .AsNoTracking()
            .SingleOrDefaultAsync(
                staff =>
                    staff.IdentityUserId == identityUserId
                    && (!staffId.HasValue || staff.Id == staffId.Value)
                    && staff.IsActive,
                cancellationToken);

    private LoginResponse CreateLoginResponse(
        ApplicationUser user,
        Staff staff,
        IReadOnlyCollection<string> roles)
    {
        var normalizedRoles = NormalizeRoles(roles);
        var token = accessTokenService.CreateToken(user, staff, normalizedRoles);

        return new LoginResponse(
            token.AccessToken,
            token.ExpiresAt,
            user.MustChangePassword,
            ToStaffReference(staff),
            normalizedRoles);
    }

    private static string[] NormalizeRoles(IEnumerable<string> roles) =>
        roles
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static StaffReference ToStaffReference(Staff staff) =>
        new(staff.Id, staff.FullName, staff.Position, staff.Department);
}
