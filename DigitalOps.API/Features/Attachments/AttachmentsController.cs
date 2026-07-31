using DigitalOps.API.Shared.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DigitalOps.API.Features.Attachments;

[ApiController]
[Route("api/v1/attachments")]
[Authorize(Policy = AuthorizationPolicies.BusinessAccess)]
public sealed class AttachmentsController(
    IAttachmentService attachmentService) : ControllerBase
{
    [HttpGet("{id:guid}/download")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Download(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await attachmentService.DownloadAsync(id, cancellationToken);
        if (!result.Succeeded)
        {
            return ToActionResult(result.Failure, result.Detail);
        }

        var download = result.Value!;
        return File(download.Content, download.ContentType, download.FileName);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Clerk)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await attachmentService.DeleteIncomingAsync(
            id,
            cancellationToken);
        return result.Succeeded
            ? NoContent()
            : ToActionResult(result.Failure, result.Detail);
    }

    private ObjectResult ToActionResult(
        AttachmentFailure failure,
        string? detail) =>
        failure switch
        {
            AttachmentFailure.NotFound => Problem(
                detail: "Không tìm thấy file đính kèm.",
                statusCode: StatusCodes.Status404NotFound),
            AttachmentFailure.Conflict => Problem(
                detail: detail,
                statusCode: StatusCodes.Status409Conflict),
            AttachmentFailure.Storage => Problem(
                detail: detail,
                statusCode: StatusCodes.Status500InternalServerError),
            _ => throw new InvalidOperationException(
                "The attachment service returned an unsupported failure.")
        };
}
