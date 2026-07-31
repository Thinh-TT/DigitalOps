using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DigitalOps.API.Shared.Identity;

public sealed class JwtAccessTokenService(
    IOptions<JwtOptions> jwtOptionsAccessor,
    TimeProvider timeProvider) : IAccessTokenService
{
    private readonly JwtOptions _jwtOptions = jwtOptionsAccessor.Value;

    public AccessTokenResult CreateToken(
        ApplicationUser user,
        Staff staff,
        IReadOnlyCollection<string> roles)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(staff);
        ArgumentNullException.ThrowIfNull(roles);

        if (user.Id == Guid.Empty || staff.Id == Guid.Empty || staff.IdentityUserId != user.Id)
        {
            throw new InvalidOperationException(
                "The Identity user and Staff record must have a valid one-to-one relationship.");
        }

        var normalizedRoles = roles
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (normalizedRoles.Any(role => !SystemRoles.All.Contains(role)))
        {
            throw new ArgumentException("One or more roles are not supported by DigitalOps.", nameof(roles));
        }

        var issuedAt = timeProvider.GetUtcNow();
        var expiresAt = issuedAt.AddMinutes(_jwtOptions.AccessTokenLifetimeMinutes);
        var claims = new List<Claim>
        {
            new(JwtClaimNames.Subject, user.Id.ToString()),
            new(JwtClaimNames.StaffId, staff.Id.ToString()),
            new(
                JwtClaimNames.MustChangePassword,
                user.MustChangePassword ? "true" : "false",
                ClaimValueTypes.Boolean)
        };

        claims.AddRange(normalizedRoles.Select(role => new Claim(JwtClaimNames.Role, role)));

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new AccessTokenResult(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt);
    }
}
