using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalOps.API.Tests;

public sealed class ProblemDetailsPipelineTests(ProblemDetailsApiFactory factory)
    : IClassFixture<ProblemDetailsApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    [Fact]
    public async Task Automatic_model_validation_returns_camel_case_validation_problem_details()
    {
        var response = await _client.PostAsJsonAsync(
            "/_test/errors/validation",
            new { status = ErrorProbeStatus.Active });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(
            "https://digitalops/errors/validation-error",
            root.GetProperty("type").GetString());
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.Equal(
            "/_test/errors/validation",
            root.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        Assert.True(root.GetProperty("errors").TryGetProperty("displayName", out var errors));
        Assert.NotEmpty(errors.EnumerateArray());
        Assert.False(root.GetProperty("errors").TryGetProperty("DisplayName", out _));
    }

    [Fact]
    public async Task Empty_controller_error_is_mapped_to_problem_details()
    {
        var response = await _client.GetAsync("/_test/errors/bad-request");

        await ProblemDetailsAssert.HasContractAsync(
            response,
            HttpStatusCode.BadRequest,
            "validation-error",
            "/_test/errors/bad-request");
    }

    [Fact]
    public async Task Missing_route_is_mapped_to_problem_details()
    {
        var response = await _client.GetAsync("/_test/missing");

        await ProblemDetailsAssert.HasContractAsync(
            response,
            HttpStatusCode.NotFound,
            "not-found",
            "/_test/missing");
    }

    [Fact]
    public async Task Explicit_business_problem_type_and_detail_are_preserved()
    {
        var response = await _client.GetAsync("/_test/errors/invalid-state");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.Equal(
            "https://digitalops/errors/invalid-state",
            root.GetProperty("type").GetString());
        Assert.Equal("State transition is not allowed.", root.GetProperty("title").GetString());
        Assert.Equal("The resource cannot move to the requested state.", root.GetProperty("detail").GetString());
        Assert.Equal("/_test/errors/invalid-state", root.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Explicit_title_is_preserved_when_default_type_is_used()
    {
        var response = await _client.GetAsync("/_test/errors/custom-title");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "https://digitalops/errors/conflict",
            root.GetProperty("type").GetString());
        Assert.Equal("A domain conflict occurred.", root.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Unhandled_exception_returns_safe_internal_server_error()
    {
        var response = await _client.GetAsync("/_test/errors/exception");
        var body = await response.Content.ReadAsStringAsync();

        await ProblemDetailsAssert.HasContractAsync(
            response,
            HttpStatusCode.InternalServerError,
            "internal-server-error",
            "/_test/errors/exception");
        Assert.DoesNotContain(ErrorProbeController.SensitiveExceptionMessage, body);
        Assert.DoesNotContain(nameof(InvalidOperationException), body);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ProblemDetailsApiFactory : DigitalOpsApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services => services
            .AddControllers()
            .AddApplicationPart(typeof(ErrorProbeController).Assembly));
    }
}

[ApiController]
[AllowAnonymous]
[Route("_test/errors")]
public sealed class ErrorProbeController : ControllerBase
{
    public const string SensitiveExceptionMessage =
        "sensitive-internal-exception-message";

    [HttpPost("validation")]
    [ProducesResponseType<ErrorProbeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public ActionResult<ErrorProbeResponse> Validate(ErrorProbeRequest request) =>
        Ok(new ErrorProbeResponse(request.DisplayName, request.Status));

    [HttpGet("bad-request")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public IActionResult BadRequestProbe() => BadRequest();

    [HttpGet("exception")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public IActionResult ExceptionProbe() =>
        throw new InvalidOperationException(SensitiveExceptionMessage);

    [HttpGet("invalid-state")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public IActionResult InvalidStateProbe() =>
        Problem(
            detail: "The resource cannot move to the requested state.",
            statusCode: StatusCodes.Status409Conflict,
            title: "State transition is not allowed.",
            type: "https://digitalops/errors/invalid-state");

    [HttpGet("custom-title")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public IActionResult CustomTitleProbe() =>
        Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "A domain conflict occurred.");
}

public sealed record ErrorProbeRequest(
    [Required] string DisplayName,
    ErrorProbeStatus Status);

public sealed record ErrorProbeResponse(string DisplayName, ErrorProbeStatus Status);

public enum ErrorProbeStatus
{
    Active,
    Inactive
}
