using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalOps.API.Features.Authentication;
using DigitalOps.API.Features.Drafting;
using DigitalOps.API.Features.OutgoingDocuments;
using DigitalOps.API.Shared.AI;
using DigitalOps.API.Shared.Data;
using DigitalOps.API.Shared.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalOps.API.Tests;

public sealed class OutgoingDocumentServiceTests
{
    [Fact]
    public async Task Patch_validates_owner_and_state_and_preserves_first_ai_draft()
    {
        await using var database = await OutgoingDocumentDatabase.CreateAsync();
        var owner = await database.CreateStaffAsync("Owner");
        var other = await database.CreateStaffAsync("Other");
        var template = await database.CreateTemplateAsync(isActive: true);
        var service = database.CreateService();
        var created = (await service.CreateAsync(CreateRequest(template.Id), owner.Id)).Value!;

        var empty = await service.UpdateAsync(
            created.Id,
            new OutgoingDocumentUpdateRequest(),
            owner.Id);
        Assert.Equal(OutgoingDocumentFailure.Validation, empty.Failure);
        Assert.Contains("body", empty.Errors.Keys);

        var invalid = await service.UpdateAsync(
            created.Id,
            new OutgoingDocumentUpdateRequest { Content = null },
            owner.Id);
        Assert.Equal(OutgoingDocumentFailure.Validation, invalid.Failure);
        Assert.Contains("content", invalid.Errors.Keys);

        var forbidden = await service.UpdateAsync(
            created.Id,
            new OutgoingDocumentUpdateRequest { Content = "Không được lưu" },
            other.Id);
        Assert.Equal(OutgoingDocumentFailure.Forbidden, forbidden.Failure);

        var entity = await database.Context.OutgoingDocuments.SingleAsync(
            document => document.Id == created.Id);
        entity.Status = OutgoingDocumentStatus.ReviewFailed;
        entity.AiDraftContent = "Bản AI đầu tiên";
        entity.ReviewIssues = JsonDocument.Parse(
            "[{\"ruleCode\":\"header\",\"severity\":\"Error\",\"message\":\"Thiếu tiêu đề\",\"location\":null}]")
            .RootElement.Clone();
        await database.Context.SaveChangesAsync();

        var updated = await service.UpdateAsync(
            created.Id,
            new OutgoingDocumentUpdateRequest
            {
                Title = "  Tiêu đề mới  ",
                Content = "  Nội dung cán bộ sửa  "
            },
            owner.Id);

        Assert.True(updated.Succeeded);
        Assert.Equal("Tiêu đề mới", updated.Value!.Title);
        Assert.Equal("Nội dung cán bộ sửa", updated.Value.Content);
        Assert.Equal("Bản AI đầu tiên", updated.Value.AiDraftContent);
        Assert.Equal(OutgoingDocumentStatus.Editing, updated.Value.Status);
        Assert.Single(updated.Value.ReviewIssues);

        entity.Status = OutgoingDocumentStatus.PendingReview;
        await database.Context.SaveChangesAsync();
        var locked = await service.UpdateAsync(
            created.Id,
            new OutgoingDocumentUpdateRequest { Content = "Không được lưu" },
            owner.Id);
        Assert.Equal(OutgoingDocumentFailure.Conflict, locked.Failure);
    }

    [Fact]
    public async Task Ai_draft_preserves_first_result_and_failure_does_not_mutate()
    {
        await using var database = await OutgoingDocumentDatabase.CreateAsync();
        var owner = await database.CreateStaffAsync("Owner");
        var template = await database.CreateTemplateAsync(isActive: true);
        var generator = new AiDraftGeneratorTestDouble
        {
            Handler = (_, _) => Task.FromResult(new AiDraftGenerationResult("Bản AI đầu tiên"))
        };
        var service = database.CreateService(generator);
        var created = (await service.CreateAsync(CreateRequest(template.Id), owner.Id)).Value!;

        var first = await service.GenerateAiDraftAsync(
            created.Id,
            new AiDraftRequest { Instruction = "  Nhấn mạnh tiến độ  " },
            owner.Id);
        Assert.True(first.Succeeded);
        Assert.Equal("Bản AI đầu tiên", first.Value!.Content);
        Assert.Equal("Bản AI đầu tiên", first.Value.AiDraftContent);
        Assert.Equal(OutgoingDocumentStatus.AiDraft, first.Value.Status);
        Assert.Equal("Nhấn mạnh tiến độ", generator.LastInput!.Instruction);

        generator.Handler = (_, _) =>
            Task.FromResult(new AiDraftGenerationResult("Bản AI lần hai"));
        var second = await service.GenerateAiDraftAsync(
            created.Id,
            new AiDraftRequest(),
            owner.Id);
        Assert.True(second.Succeeded);
        Assert.Equal("Bản AI lần hai", second.Value!.Content);
        Assert.Equal("Bản AI đầu tiên", second.Value.AiDraftContent);
        Assert.Equal(OutgoingDocumentStatus.Editing, second.Value.Status);

        generator.Handler = (_, _) => throw new AiProviderException("provider unavailable");
        var failed = await service.GenerateAiDraftAsync(
            created.Id,
            new AiDraftRequest(),
            owner.Id);
        Assert.Equal(OutgoingDocumentFailure.ServiceUnavailable, failed.Failure);
        var unchanged = await service.GetByIdAsync(created.Id);
        Assert.Equal("Bản AI lần hai", unchanged!.Content);
        Assert.Equal("Bản AI đầu tiên", unchanged.AiDraftContent);
        Assert.Equal(OutgoingDocumentStatus.Editing, unchanged.Status);
    }

    [Fact]
    public async Task Ai_draft_rejects_inactive_template_and_concurrent_document_change()
    {
        await using var database = await OutgoingDocumentDatabase.CreateAsync();
        var owner = await database.CreateStaffAsync("Owner");
        var template = await database.CreateTemplateAsync(isActive: true);
        var generator = new AiDraftGeneratorTestDouble();
        var service = database.CreateService(generator);
        var created = (await service.CreateAsync(CreateRequest(template.Id), owner.Id)).Value!;

        template.IsActive = false;
        await database.Context.SaveChangesAsync();
        var inactive = await service.GenerateAiDraftAsync(
            created.Id,
            new AiDraftRequest(),
            owner.Id);
        Assert.Equal(OutgoingDocumentFailure.Conflict, inactive.Failure);
        Assert.Equal(0, generator.CallCount);

        template.IsActive = true;
        await database.Context.SaveChangesAsync();
        generator.Handler = async (_, _) =>
        {
            var document = await database.Context.OutgoingDocuments.SingleAsync(
                item => item.Id == created.Id);
            document.Content = "Nội dung từ request khác";
            await database.Context.SaveChangesAsync();
            return new AiDraftGenerationResult("Kết quả AI đến muộn");
        };

        var concurrent = await service.GenerateAiDraftAsync(
            created.Id,
            new AiDraftRequest(),
            owner.Id);
        Assert.Equal(OutgoingDocumentFailure.Conflict, concurrent.Failure);
        var persisted = await service.GetByIdAsync(created.Id);
        Assert.Equal("Nội dung từ request khác", persisted!.Content);
        Assert.Null(persisted.AiDraftContent);
    }

    private static OutgoingDocumentCreateRequest CreateRequest(Guid templateId) => new()
    {
        TemplateId = templateId,
        Title = "Văn bản thử nghiệm"
    };

    private sealed class OutgoingDocumentDatabase(
        SqliteConnection connection,
        DigitalOpsDbContext context) : IAsyncDisposable
    {
        public DigitalOpsDbContext Context { get; } = context;

        public static async Task<OutgoingDocumentDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<DigitalOpsDbContext>()
                .UseSqlite(connection)
                .ReplaceService<IModelCustomizer, AuthenticationTestModelCustomizer>()
                .Options;
            var context = new DigitalOpsDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new OutgoingDocumentDatabase(connection, context);
        }

        public OutgoingDocumentService CreateService(
            AiDraftGeneratorTestDouble? generator = null) =>
            new(
                Context,
                generator ?? new AiDraftGeneratorTestDouble(),
                NullLogger<OutgoingDocumentService>.Instance);

        public async Task<Staff> CreateStaffAsync(string fullName)
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = $"user-{Guid.NewGuid():N}",
                Email = $"{Guid.NewGuid():N}@test.local"
            };
            var staff = new Staff
            {
                Id = Guid.NewGuid(),
                IdentityUserId = user.Id,
                IdentityUser = user,
                FullName = fullName,
                Email = user.Email!,
                IsActive = true
            };
            Context.Staff.Add(staff);
            await Context.SaveChangesAsync();
            return staff;
        }

        public async Task<DocumentTemplate> CreateTemplateAsync(bool isActive)
        {
            var type = new DocumentType
            {
                Id = Guid.NewGuid(),
                Code = $"TYPE-{Guid.NewGuid():N}"[..20],
                Name = "Loại thử nghiệm",
                IsActive = true
            };
            var template = new DocumentTemplate
            {
                Id = Guid.NewGuid(),
                DocumentTypeId = type.Id,
                DocumentType = type,
                Name = "Mẫu thử nghiệm",
                TemplateContent = "KẾ HOẠCH\nI. MỤC ĐÍCH\n[CẦN BỔ SUNG]",
                FormatRules = JsonDocument.Parse("{\"version\":1,\"rules\":[]}")
                    .RootElement.Clone(),
                IsActive = isActive
            };
            Context.DocumentTemplates.Add(template);
            await Context.SaveChangesAsync();
            return template;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}

public sealed class OutgoingDocumentApiTests
{
    private const string Password = "Valid1!Password";
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task Drafter_can_patch_and_generate_first_and_subsequent_ai_drafts()
    {
        using var factory = new StaffManagementApiFactory();
        var templateId = await CreateTemplateAsync(factory);
        using var drafter = factory.CreateApiClient();
        await AuthenticateAsync(drafter, "drafter");
        var created = await CreateOutgoingAsync(drafter, templateId);

        var patchedResponse = await drafter.PatchAsJsonAsync(
            $"/api/v1/outgoing-documents/{created.Id}",
            new { title = "Tiêu đề đã sửa", content = "Nội dung cán bộ" });
        Assert.Equal(HttpStatusCode.OK, patchedResponse.StatusCode);
        var patched = (await patchedResponse.Content
            .ReadFromJsonAsync<OutgoingDocumentResponse>(JsonOptions))!;
        Assert.Equal(OutgoingDocumentStatus.Editing, patched.Status);
        Assert.Equal("Nội dung cán bộ", patched.Content);

        factory.AiDraftGenerator.Handler = (_, _) =>
            Task.FromResult(new AiDraftGenerationResult("AI lần đầu"));
        var firstResponse = await drafter.PostAsJsonAsync(
            $"/api/v1/outgoing-documents/{created.Id}/ai-draft",
            new { instruction = "Tập trung tiến độ" });
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var first = (await firstResponse.Content
            .ReadFromJsonAsync<OutgoingDocumentResponse>(JsonOptions))!;
        Assert.Equal("AI lần đầu", first.Content);
        Assert.Equal("AI lần đầu", first.AiDraftContent);
        Assert.Equal(OutgoingDocumentStatus.AiDraft, first.Status);

        factory.AiDraftGenerator.Handler = (_, _) =>
            Task.FromResult(new AiDraftGenerationResult("AI lần sau"));
        var secondResponse = await drafter.PostAsJsonAsync(
            $"/api/v1/outgoing-documents/{created.Id}/ai-draft",
            new { });
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var second = (await secondResponse.Content
            .ReadFromJsonAsync<OutgoingDocumentResponse>(JsonOptions))!;
        Assert.Equal("AI lần sau", second.Content);
        Assert.Equal("AI lần đầu", second.AiDraftContent);
        Assert.Equal(OutgoingDocumentStatus.Editing, second.Status);
    }

    [Fact]
    public async Task Endpoints_enforce_role_ownership_state_and_preserve_data_on_ai_failure()
    {
        using var factory = new StaffManagementApiFactory();
        var templateId = await CreateTemplateAsync(factory);
        using var drafter = factory.CreateApiClient();
        await AuthenticateAsync(drafter, "drafter");
        var created = await CreateOutgoingAsync(drafter, templateId);

        using var other = factory.CreateApiClient();
        await AuthenticateAsync(other, "otherdrafter");
        await ProblemDetailsAssert.HasContractAsync(
            await other.PatchAsJsonAsync(
                $"/api/v1/outgoing-documents/{created.Id}",
                new { content = "Không được sửa" }),
            HttpStatusCode.Forbidden,
            "forbidden",
            $"/api/v1/outgoing-documents/{created.Id}");

        using var admin = factory.CreateApiClient();
        await AuthenticateAsync(admin, "admin");
        await ProblemDetailsAssert.HasContractAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/outgoing-documents/{created.Id}/ai-draft",
                new { }),
            HttpStatusCode.Forbidden,
            "forbidden",
            $"/api/v1/outgoing-documents/{created.Id}/ai-draft");

        factory.AiDraftGenerator.Handler = (_, _) =>
            throw new AiProviderException("provider unavailable");
        await ProblemDetailsAssert.HasContractAsync(
            await drafter.PostAsJsonAsync(
                $"/api/v1/outgoing-documents/{created.Id}/ai-draft",
                new { }),
            HttpStatusCode.ServiceUnavailable,
            "ai-service-unavailable",
            $"/api/v1/outgoing-documents/{created.Id}/ai-draft");
        var unchanged = await drafter.GetFromJsonAsync<OutgoingDocumentResponse>(
            $"/api/v1/outgoing-documents/{created.Id}",
            JsonOptions);
        Assert.Equal(created.Content, unchanged!.Content);
        Assert.Null(unchanged.AiDraftContent);

        await SetStatusAsync(factory, created.Id, OutgoingDocumentStatus.PendingReview);
        await ProblemDetailsAssert.HasContractAsync(
            await drafter.PatchAsJsonAsync(
                $"/api/v1/outgoing-documents/{created.Id}",
                new { content = "Không được sửa" }),
            HttpStatusCode.Conflict,
            "conflict",
            $"/api/v1/outgoing-documents/{created.Id}");
    }

    private static async Task<OutgoingDocumentResponse> CreateOutgoingAsync(
        HttpClient client,
        Guid templateId)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/outgoing-documents",
            new { templateId, title = "Văn bản API" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OutgoingDocumentResponse>(JsonOptions))!;
    }

    private static async Task<Guid> CreateTemplateAsync(StaffManagementApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DigitalOpsDbContext>();
        var type = new DocumentType
        {
            Id = Guid.NewGuid(),
            Code = $"API-{Guid.NewGuid():N}"[..20],
            Name = "Loại API",
            IsActive = true
        };
        var template = new DocumentTemplate
        {
            Id = Guid.NewGuid(),
            DocumentTypeId = type.Id,
            DocumentType = type,
            Name = "Mẫu API",
            TemplateContent = "BÁO CÁO\nNội dung ban đầu",
            FormatRules = JsonDocument.Parse("{\"version\":1,\"rules\":[]}")
                .RootElement.Clone(),
            IsActive = true
        };
        dbContext.DocumentTemplates.Add(template);
        await dbContext.SaveChangesAsync();
        return template.Id;
    }

    private static async Task SetStatusAsync(
        StaffManagementApiFactory factory,
        Guid id,
        OutgoingDocumentStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DigitalOpsDbContext>();
        var document = await dbContext.OutgoingDocuments.SingleAsync(item => item.Id == id);
        document.Status = status;
        await dbContext.SaveChangesAsync();
    }

    private static async Task AuthenticateAsync(HttpClient client, string userName)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(userName, Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
