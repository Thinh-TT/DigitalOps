using DigitalOps.API.Shared.Api;
using DigitalOps.API.Shared.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DigitalOps.API.Features.OutgoingDocuments;

[ApiController]
[Route("api/v1/outgoing-documents")]
[Authorize(Policy = AuthorizationPolicies.BusinessAccess)]
public sealed class OutgoingDocumentsController(
    IOutgoingDocumentService outgoingDocumentService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResponse<OutgoingDocumentResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResponse<OutgoingDocumentResponse>>> GetList(
        [FromQuery] OutgoingDocumentListQuery query,
        CancellationToken cancellationToken) =>
        Ok(await outgoingDocumentService.GetListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<OutgoingDocumentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OutgoingDocumentResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var document = await outgoingDocumentService.GetByIdAsync(id, cancellationToken);
        return document is null
            ? Problem(
                detail: "Không tìm thấy văn bản đi.",
                statusCode: StatusCodes.Status404NotFound)
            : Ok(document);
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.Drafter)]
    [ProducesResponseType<OutgoingDocumentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OutgoingDocumentResponse>> Create(
        OutgoingDocumentCreateRequest request,
        CancellationToken cancellationToken)
    {
        if (!CurrentStaffClaims.TryRead(User, out var claims))
        {
            return Problem(
                detail: "Không thể xác định cán bộ soạn thảo hiện tại.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await outgoingDocumentService.CreateAsync(
            request,
            claims.StaffId,
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
    [Authorize(Policy = AuthorizationPolicies.Drafter)]
    [ProducesResponseType<OutgoingDocumentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OutgoingDocumentResponse>> Update(
        Guid id,
        OutgoingDocumentUpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (!CurrentStaffClaims.TryRead(User, out var claims))
        {
            return Problem(
                detail: "Không thể xác định cán bộ soạn thảo hiện tại.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await outgoingDocumentService.UpdateAsync(
            id,
            request,
            claims.StaffId,
            cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToActionResult(result);
    }

    [HttpPost("{id:guid}/ai-draft")]
    [Authorize(Policy = AuthorizationPolicies.Drafter)]
    [ProducesResponseType<OutgoingDocumentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OutgoingDocumentResponse>> GenerateAiDraft(
        Guid id,
        AiDraftRequest request,
        CancellationToken cancellationToken)
    {
        if (!CurrentStaffClaims.TryRead(User, out var claims))
        {
            return Problem(
                detail: "Không thể xác định cán bộ soạn thảo hiện tại.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await outgoingDocumentService.GenerateAiDraftAsync(
            id,
            request,
            claims.StaffId,
            cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToActionResult(result);
    }

    private ActionResult ToActionResult(
        OutgoingDocumentResult<OutgoingDocumentResponse> result)
    {
        if (result.Failure == OutgoingDocumentFailure.Validation)
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
            OutgoingDocumentFailure.NotFound => Problem(
                detail: "Không tìm thấy văn bản đi.",
                statusCode: StatusCodes.Status404NotFound),
            OutgoingDocumentFailure.Conflict => Problem(
                detail: result.Detail,
                statusCode: StatusCodes.Status409Conflict),
            OutgoingDocumentFailure.Forbidden => Problem(
                detail: result.Detail,
                statusCode: StatusCodes.Status403Forbidden),
            OutgoingDocumentFailure.ServiceUnavailable => Problem(
                detail: result.Detail,
                statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => throw new InvalidOperationException(
                "The outgoing document service returned an unsupported failure.")
        };
    }
}
