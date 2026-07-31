using DigitalOps.API.Shared.Api;
using DigitalOps.API.Shared.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DigitalOps.API.Features.StaffManagement;

[ApiController]
[Route("api/v1/staff")]
public sealed class StaffController(
    IStaffManagementService staffService) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.BusinessAccess)]
    [ProducesResponseType<PagedResponse<StaffResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResponse<StaffResponse>>> GetList(
        [FromQuery] StaffListQuery query,
        CancellationToken cancellationToken)
    {
        var isAdministrator = User.IsInRole(SystemRoles.Administrator);
        var isActiveDirectoryClerk =
            User.IsInRole(SystemRoles.Clerk) && query.ActiveOnly == true;

        if (!isAdministrator && !isActiveDirectoryClerk)
        {
            return Forbid();
        }

        return Ok(await staffService.GetListAsync(query, cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    [ProducesResponseType<StaffResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StaffResponse>> Create(
        StaffCreateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await staffService.CreateAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            return ToActionResult(result);
        }

        return CreatedAtAction(
            nameof(GetById),
            new { id = result.Value!.Id },
            result.Value);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    [ProducesResponseType<StaffResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StaffResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var staff = await staffService.GetByIdAsync(id, cancellationToken);
        return staff is null
            ? Problem(
                detail: "Không tìm thấy Staff.",
                statusCode: StatusCodes.Status404NotFound)
            : Ok(staff);
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    [ProducesResponseType<StaffResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StaffResponse>> Update(
        Guid id,
        StaffUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await staffService.UpdateAsync(id, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToActionResult(result);
    }

    [HttpPut("{id:guid}/roles")]
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    [ProducesResponseType<StaffResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StaffResponse>> ReplaceRoles(
        Guid id,
        RoleAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await staffService.ReplaceRolesAsync(
            id,
            request,
            cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToActionResult(result);
    }

    [HttpPost("{id:guid}/reset-password")]
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(
        Guid id,
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await staffService.ResetPasswordAsync(
            id,
            request,
            cancellationToken);
        return result.Succeeded ? NoContent() : ToActionResult(result);
    }

    private ActionResult ToActionResult(StaffServiceResult result)
    {
        if (result.Failure == StaffServiceFailure.Validation)
        {
            foreach (var (field, errors) in result.Errors)
            {
                foreach (var error in errors)
                {
                    ModelState.AddModelError(field, error);
                }
            }

            return ValidationProblem(ModelState);
        }

        return result.Failure switch
        {
            StaffServiceFailure.NotFound => Problem(
                detail: "Không tìm thấy Staff.",
                statusCode: StatusCodes.Status404NotFound),
            StaffServiceFailure.Conflict => Problem(
                detail: result.Detail,
                statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                "The Staff service returned an unsupported failure.")
        };
    }
}
