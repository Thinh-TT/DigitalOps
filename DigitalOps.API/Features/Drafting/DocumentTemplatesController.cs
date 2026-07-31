using DigitalOps.API.Shared.Api;
using DigitalOps.API.Shared.Errors;
using DigitalOps.API.Shared.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DigitalOps.API.Features.Drafting;

[ApiController]
[Route("api/v1/document-templates")]
[Authorize(Policy = AuthorizationPolicies.BusinessAccess)]
public sealed class DocumentTemplatesController(
    IDocumentCatalogService catalogService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResponse<DocumentTemplateResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResponse<DocumentTemplateResponse>>> GetList(
        [FromQuery] DocumentTemplateListQuery query,
        CancellationToken cancellationToken) =>
        Ok(await catalogService.GetDocumentTemplatesAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<DocumentTemplateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentTemplateResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var template = await catalogService.GetDocumentTemplateAsync(
            id,
            cancellationToken);
        return template is null
            ? Problem(
                detail: "Không tìm thấy mẫu văn bản.",
                statusCode: StatusCodes.Status404NotFound)
            : Ok(template);
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    [ProducesResponseType<DocumentTemplateResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DocumentTemplateResponse>> Create(
        DocumentTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await catalogService.CreateDocumentTemplateAsync(
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
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    [ProducesResponseType<DocumentTemplateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DocumentTemplateResponse>> Update(
        Guid id,
        DocumentTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await catalogService.UpdateDocumentTemplateAsync(
            id,
            request,
            cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToActionResult(result);
    }

    private ActionResult ToActionResult(
        DocumentCatalogResult<DocumentTemplateResponse> result)
    {
        if (result.Failure == DocumentCatalogFailure.Validation)
        {
            AddErrors(result.Errors);
            return ValidationProblem(ModelState);
        }

        if (result.Failure == DocumentCatalogFailure.FormatRulesValidation)
        {
            var problemDetails = new ValidationProblemDetails(
                result.Errors.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal))
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Detail = "FormatRules không đúng cấu trúc bắt buộc."
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
            DocumentCatalogFailure.NotFound => Problem(
                detail: "Không tìm thấy mẫu văn bản.",
                statusCode: StatusCodes.Status404NotFound),
            DocumentCatalogFailure.Conflict => Problem(
                detail: result.Detail,
                statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException(
                "The document catalog service returned an unsupported failure.")
        };
    }

    private void AddErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (var (field, messages) in errors)
        {
            foreach (var message in messages)
            {
                ModelState.AddModelError(field, message);
            }
        }
    }
}
