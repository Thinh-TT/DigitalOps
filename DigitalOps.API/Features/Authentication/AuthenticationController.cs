using DigitalOps.API.Shared.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DigitalOps.API.Features.Authentication;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthenticationController(
    IAuthenticationService authenticationService) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<LoginResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var response = await authenticationService.LoginAsync(request, cancellationToken);

        return response is null
            ? Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                detail: "Tên đăng nhập/email hoặc mật khẩu không đúng.")
            : Ok(response);
    }

    [HttpGet("me")]
    [Authorize(Policy = AuthorizationPolicies.CurrentStaff)]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CurrentUserResponse>> Me(
        CancellationToken cancellationToken)
    {
        if (!CurrentStaffClaims.TryRead(User, out var claims))
        {
            return Forbid();
        }

        var response = await authenticationService.GetCurrentUserAsync(
            claims.IdentityUserId,
            claims.StaffId,
            User.FindAll(JwtClaimNames.Role)
                .Select(claim => claim.Value)
                .ToArray(),
            cancellationToken);

        return response is null ? Forbid() : Ok(response);
    }

    [HttpPost("change-password")]
    [Authorize(Policy = AuthorizationPolicies.CurrentStaff)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<LoginResponse>> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (!CurrentStaffClaims.TryRead(User, out var claims))
        {
            return Forbid();
        }

        var result = await authenticationService.ChangePasswordAsync(
            claims.IdentityUserId,
            claims.StaffId,
            request,
            cancellationToken);

        if (result.IsForbidden)
        {
            return Forbid();
        }

        if (result.Succeeded)
        {
            return Ok(result.Response);
        }

        foreach (var (field, errors) in result.Errors)
        {
            foreach (var error in errors)
            {
                ModelState.AddModelError(field, error);
            }
        }

        return ValidationProblem(ModelState);
    }
}
