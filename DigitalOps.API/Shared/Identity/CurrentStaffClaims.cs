using System.Security.Claims;

namespace DigitalOps.API.Shared.Identity;

public sealed record CurrentStaffClaimValues(
    Guid IdentityUserId,
    Guid StaffId,
    bool MustChangePassword);

public static class CurrentStaffClaims
{
    public static bool TryRead(
        ClaimsPrincipal principal,
        out CurrentStaffClaimValues values)
    {
        if (TryReadGuidClaim(principal, JwtClaimNames.Subject, out var identityUserId)
            && TryReadGuidClaim(principal, JwtClaimNames.StaffId, out var staffId)
            && bool.TryParse(
                principal.FindFirstValue(JwtClaimNames.MustChangePassword),
                out var mustChangePassword))
        {
            values = new CurrentStaffClaimValues(
                identityUserId,
                staffId,
                mustChangePassword);
            return true;
        }

        values = null!;
        return false;
    }

    private static bool TryReadGuidClaim(
        ClaimsPrincipal principal,
        string claimType,
        out Guid value) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out value)
        && value != Guid.Empty;
}
