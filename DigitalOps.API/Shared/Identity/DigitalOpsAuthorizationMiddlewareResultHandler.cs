using DigitalOps.API.Shared.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;

namespace DigitalOps.API.Shared.Identity;

public sealed class DigitalOpsAuthorizationMiddlewareResultHandler
    : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden
            && authorizeResult.AuthorizationFailure?.FailureReasons.Any(
                reason => string.Equals(
                    reason.Message,
                    CurrentStaffAccessHandler.PasswordChangeRequiredFailureReason,
                    StringComparison.Ordinal)) == true)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            var definition = ProblemDetailsCatalog.PasswordChangeRequired;
            var problemDetailsService =
                context.RequestServices.GetRequiredService<IProblemDetailsService>();

            await problemDetailsService.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Type = definition.Type,
                    Title = definition.Title,
                    Detail = "Bạn phải đổi mật khẩu tạm trước khi sử dụng chức năng nghiệp vụ."
                }
            });
            return;
        }

        await _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
