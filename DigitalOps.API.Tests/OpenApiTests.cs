using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalOps.API.Tests;

public sealed class OpenApiTests(OpenApiApiFactory factory)
    : IClassFixture<OpenApiApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    [Fact]
    public async Task Development_exposes_swagger_ui_and_openapi_document()
    {
        var swaggerResponse = await _client.GetAsync("/swagger/index.html");
        var openApiResponse = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, swaggerResponse.StatusCode);
        Assert.Contains(
            "DigitalOps API",
            await swaggerResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, openApiResponse.StatusCode);
        Assert.Equal(
            "application/json",
            openApiResponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Openapi_describes_bearer_security_dtos_enums_and_problem_details()
    {
        using var document = JsonDocument.Parse(
            await _client.GetStringAsync("/openapi/v1.json"));
        var root = document.RootElement;

        Assert.Equal("DigitalOps API", root.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("v1", root.GetProperty("info").GetProperty("version").GetString());

        var bearer = root
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());

        var protectedOperation = root
            .GetProperty("paths")
            .GetProperty("/_test/authorization/business")
            .GetProperty("get");
        Assert.Equal(
            1,
            protectedOperation.GetProperty("security").GetArrayLength());
        Assert.True(
            protectedOperation
                .GetProperty("security")[0]
                .TryGetProperty("Bearer", out _));

        var anonymousOperation = root
            .GetProperty("paths")
            .GetProperty("/_test/errors/validation")
            .GetProperty("post");
        Assert.False(anonymousOperation.TryGetProperty("security", out _));

        var schemas = root.GetProperty("components").GetProperty("schemas");
        var requestSchema = schemas.GetProperty(nameof(ErrorProbeRequest));
        Assert.True(requestSchema.GetProperty("properties").TryGetProperty("displayName", out _));
        Assert.True(requestSchema.GetProperty("properties").TryGetProperty("status", out var status));
        Assert.Contains("Active", ResolveEnumValues(status, schemas));
        Assert.Contains("Inactive", ResolveEnumValues(status, schemas));

        var responses = anonymousOperation.GetProperty("responses");
        Assert.True(responses.TryGetProperty("400", out var validationResponse));
        Assert.Contains(
            nameof(ValidationProblemDetails),
            validationResponse.GetRawText());

        var paths = root.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/auth/login", out var loginPath));
        Assert.False(loginPath.GetProperty("post").TryGetProperty("security", out _));
        Assert.True(paths.TryGetProperty("/api/v1/auth/me", out var mePath));
        Assert.True(mePath.GetProperty("get").TryGetProperty("security", out _));
        Assert.True(
            paths.TryGetProperty(
                "/api/v1/auth/change-password",
                out var changePasswordPath));
        Assert.True(
            changePasswordPath.GetProperty("post").TryGetProperty("security", out _));

        Assert.True(schemas.TryGetProperty("LoginRequest", out _));
        Assert.True(schemas.TryGetProperty("LoginResponse", out _));
        Assert.True(schemas.TryGetProperty("CurrentUserResponse", out _));
        Assert.True(schemas.TryGetProperty("ChangePasswordRequest", out _));
    }

    private static IReadOnlyCollection<string> ResolveEnumValues(
        JsonElement schema,
        JsonElement schemas)
    {
        if (schema.TryGetProperty("enum", out var inlineValues))
        {
            return inlineValues
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray();
        }

        if (!schema.TryGetProperty("$ref", out var referenceElement))
        {
            throw new InvalidOperationException(
                $"Enum schema was not emitted as an enum or reference: {schema.GetRawText()}");
        }

        var reference = referenceElement.GetString()!;
        var schemaName = reference[(reference.LastIndexOf('/') + 1)..];
        var referencedSchema = schemas.GetProperty(schemaName);
        if (!referencedSchema.TryGetProperty("enum", out var referencedValues))
        {
            throw new InvalidOperationException(
                $"Referenced enum schema did not include values: {referencedSchema.GetRawText()}");
        }

        return referencedValues
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
    }
}

public sealed class OpenApiApiFactory : DigitalOpsApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services => services
            .AddControllers()
            .AddApplicationPart(typeof(ErrorProbeController).Assembly));
    }
}
