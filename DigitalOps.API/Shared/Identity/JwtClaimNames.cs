using System.IdentityModel.Tokens.Jwt;

namespace DigitalOps.API.Shared.Identity;

public static class JwtClaimNames
{
    public const string Subject = JwtRegisteredClaimNames.Sub;

    public const string StaffId = "staffId";

    public const string Role = "role";

    public const string MustChangePassword = "mustChangePassword";
}
