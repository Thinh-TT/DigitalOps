using DigitalOps.API.Shared.Api;
using DigitalOps.API.Shared.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DigitalOps.API.Features.Review;

[ApiController]
[Route("api/v1/outgoing-documents/{outgoingDocumentId:guid}/reviews")]
[Authorize(Policy = AuthorizationPolicies.BusinessAccess)]
public sealed class OutgoingDocumentReviewsController(
    IOutgoingDocumentReviewService reviewService) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.Drafter)]
    [ProducesResponseType<ReviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ReviewResponse>> Create(
        Guid outgoingDocumentId,
        CancellationToken cancellationToken)
    {
        if (!CurrentStaffClaims.TryRead(User, out var claims))
        {
            return Problem(
                detail: "Không thể xác định cán bộ soạn thảo hiện tại.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await reviewService.CreateAsync(
            outgoingDocumentId,
            claims.StaffId,
            cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToActionResult(result);
    }

    [HttpGet]
    [ProducesResponseType<PagedResponse<ReviewResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<ReviewResponse>>> GetList(
        Guid outgoingDocumentId,
        [FromQuery] ReviewListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await reviewService.GetListAsync(
            outgoingDocumentId,
            query,
            cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToActionResult(result);
    }

    private ActionResult ToActionResult<T>(ReviewOperationResult<T> result) =>
        result.Failure switch
        {
            ReviewOperationFailure.NotFound => Problem(
                detail: "Không tìm thấy văn bản đi.",
                statusCode: StatusCodes.Status404NotFound),
            ReviewOperationFailure.Forbidden => Problem(
                detail: result.Detail,
                statusCode: StatusCodes.Status403Forbidden),
            ReviewOperationFailure.Conflict => Problem(
                detail: result.Detail,
                statusCode: StatusCodes.Status409Conflict),
            ReviewOperationFailure.ServiceUnavailable => Problem(
                detail: result.Detail,
                statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => throw new InvalidOperationException(
                "The review service returned an unsupported failure.")
        };
}
