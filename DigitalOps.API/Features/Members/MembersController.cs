using DigitalOps.API.Shared.Api;
using DigitalOps.API.Shared.Errors;
using DigitalOps.API.Shared.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DigitalOps.API.Features.Members;

[ApiController]
[Route("api/v1/members")]
[Authorize(Policy = AuthorizationPolicies.BusinessAccess)]
public sealed class MembersController(
    IMemberManagementService memberService,
    IMemberImportService memberImportService) : ControllerBase
{
    private const string MemberManagerRoles =
        SystemRoles.Administrator + "," + SystemRoles.Clerk;
    private const string MemberLookupRoles =
        MemberManagerRoles + "," + SystemRoles.Drafter;

    [HttpGet]
    [Authorize(Roles = MemberManagerRoles)]
    [ProducesResponseType<PagedResponse<MemberResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResponse<MemberResponse>>> GetList(
        [FromQuery] MemberListQuery query,
        CancellationToken cancellationToken) =>
        Ok(await memberService.GetListAsync(query, cancellationToken));

    [HttpGet("lookup")]
    [Authorize(Roles = MemberLookupRoles)]
    [ProducesResponseType<PagedResponse<MemberLookupResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResponse<MemberLookupResponse>>> GetLookup(
        [FromQuery] MemberLookupQuery query,
        CancellationToken cancellationToken) =>
        Ok(await memberService.GetLookupAsync(query, cancellationToken));

    [HttpGet("import-template")]
    [Authorize(Roles = MemberManagerRoles)]
    [Produces(MemberImportService.SpreadsheetContentType)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public IActionResult DownloadImportTemplate() =>
        File(
            memberImportService.CreateTemplate(),
            MemberImportService.SpreadsheetContentType,
            MemberImportService.TemplateFileName);

    [HttpPost("import")]
    [Authorize(Roles = MemberManagerRoles)]
    [RequestSizeLimit(MemberImportOptions.DefaultMaxFileSizeBytes + 1024 * 1024)]
    [RequestFormLimits(
        MultipartBodyLengthLimit = MemberImportOptions.DefaultMaxFileSizeBytes
            + 1024 * 1024)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<MemberImportResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<MemberImportProblemDetails>(
        StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MemberImportResult>> Import(
        [FromForm] MemberImportForm request,
        CancellationToken cancellationToken)
    {
        if (request.File is null || request.File.Length == 0)
        {
            ModelState.AddModelError("file", "Vui lòng chọn file XLSX có dữ liệu.");
            return ValidationProblem(ModelState);
        }

        await using var stream = request.File.OpenReadStream();
        var result = await memberImportService.ImportAsync(
            stream,
            request.File.FileName,
            request.File.Length,
            cancellationToken);
        return result.Succeeded
            ? Ok(result.Result)
            : ToImportActionResult(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = MemberManagerRoles)]
    [ProducesResponseType<MemberResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MemberResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var member = await memberService.GetByIdAsync(id, cancellationToken);
        return member is null
            ? Problem(
                detail: "Không tìm thấy hội viên.",
                statusCode: StatusCodes.Status404NotFound)
            : Ok(member);
    }

    [HttpPost]
    [Authorize(Roles = MemberManagerRoles)]
    [ProducesResponseType<MemberResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MemberResponse>> Create(
        MemberUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var result = await memberService.CreateAsync(request, cancellationToken);
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
    [Authorize(Roles = MemberManagerRoles)]
    [ProducesResponseType<MemberResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MemberResponse>> Update(
        Guid id,
        MemberUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var result = await memberService.UpdateAsync(
            id,
            request,
            cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToActionResult(result);
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Roles = MemberManagerRoles)]
    [ProducesResponseType<MemberResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MemberResponse>> Deactivate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await memberService.DeactivateAsync(id, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToActionResult(result);
    }

    private ActionResult ToActionResult(
        MemberServiceResult<MemberResponse> result)
    {
        if (result.Failure == MemberServiceFailure.Validation)
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
            MemberServiceFailure.NotFound => Problem(
                detail: "Không tìm thấy hội viên.",
                statusCode: StatusCodes.Status404NotFound),
            MemberServiceFailure.Conflict => Problem(
                detail: result.Detail,
                statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                "The Member service returned an unsupported failure.")
        };
    }

    private ActionResult ToImportActionResult(MemberImportServiceResult result)
    {
        if (result.Failure == MemberImportFailure.Validation)
        {
            var report = result.Result!;
            var problemDetails = new MemberImportProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Detail = result.Detail,
                ImportedCount = 0,
                TotalRows = report.TotalRows,
                Errors = report.Errors
            };
            ProblemDetailsDefaults.Apply(HttpContext, problemDetails);
            var response = new ObjectResult(problemDetails)
            {
                StatusCode = StatusCodes.Status422UnprocessableEntity
            };
            response.ContentTypes.Add("application/problem+json");
            return response;
        }

        return result.Failure switch
        {
            MemberImportFailure.PayloadTooLarge => Problem(
                detail: result.Detail,
                statusCode: StatusCodes.Status413PayloadTooLarge),
            MemberImportFailure.UnsupportedFileType => Problem(
                detail: result.Detail,
                statusCode: StatusCodes.Status415UnsupportedMediaType),
            _ => throw new InvalidOperationException(
                "The Member import service returned an unsupported failure.")
        };
    }
}
