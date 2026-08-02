using DigitalOps.API.Features.OutgoingDocuments;
using DigitalOps.API.Shared.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DigitalOps.API.Features.Approval;

[ApiController]
[Route("api/v1/outgoing-documents/{outgoingDocumentId:guid}/approval")]
[Authorize(Policy = AuthorizationPolicies.Leader)]
public sealed class OutgoingDocumentApprovalController(
    IOutgoingDocumentApprovalService approvalService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<OutgoingDocumentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OutgoingDocumentResponse>> Decide(
        Guid outgoingDocumentId,
        ApprovalDecisionRequest request,
        CancellationToken cancellationToken)
    {
        if (!CurrentStaffClaims.TryRead(User, out var claims))
        {
            return Problem(
                detail: "Không thể xác định lãnh đạo hiện tại.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await approvalService.DecideAsync(
            outgoingDocumentId,
            request,
            claims.StaffId,
            cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToActionResult(result);
    }

    private ActionResult ToActionResult(
        ApprovalOperationResult<OutgoingDocumentResponse> result)
    {
        if (result.Failure == ApprovalOperationFailure.Validation)
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
            ApprovalOperationFailure.NotFound => Problem(
                detail: "Không tìm thấy văn bản đi.",
                statusCode: StatusCodes.Status404NotFound),
            ApprovalOperationFailure.Conflict => Problem(
                detail: result.Detail,
                statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                "The approval service returned an unsupported failure.")
        };
    }
}
