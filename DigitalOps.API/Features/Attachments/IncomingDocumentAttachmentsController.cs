using DigitalOps.API.Shared.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DigitalOps.API.Features.Attachments;

[ApiController]
[Route("api/v1/incoming-documents/{incomingDocumentId:guid}/attachments")]
[Authorize(Policy = AuthorizationPolicies.Clerk)]
public sealed class IncomingDocumentAttachmentsController(
    IAttachmentService attachmentService) : ControllerBase
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    [AttachmentRequestSizeLimit]
    [ProducesResponseType<AttachmentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<AttachmentResponse>> Upload(
        Guid incomingDocumentId,
        [FromForm] AttachmentUploadForm request,
        CancellationToken cancellationToken)
    {
        if (request.File is null || request.File.Length == 0)
        {
            ModelState.AddModelError("file", "Vui lòng chọn file có dữ liệu.");
            return ValidationProblem(ModelState);
        }

        if (!CurrentStaffClaims.TryRead(User, out var claims))
        {
            return Problem(
                detail: "Không thể xác định tài khoản nhân sự hiện tại.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        await using var stream = request.File.OpenReadStream();
        var result = await attachmentService.UploadIncomingAsync(
            incomingDocumentId,
            claims.StaffId,
            stream,
            request.File.FileName,
            request.File.Length,
            cancellationToken);
        if (!result.Succeeded)
        {
            return ToActionResult(result);
        }

        return CreatedAtAction(
            nameof(AttachmentsController.Download),
            "Attachments",
            new { id = result.Value!.Id },
            result.Value);
    }

    private ActionResult ToActionResult(AttachmentResult<AttachmentResponse> result)
    {
        if (result.Failure == AttachmentFailure.Validation)
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
            AttachmentFailure.NotFound => Problem(
                detail: "Không tìm thấy văn bản đến.",
                statusCode: StatusCodes.Status404NotFound),
            AttachmentFailure.Conflict => Problem(
                detail: result.Detail,
                statusCode: StatusCodes.Status409Conflict),
            AttachmentFailure.PayloadTooLarge => Problem(
                detail: result.Detail,
                statusCode: StatusCodes.Status413PayloadTooLarge),
            AttachmentFailure.UnsupportedFileType => Problem(
                detail: result.Detail,
                statusCode: StatusCodes.Status415UnsupportedMediaType),
            AttachmentFailure.Storage => Problem(
                detail: result.Detail,
                statusCode: StatusCodes.Status500InternalServerError),
            _ => throw new InvalidOperationException(
                "The attachment service returned an unsupported failure.")
        };
    }
}
