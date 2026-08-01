using DigitalOps.API.Shared.Api;
using DigitalOps.API.Shared.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DigitalOps.API.Features.Reminders;

[ApiController]
[Route("api/v1/reminders")]
[Authorize(Policy = AuthorizationPolicies.BusinessAccess)]
public sealed class RemindersController(IReminderService reminderService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResponse<ReminderResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResponse<ReminderResponse>>> GetList(
        [FromQuery] ReminderListQuery query,
        CancellationToken cancellationToken)
    {
        if (!CurrentStaffClaims.TryRead(User, out var claims))
        {
            return Problem(
                detail: "Không thể xác định tài khoản nhân sự hiện tại.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await reminderService.GetListAsync(
            query,
            claims.StaffId,
            User.IsInRole(SystemRoles.Administrator),
            cancellationToken);
        return result.Failure == ReminderServiceFailure.Forbidden
            ? Problem(
                detail: "Bạn không được phép xem thông báo của Staff khác.",
                statusCode: StatusCodes.Status403Forbidden)
            : Ok(result.Value);
    }

    [HttpPost("{id:guid}/read")]
    [ProducesResponseType<ReminderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReminderResponse>> MarkRead(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!CurrentStaffClaims.TryRead(User, out var claims))
        {
            return Problem(
                detail: "Không thể xác định tài khoản nhân sự hiện tại.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await reminderService.MarkReadAsync(
            id,
            claims.StaffId,
            User.IsInRole(SystemRoles.Administrator),
            cancellationToken);
        return result.Failure switch
        {
            ReminderServiceFailure.NotFound => Problem(
                detail: "Không tìm thấy thông báo.",
                statusCode: StatusCodes.Status404NotFound),
            ReminderServiceFailure.Forbidden => Problem(
                detail: "Bạn không được phép thao tác trên thông báo này.",
                statusCode: StatusCodes.Status403Forbidden),
            _ => Ok(result.Value)
        };
    }
}
