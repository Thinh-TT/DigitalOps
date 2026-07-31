using System.IdentityModel.Tokens.Jwt;
using System.Text;
using DigitalOps.API.Shared.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DigitalOps.API.Tests;

public sealed class JwtAccessTokenServiceTests
{
    private const string SigningKey = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void CreateToken_returns_a_signed_eight_hour_token_with_required_claims()
    {
        var now = DateTimeOffset.UtcNow;
        var options = CreateOptions();
        var service = new JwtAccessTokenService(
            Options.Create(options),
            new FixedTimeProvider(now));
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            MustChangePassword = true
        };
        var staff = new Staff
        {
            Id = Guid.NewGuid(),
            IdentityUserId = user.Id
        };

        var result = service.CreateToken(
            user,
            staff,
            [SystemRoles.Clerk, SystemRoles.Leader, SystemRoles.Clerk]);

        Assert.Equal(now.AddHours(8), result.ExpiresAt);

        var principal = new JwtSecurityTokenHandler
        {
            MapInboundClaims = false
        }.ValidateToken(
            result.AccessToken,
            CreateValidationParameters(options),
            out var validatedToken);

        var jwt = Assert.IsType<JwtSecurityToken>(validatedToken);
        Assert.Equal(SecurityAlgorithms.HmacSha256, jwt.Header.Alg);
        Assert.Equal(user.Id.ToString(), principal.FindFirst(JwtClaimNames.Subject)?.Value);
        Assert.Equal(staff.Id.ToString(), principal.FindFirst(JwtClaimNames.StaffId)?.Value);
        Assert.Equal("true", principal.FindFirst(JwtClaimNames.MustChangePassword)?.Value);
        Assert.Equal(
            [SystemRoles.Clerk, SystemRoles.Leader],
            principal.FindAll(JwtClaimNames.Role).Select(claim => claim.Value).Order().ToArray());
        Assert.True(principal.IsInRole(SystemRoles.Clerk));
        Assert.True(principal.IsInRole(SystemRoles.Leader));
    }

    [Fact]
    public void CreateToken_rejects_a_mismatched_identity_user_and_staff()
    {
        var service = new JwtAccessTokenService(
            Options.Create(CreateOptions()),
            TimeProvider.System);
        var user = new ApplicationUser { Id = Guid.NewGuid() };
        var staff = new Staff
        {
            Id = Guid.NewGuid(),
            IdentityUserId = Guid.NewGuid()
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => service.CreateToken(user, staff, []));

        Assert.Contains("one-to-one relationship", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateToken_rejects_an_unknown_role()
    {
        var service = new JwtAccessTokenService(
            Options.Create(CreateOptions()),
            TimeProvider.System);
        var user = new ApplicationUser { Id = Guid.NewGuid() };
        var staff = new Staff
        {
            Id = Guid.NewGuid(),
            IdentityUserId = user.Id
        };

        Assert.Throws<ArgumentException>(
            () => service.CreateToken(user, staff, ["UnknownRole"]));
    }

    private static JwtOptions CreateOptions() => new()
    {
        Issuer = "DigitalOps.API",
        Audience = "DigitalOps.Web",
        SigningKey = SigningKey,
        AccessTokenLifetimeMinutes = 480
    };

    private static TokenValidationParameters CreateValidationParameters(JwtOptions options) => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
        ValidateIssuer = true,
        ValidIssuer = options.Issuer,
        ValidateAudience = true,
        ValidAudience = options.Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        NameClaimType = JwtClaimNames.Subject,
        RoleClaimType = JwtClaimNames.Role
    };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
