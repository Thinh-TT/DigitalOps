namespace DigitalOps.API.Features.Authentication;

public interface IAuthenticationService
{
    Task<LoginResponse?> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default);

    Task<CurrentUserResponse?> GetCurrentUserAsync(
        Guid identityUserId,
        Guid staffId,
        IReadOnlyCollection<string> tokenRoles,
        CancellationToken cancellationToken = default);

    Task<ChangePasswordResult> ChangePasswordAsync(
        Guid identityUserId,
        Guid staffId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ChangePasswordResult(
    LoginResponse? Response,
    Dictionary<string, string[]> Errors,
    bool IsForbidden)
{
    public bool Succeeded => Response is not null;

    public static ChangePasswordResult Success(LoginResponse response) =>
        new(response, [], IsForbidden: false);

    public static ChangePasswordResult ValidationFailure(
        string field,
        IEnumerable<string> errors) =>
        new(
            Response: null,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = errors.ToArray()
            },
            IsForbidden: false);

    public static ChangePasswordResult Forbidden() =>
        new(Response: null, [], IsForbidden: true);
}
