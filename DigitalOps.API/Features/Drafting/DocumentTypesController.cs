using DigitalOps.API.Shared.Api;
using DigitalOps.API.Shared.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DigitalOps.API.Features.Drafting;

[ApiController]
[Route("api/v1/document-types")]
[Authorize(Policy = AuthorizationPolicies.BusinessAccess)]
public sealed class DocumentTypesController(
    IDocumentCatalogService catalogService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResponse<DocumentTypeResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResponse<DocumentTypeResponse>>> GetList(
        [FromQuery] DocumentTypeListQuery query,
        CancellationToken cancellationToken) =>
        Ok(await catalogService.GetDocumentTypesAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<DocumentTypeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentTypeResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var documentType = await catalogService.GetDocumentTypeAsync(
            id,
            cancellationToken);
        return documentType is null
            ? Problem(
                detail: "Không tìm thấy loại văn bản.",
                statusCode: StatusCodes.Status404NotFound)
            : Ok(documentType);
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    [ProducesResponseType<DocumentTypeResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DocumentTypeResponse>> Create(
        DocumentTypeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await catalogService.CreateDocumentTypeAsync(
            request,
            cancellationToken);
        if (!result.Succeeded)
        {
            return ToActionResult(result, "Không tìm thấy loại văn bản.");
        }

        return CreatedAtAction(
            nameof(GetById),
            new { id = result.Value!.Id },
            result.Value);
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    [ProducesResponseType<DocumentTypeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DocumentTypeResponse>> Update(
        Guid id,
        DocumentTypeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await catalogService.UpdateDocumentTypeAsync(
            id,
            request,
            cancellationToken);
        return result.Succeeded
            ? Ok(result.Value)
            : ToActionResult(result, "Không tìm thấy loại văn bản.");
    }

    private ActionResult ToActionResult<T>(
        DocumentCatalogResult<T> result,
        string notFoundDetail)
    {
        if (result.Failure == DocumentCatalogFailure.Validation)
        {
            AddErrors(result.Errors);
            return ValidationProblem(ModelState);
        }

        return result.Failure switch
        {
            DocumentCatalogFailure.NotFound => Problem(
                detail: notFoundDetail,
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
