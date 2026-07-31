using DigitalOps.API.Shared.Api;
using DigitalOps.API.Shared.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DigitalOps.API.Features.IncomingDocuments;

[ApiController]
[Route("api/v1/incoming-documents")]
[Authorize(Policy = AuthorizationPolicies.BusinessAccess)]
public sealed class IncomingDocumentsController(
    IIncomingDocumentService incomingDocumentService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResponse<IncomingDocumentResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResponse<IncomingDocumentResponse>>> GetList(
        [FromQuery] IncomingDocumentListQuery query,
        CancellationToken cancellationToken) =>
        Ok(await incomingDocumentService.GetListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<IncomingDocumentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IncomingDocumentResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var document = await incomingDocumentService.GetByIdAsync(
            id,
            cancellationToken);
        return document is null
            ? Problem(
                detail: "Không tìm thấy văn bản đến.",
                statusCode: StatusCodes.Status404NotFound)
            : Ok(document);
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.Clerk)]
    [ProducesResponseType<IncomingDocumentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IncomingDocumentResponse>> Create(
        IncomingDocumentCreateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await incomingDocumentService.CreateAsync(
            request,
            cancellationToken);
        if (!result.Succeeded)
        {
            return ToActionResult(result);
        }

        return CreatedAtAction(
            nameof(GetById),
            new { id = result.Value!.Id },
            result.Value);
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Clerk)]
    [ProducesResponseType<IncomingDocumentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IncomingDocumentResponse>> Update(
        Guid id,
        IncomingDocumentUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await incomingDocumentService.UpdateAsync(
            id,
            request,
            cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToActionResult(result);
    }

    [HttpPost("{id:guid}/complete")]
    [ProducesResponseType<IncomingDocumentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IncomingDocumentResponse>> Complete(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!CurrentStaffClaims.TryRead(User, out var claims))
        {
            return Problem(
                detail: "Không thể xác định tài khoản nhân sự hiện tại.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await incomingDocumentService.CompleteAsync(
            id,
            claims.StaffId,
            User.IsInRole(SystemRoles.Clerk),
            cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToActionResult(result);
    }

    private ActionResult ToActionResult<T>(IncomingDocumentResult<T> result)
    {
        if (result.Failure == IncomingDocumentFailure.Validation)
        {
            foreach (var (field, messages) in result.Errors)
            {
                foreach (var message in messages)
                {
                    ModelState.AddModelError(field, message);
                }
            }

            return ValidationProblem(ModelState);
        }

        return result.Failure switch
        {
            IncomingDocumentFailure.NotFound => Problem(
                detail: "Không tìm thấy văn bản đến.",
                statusCode: StatusCodes.Status404NotFound),
            IncomingDocumentFailure.Conflict => Problem(
                detail: result.Detail,
                statusCode: StatusCodes.Status409Conflict),
            IncomingDocumentFailure.Forbidden => Problem(
                detail: "Bạn không được phép hoàn tất văn bản đến này.",
                statusCode: StatusCodes.Status403Forbidden),
            _ => throw new InvalidOperationException(
                "The incoming document service returned an unsupported failure.")
        };
    }
}
