using DigitalOps.API.Shared.Api;
using DigitalOps.API.Shared.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DigitalOps.API.Features.Search;

[ApiController]
[Route("api/v1/documents")]
[Authorize(Policy = AuthorizationPolicies.BusinessAccess)]
public sealed class DocumentsSearchController(
    IDocumentSearchService documentSearchService) : ControllerBase
{
    [HttpGet("search")]
    [ProducesResponseType<PagedResponse<DocumentSearchResult>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResponse<DocumentSearchResult>>> Search(
        [FromQuery] DocumentSearchQuery query,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await documentSearchService.SearchAsync(query, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(ex.ParamName ?? nameof(query.Q), ex.Message);
            return ValidationProblem(ModelState);
        }
    }
}
