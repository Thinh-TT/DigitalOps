using System.ComponentModel.DataAnnotations;

namespace DigitalOps.API.Features.Authentication;

public sealed record LoginRequest(
    [param: Required] string UserNameOrEmail,
    [param: Required] string Password);

public sealed record ChangePasswordRequest(
    [param: Required] string CurrentPassword,
    [param: Required] string NewPassword);

public sealed record StaffReference(
    Guid Id,
    string FullName,
    string? Position,
    string? Department);

public sealed record CurrentUserResponse(
    StaffReference Staff,
    IReadOnlyCollection<string> Roles,
    bool MustChangePassword);

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    bool MustChangePassword,
    StaffReference Staff,
    IReadOnlyCollection<string> Roles);
