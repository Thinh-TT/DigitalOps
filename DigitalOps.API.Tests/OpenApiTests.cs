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

        Assert.True(paths.TryGetProperty("/api/v1/staff", out var staffPath));
        Assert.True(staffPath.GetProperty("get").TryGetProperty("security", out _));
        Assert.True(staffPath.GetProperty("post").TryGetProperty("security", out _));
        var staffQueryParameters = staffPath
            .GetProperty("get")
            .GetProperty("parameters")
            .EnumerateArray()
            .Select(parameter => parameter.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains("activeOnly", staffQueryParameters);
        Assert.Contains("page", staffQueryParameters);
        Assert.Contains("pageSize", staffQueryParameters);
        Assert.True(
            paths.TryGetProperty("/api/v1/staff/{id}", out var staffDetailPath));
        Assert.True(staffDetailPath.TryGetProperty("get", out _));
        Assert.True(staffDetailPath.TryGetProperty("patch", out _));
        Assert.True(
            paths.TryGetProperty(
                "/api/v1/staff/{id}/roles",
                out var staffRolesPath));
        Assert.True(staffRolesPath.TryGetProperty("put", out _));
        Assert.True(
            paths.TryGetProperty(
                "/api/v1/staff/{id}/reset-password",
                out var resetPasswordPath));
        Assert.True(
            resetPasswordPath
                .GetProperty("post")
                .GetProperty("responses")
                .TryGetProperty("204", out _));

        Assert.True(schemas.TryGetProperty("StaffCreateRequest", out _));
        Assert.True(schemas.TryGetProperty("StaffUpdateRequest", out _));
        Assert.True(schemas.TryGetProperty("RoleAssignmentRequest", out _));
        Assert.True(schemas.TryGetProperty("ResetPasswordRequest", out _));
        Assert.True(schemas.TryGetProperty("StaffResponse", out _));

        Assert.True(paths.TryGetProperty("/api/v1/members", out var membersPath));
        Assert.True(membersPath.GetProperty("get").TryGetProperty("security", out _));
        Assert.True(membersPath.GetProperty("post").TryGetProperty("security", out _));
        var memberQueryParameters = membersPath
            .GetProperty("get")
            .GetProperty("parameters")
            .EnumerateArray()
            .Select(parameter => parameter.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains("q", memberQueryParameters);
        Assert.Contains("status", memberQueryParameters);
        Assert.Contains("page", memberQueryParameters);
        Assert.Contains("pageSize", memberQueryParameters);
        Assert.True(
            paths.TryGetProperty("/api/v1/members/lookup", out var memberLookupPath));
        Assert.True(memberLookupPath.TryGetProperty("get", out _));
        Assert.True(
            paths.TryGetProperty("/api/v1/members/{id}", out var memberDetailPath));
        Assert.True(memberDetailPath.TryGetProperty("get", out _));
        Assert.True(memberDetailPath.TryGetProperty("patch", out _));
        Assert.True(
            paths.TryGetProperty(
                "/api/v1/members/{id}/deactivate",
                out var memberDeactivatePath));
        Assert.True(
            memberDeactivatePath
                .GetProperty("post")
                .GetProperty("responses")
                .TryGetProperty("409", out _));
        Assert.True(
            paths.TryGetProperty(
                "/api/v1/members/import-template",
                out var memberImportTemplatePath));
        Assert.True(
            memberImportTemplatePath
                .GetProperty("get")
                .GetProperty("responses")
                .GetProperty("200")
                .GetProperty("content")
                .TryGetProperty(
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    out _));
        Assert.True(
            paths.TryGetProperty(
                "/api/v1/members/import",
                out var memberImportPath));
        var memberImportOperation = memberImportPath.GetProperty("post");
        Assert.True(
            memberImportOperation
                .GetProperty("requestBody")
                .GetProperty("content")
                .TryGetProperty("multipart/form-data", out _));
        var memberImportResponses = memberImportOperation.GetProperty("responses");
        foreach (var statusCode in new[] { "200", "400", "413", "415", "422" })
        {
            Assert.True(memberImportResponses.TryGetProperty(statusCode, out _));
        }
        Assert.True(schemas.TryGetProperty("MemberUpsertRequest", out _));
        Assert.True(schemas.TryGetProperty("MemberResponse", out _));
        Assert.True(schemas.TryGetProperty("MemberLookupResponse", out _));
        Assert.True(schemas.TryGetProperty("MemberImportResult", out _));
        Assert.True(schemas.TryGetProperty("MemberImportRowError", out _));
        Assert.True(schemas.TryGetProperty("MemberImportProblemDetails", out _));
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
