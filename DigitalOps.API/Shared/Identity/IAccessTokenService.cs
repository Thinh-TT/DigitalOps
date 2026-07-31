namespace DigitalOps.API.Shared.Identity;

public interface IAccessTokenService
{
    AccessTokenResult CreateToken(
        ApplicationUser user,
        Staff staff,
        IReadOnlyCollection<string> roles);
}

public sealed record AccessTokenResult(string AccessToken, DateTimeOffset ExpiresAt);
