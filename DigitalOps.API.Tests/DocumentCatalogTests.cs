using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalOps.API.Features.Authentication;
using DigitalOps.API.Features.Drafting;
using DigitalOps.API.Shared.Api;
using DigitalOps.API.Shared.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalOps.API.Tests;

public sealed class DocumentCatalogServiceTests
{
    [Fact]
    public async Task Document_type_crud_trims_values_filters_and_preserves_omitted_fields()
    {
        await using var database = await DocumentCatalogDatabase.CreateAsync();
        var service = new DocumentCatalogService(database.Context);

        var created = await service.CreateDocumentTypeAsync(
            new DocumentTypeRequest
            {
                Code = "  REPORT  ",
                Name = "  Báo cáo  ",
                Description = "  Dùng cho báo cáo  "
            });

        Assert.True(created.Succeeded);
        Assert.Equal("REPORT", created.Value!.Code);
        Assert.Equal("Báo cáo", created.Value.Name);
        Assert.Equal("Dùng cho báo cáo", created.Value.Description);
        Assert.True(created.Value.IsActive);

        var patched = await service.UpdateDocumentTypeAsync(
            created.Value.Id,
            new DocumentTypeRequest
            {
                Description = null,
                IsActive = false
            });

        Assert.True(patched.Succeeded);
        Assert.Equal("REPORT", patched.Value!.Code);
        Assert.Equal("Báo cáo", patched.Value.Name);
        Assert.Null(patched.Value.Description);
        Assert.False(patched.Value.IsActive);

        var all = await service.GetDocumentTypesAsync(
            new DocumentTypeListQuery { Page = 1, PageSize = 20 });
        var active = await service.GetDocumentTypesAsync(
            new DocumentTypeListQuery
            {
                ActiveOnly = true,
                Page = 1,
                PageSize = 20
            });
        Assert.Single(all.Items);
        Assert.Empty(active.Items);
    }

    [Fact]
    public async Task Document_type_rejects_missing_fields_and_duplicate_code()
    {
        await using var database = await DocumentCatalogDatabase.CreateAsync();
        var service = new DocumentCatalogService(database.Context);

        var invalid = await service.CreateDocumentTypeAsync(new DocumentTypeRequest());
        Assert.Equal(DocumentCatalogFailure.Validation, invalid.Failure);
        Assert.Contains("code", invalid.Errors.Keys);
        Assert.Contains("name", invalid.Errors.Keys);

        Assert.True((await service.CreateDocumentTypeAsync(
            new DocumentTypeRequest { Code = "PLAN", Name = "Kế hoạch" })).Succeeded);
        var duplicate = await service.CreateDocumentTypeAsync(
            new DocumentTypeRequest { Code = "PLAN", Name = "Kế hoạch khác" });
        Assert.Equal(DocumentCatalogFailure.Conflict, duplicate.Failure);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"version\":0,\"rules\":[]}")]
    [InlineData("{\"version\":1,\"rules\":{}}")]
    [InlineData("{\"version\":1,\"rules\":[{\"code\":\"header\"}]}")]
    [InlineData("{\"version\":1,\"rules\":[{\"code\":\"header\",\"required\":true},{\"code\":\" header \",\"required\":false}]}")]
    public async Task Format_rules_invalid_structures_are_rejected(string json)
    {
        await using var database = await DocumentCatalogDatabase.CreateAsync();
        var service = new DocumentCatalogService(database.Context);

        var result = await service.CreateDocumentTemplateAsync(
            CreateTemplateRequest(Guid.NewGuid(), json));

        Assert.Equal(DocumentCatalogFailure.FormatRulesValidation, result.Failure);
        Assert.Contains("formatRules", result.Errors.Keys);
    }

    [Fact]
    public async Task Template_accepts_extensions_and_enforces_parent_activity_and_unique_name()
    {
        await using var database = await DocumentCatalogDatabase.CreateAsync();
        var service = new DocumentCatalogService(database.Context);
        var activeType = await CreateTypeAsync(service, "DECISION", "Quyết định");
        var inactiveType = await CreateTypeAsync(service, "NOTICE", "Thông báo");
        await service.UpdateDocumentTypeAsync(
            inactiveType.Id,
            new DocumentTypeRequest { IsActive = false });

        var invalidParent = await service.CreateDocumentTemplateAsync(
            CreateTemplateRequest(inactiveType.Id, ValidFormatRules));
        Assert.Equal(DocumentCatalogFailure.Validation, invalidParent.Failure);
        Assert.Contains("documentTypeId", invalidParent.Errors.Keys);

        var request = CreateTemplateRequest(
            activeType.Id,
            """
            {
              "version": 1,
              "source": "administrative",
              "rules": [
                { "code": "header", "required": true, "severity": "Error" }
              ]
            }
            """);
        var created = await service.CreateDocumentTemplateAsync(request);
        Assert.True(created.Succeeded);
        Assert.Equal("administrative", created.Value!.FormatRules.GetProperty("source").GetString());

        var duplicate = await service.CreateDocumentTemplateAsync(
            CreateTemplateRequest(activeType.Id, ValidFormatRules));
        Assert.Equal(DocumentCatalogFailure.Conflict, duplicate.Failure);
    }

    [Fact]
    public async Task Template_patch_and_active_only_respect_inactive_parent_without_cascade()
    {
        await using var database = await DocumentCatalogDatabase.CreateAsync();
        var service = new DocumentCatalogService(database.Context);
        var parent = await CreateTypeAsync(service, "REPORT", "Báo cáo");
        var created = await service.CreateDocumentTemplateAsync(
            CreateTemplateRequest(parent.Id, ValidFormatRules));
        Assert.True(created.Succeeded);

        await service.UpdateDocumentTemplateAsync(
            created.Value!.Id,
            new DocumentTemplateRequest { IsActive = false });
        await service.UpdateDocumentTypeAsync(
            parent.Id,
            new DocumentTypeRequest { IsActive = false });

        var contentPatch = await service.UpdateDocumentTemplateAsync(
            created.Value.Id,
            new DocumentTemplateRequest { TemplateContent = "  Nội dung mới  " });
        Assert.True(contentPatch.Succeeded);
        Assert.Equal("Nội dung mới", contentPatch.Value!.TemplateContent);

        var activation = await service.UpdateDocumentTemplateAsync(
            created.Value.Id,
            new DocumentTemplateRequest { IsActive = true });
        Assert.Equal(DocumentCatalogFailure.Validation, activation.Failure);

        var activeOnly = await service.GetDocumentTemplatesAsync(
            new DocumentTemplateListQuery
            {
                ActiveOnly = true,
                Page = 1,
                PageSize = 20
            });
        var all = await service.GetDocumentTemplatesAsync(
            new DocumentTemplateListQuery { Page = 1, PageSize = 20 });
        Assert.Empty(activeOnly.Items);
        Assert.Single(all.Items);
        Assert.Equal("REPORT", all.Items[0].DocumentType.Code);
    }

    private const string ValidFormatRules =
        "{\"version\":1,\"rules\":[{\"code\":\"header\",\"required\":true}]}";

    private static DocumentTemplateRequest CreateTemplateRequest(Guid typeId, string json) =>
        new()
        {
            DocumentTypeId = typeId,
            Name = "Mẫu chuẩn",
            TemplateContent = "Nội dung {{member.fullName}}",
            FormatRules = JsonDocument.Parse(json).RootElement.Clone()
        };

    private static async Task<DocumentTypeResponse> CreateTypeAsync(
        DocumentCatalogService service,
        string code,
        string name)
    {
        var result = await service.CreateDocumentTypeAsync(
            new DocumentTypeRequest { Code = code, Name = name });
        Assert.True(result.Succeeded);
        return result.Value!;
    }

    private sealed class DocumentCatalogDatabase : IAsyncDisposable
    {
        private DocumentCatalogDatabase(
            SqliteConnection connection,
            DigitalOpsDbContext context)
        {
            Connection = connection;
            Context = context;
        }

        public SqliteConnection Connection { get; }

        public DigitalOpsDbContext Context { get; }

        public static async Task<DocumentCatalogDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<DigitalOpsDbContext>()
                .UseSqlite(connection)
                .ReplaceService<IModelCustomizer, AuthenticationTestModelCustomizer>()
                .Options;
            var context = new DigitalOpsDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new DocumentCatalogDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}

public sealed class DocumentCatalogApiTests
{
    private const string Password = "Valid1!Password";

    [Fact]
    public async Task Endpoints_apply_business_access_and_administrator_boundaries()
    {
        using var factory = new StaffManagementApiFactory();
        using var anonymous = factory.CreateApiClient();
        await ProblemDetailsAssert.HasContractAsync(
            await anonymous.GetAsync("/api/v1/document-types"),
            HttpStatusCode.Unauthorized,
            "unauthorized",
            "/api/v1/document-types");

        using var clerk = factory.CreateApiClient();
        await AuthenticateAsync(clerk, "clerk");
        Assert.Equal(
            HttpStatusCode.OK,
            (await clerk.GetAsync("/api/v1/document-types")).StatusCode);
        await ProblemDetailsAssert.HasContractAsync(
            await clerk.PostAsJsonAsync(
                "/api/v1/document-types",
                new { code = "REPORT", name = "Báo cáo" }),
            HttpStatusCode.Forbidden,
            "forbidden",
            "/api/v1/document-types");

        using var forced = factory.CreateApiClient();
        await AuthenticateAsync(forced, "forcedadmin");
        await ProblemDetailsAssert.HasContractAsync(
            await forced.GetAsync("/api/v1/document-types"),
            HttpStatusCode.Forbidden,
            "password-change-required",
            "/api/v1/document-types");
    }

    [Fact]
    public async Task Administrator_can_crud_catalog_and_receives_expected_errors()
    {
        using var factory = new StaffManagementApiFactory();
        using var client = factory.CreateApiClient();
        await AuthenticateAsync(client, "admin");

        var typeResponse = await client.PostAsJsonAsync(
            "/api/v1/document-types",
            new { code = "DECISION", name = "Quyết định" });
        Assert.Equal(HttpStatusCode.Created, typeResponse.StatusCode);
        Assert.NotNull(typeResponse.Headers.Location);
        var documentType = (await typeResponse.Content.ReadFromJsonAsync<DocumentTypeResponse>())!;

        var duplicateType = await client.PostAsJsonAsync(
            "/api/v1/document-types",
            new { code = "DECISION", name = "Tên khác" });
        await ProblemDetailsAssert.HasContractAsync(
            duplicateType,
            HttpStatusCode.Conflict,
            "conflict",
            "/api/v1/document-types");

        var invalidTemplate = await client.PostAsJsonAsync(
            "/api/v1/document-templates",
            new
            {
                documentTypeId = documentType.Id,
                name = "Mẫu quyết định",
                templateContent = "Nội dung",
                formatRules = new { version = 0, rules = Array.Empty<object>() }
            });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidTemplate.StatusCode);
        var validation = await invalidTemplate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(validation.GetProperty("errors").TryGetProperty("formatRules", out _));

        var templateResponse = await client.PostAsJsonAsync(
            "/api/v1/document-templates",
            new
            {
                documentTypeId = documentType.Id,
                name = "Mẫu quyết định",
                templateContent = "Nội dung",
                formatRules = new
                {
                    version = 1,
                    rules = new[] { new { code = "header", required = true } }
                }
            });
        Assert.Equal(HttpStatusCode.Created, templateResponse.StatusCode);
        var template = (await templateResponse.Content.ReadFromJsonAsync<DocumentTemplateResponse>())!;
        Assert.Equal("DECISION", template.DocumentType.Code);
        Assert.Equal(JsonValueKind.Object, template.FormatRules.ValueKind);

        var activePage = await client.GetFromJsonAsync<PagedResponse<DocumentTemplateResponse>>(
            "/api/v1/document-templates?activeOnly=true&page=1&pageSize=20");
        Assert.Single(activePage!.Items);

        var deactivate = await client.PatchAsJsonAsync(
            $"/api/v1/document-types/{documentType.Id}",
            new { isActive = false });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        var usableAfterDeactivate = await client.GetFromJsonAsync<
            PagedResponse<DocumentTemplateResponse>>(
            "/api/v1/document-templates?activeOnly=true&page=1&pageSize=20");
        Assert.Empty(usableAfterDeactivate!.Items);

        var missingId = Guid.NewGuid();
        await ProblemDetailsAssert.HasContractAsync(
            await client.GetAsync($"/api/v1/document-templates/{missingId}"),
            HttpStatusCode.NotFound,
            "not-found",
            $"/api/v1/document-templates/{missingId}");
    }

    [Fact]
    public async Task Seeded_catalog_is_available_through_active_only_endpoints()
    {
        using var factory = new StaffManagementApiFactory();
        using var client = factory.CreateApiClient();
        await AuthenticateAsync(client, "admin");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<IDocumentCatalogSeeder>()
                .SeedAsync();
        }

        var documentTypes = await client.GetFromJsonAsync<
            PagedResponse<DocumentTypeResponse>>(
            "/api/v1/document-types?activeOnly=true&page=1&pageSize=20");
        var templates = await client.GetFromJsonAsync<
            PagedResponse<DocumentTemplateResponse>>(
            "/api/v1/document-templates?activeOnly=true&page=1&pageSize=20");

        Assert.Equal(7, documentTypes!.TotalCount);
        Assert.Equal(
            [
                "CONCLUSION_NOTICE",
                "DECISION",
                "INVITATION",
                "PLAN",
                "PROGRAM",
                "REPORT",
                "RESOLUTION"
            ],
            documentTypes.Items.Select(documentType => documentType.Code));
        Assert.Equal(7, templates!.TotalCount);
        Assert.All(templates.Items, template =>
        {
            Assert.True(template.IsActive);
            Assert.True(template.DocumentType.Id != Guid.Empty);
            Assert.False(string.IsNullOrWhiteSpace(template.DocumentType.Code));
            AssertSeedFormatRules(template.FormatRules);
        });
    }

    private static void AssertSeedFormatRules(JsonElement formatRules)
    {
        Assert.Equal(1, formatRules.GetProperty("version").GetInt32());
        Assert.Equal(3, formatRules.GetProperty("rules").GetArrayLength());
    }

    private static async Task AuthenticateAsync(HttpClient client, string userName)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(userName, Password));
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(login);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }
}
