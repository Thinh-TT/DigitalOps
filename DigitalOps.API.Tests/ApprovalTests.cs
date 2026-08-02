using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalOps.API.Features.Approval;
using DigitalOps.API.Features.Authentication;
using DigitalOps.API.Features.Drafting;
using DigitalOps.API.Features.OutgoingDocuments;
using DigitalOps.API.Features.Review;
using DigitalOps.API.Shared.Api;
using DigitalOps.API.Shared.Data;
using DigitalOps.API.Shared.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalOps.API.Tests;

public sealed class OutgoingDocumentApprovalServiceTests
{
    private static readonly DateTimeOffset DecisionTime = new(2026, 8, 2, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Approve_records_leader_and_utc_time_without_changing_review_history()
    {
        await using var database = await ApprovalDatabase.CreateAsync();
        var owner = await database.CreateStaffAsync("Người soạn");
        var leader = await database.CreateStaffAsync("Lãnh đạo");
        var document = await database.CreatePendingApprovalDocumentAsync(owner.Id);
        var service = database.CreateApprovalService(DecisionTime);

        var result = await service.DecideAsync(
            document.Id,
            new ApprovalDecisionRequest { Decision = ApprovalDecision.Approve },
            leader.Id);

        Assert.True(result.Succeeded, result.Detail ?? result.Failure.ToString());
        Assert.Equal(OutgoingDocumentStatus.Approved, result.Value!.Status);
        Assert.Equal(leader.Id, result.Value.ApprovedByStaff!.Id);
        Assert.Equal(DecisionTime.UtcDateTime, result.Value.ApprovedAt);

        var persisted = await database.Context.OutgoingDocuments
            .AsNoTracking()
            .SingleAsync(item => item.Id == document.Id);
        Assert.Equal(OutgoingDocumentStatus.Approved, persisted.Status);
        Assert.Equal(leader.Id, persisted.ApprovedByStaffId);
        Assert.Equal(DecisionTime.UtcDateTime, persisted.ApprovedAt);
        Assert.Equal(DecisionTime.UtcDateTime, persisted.UpdatedAt);
        Assert.Equal(1, await database.Context.ReviewHistory.CountAsync());
    }

    [Fact]
    public async Task Return_clears_approval_fields_and_preserves_content_issues_and_history()
    {
        await using var database = await ApprovalDatabase.CreateAsync();
        var owner = await database.CreateStaffAsync("Người soạn");
        var previousLeader = await database.CreateStaffAsync("Lãnh đạo trước");
        var currentLeader = await database.CreateStaffAsync("Lãnh đạo trả lại");
        var document = await database.CreatePendingApprovalDocumentAsync(
            owner.Id,
            approvedByStaffId: previousLeader.Id,
            approvedAt: DecisionTime.AddHours(-1).UtcDateTime);
        var service = database.CreateApprovalService(DecisionTime);

        var result = await service.DecideAsync(
            document.Id,
            new ApprovalDecisionRequest { Decision = ApprovalDecision.Return },
            currentLeader.Id);

        Assert.True(result.Succeeded, result.Detail ?? result.Failure.ToString());
        Assert.Equal(OutgoingDocumentStatus.Editing, result.Value!.Status);
        Assert.Null(result.Value.ApprovedByStaff);
        Assert.Null(result.Value.ApprovedAt);

        var persisted = await database.Context.OutgoingDocuments
            .AsNoTracking()
            .SingleAsync(item => item.Id == document.Id);
        Assert.Equal(OutgoingDocumentStatus.Editing, persisted.Status);
        Assert.Null(persisted.ApprovedByStaffId);
        Assert.Null(persisted.ApprovedAt);
        Assert.Equal(document.Content, persisted.Content);
        Assert.Equal("style", persisted.ReviewIssues[0].GetProperty("ruleCode").GetString());
        Assert.Equal(1, await database.Context.ReviewHistory.CountAsync());
    }

    [Fact]
    public async Task Approval_requires_pending_status_and_latest_passed_review()
    {
        await using var database = await ApprovalDatabase.CreateAsync();
        var owner = await database.CreateStaffAsync("Người soạn");
        var leader = await database.CreateStaffAsync("Lãnh đạo");
        var editing = await database.CreatePendingApprovalDocumentAsync(owner.Id);
        var missingReview = await database.CreatePendingApprovalDocumentAsync(owner.Id, includeReview: false);
        var service = database.CreateApprovalService(DecisionTime);

        var editingEntity = await database.Context.OutgoingDocuments.SingleAsync(item => item.Id == editing.Id);
        editingEntity.Status = OutgoingDocumentStatus.Editing;
        await database.Context.SaveChangesAsync();
        database.Context.ChangeTracker.Clear();

        var editingResult = await service.DecideAsync(
            editing.Id,
            new ApprovalDecisionRequest { Decision = ApprovalDecision.Approve },
            leader.Id);
        var missingReviewResult = await service.DecideAsync(
            missingReview.Id,
            new ApprovalDecisionRequest { Decision = ApprovalDecision.Approve },
            leader.Id);
        var invalidDecision = await service.DecideAsync(
            editing.Id,
            new ApprovalDecisionRequest(),
            leader.Id);

        Assert.Equal(ApprovalOperationFailure.Conflict, editingResult.Failure);
        Assert.Equal(ApprovalOperationFailure.Conflict, missingReviewResult.Failure);
        Assert.Equal(ApprovalOperationFailure.Validation, invalidDecision.Failure);
    }

    private sealed class ApprovalDatabase(
        SqliteConnection connection,
        DigitalOpsDbContext context) : IAsyncDisposable
    {
        public DigitalOpsDbContext Context { get; } = context;

        public static async Task<ApprovalDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<DigitalOpsDbContext>()
                .UseSqlite(connection)
                .ReplaceService<IModelCustomizer, AuthenticationTestModelCustomizer>()
                .Options;
            var context = new DigitalOpsDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new ApprovalDatabase(connection, context);
        }

        public OutgoingDocumentApprovalService CreateApprovalService(DateTimeOffset utcNow) =>
            new(
                Context,
                new OutgoingDocumentService(
                    Context,
                    new AiDraftGeneratorTestDouble(),
                    NullLogger<OutgoingDocumentService>.Instance),
                new FixedTimeProvider(utcNow));

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

        public async Task<OutgoingDocument> CreatePendingApprovalDocumentAsync(
            Guid draftedByStaffId,
            bool includeReview = true,
            Guid? approvedByStaffId = null,
            DateTime? approvedAt = null)
        {
            var type = new DocumentType
            {
                Id = Guid.NewGuid(),
                Code = $"TYPE-{Guid.NewGuid():N}"[..20],
                Name = "Loại thử",
                IsActive = true
            };
            var template = new DocumentTemplate
            {
                Id = Guid.NewGuid(),
                DocumentTypeId = type.Id,
                DocumentType = type,
                Name = "Mẫu thử",
                TemplateContent = "Nội dung mẫu",
                FormatRules = JsonDocument.Parse("{\"version\":1,\"rules\":[]}").RootElement.Clone(),
                IsActive = true
            };
            var document = new OutgoingDocument
            {
                Id = Guid.NewGuid(),
                TemplateId = template.Id,
                Template = template,
                Title = "Văn bản chờ duyệt",
                Content = "Nội dung cần duyệt",
                DraftedByStaffId = draftedByStaffId,
                Status = OutgoingDocumentStatus.PendingApproval,
                ReviewIssues = JsonDocument.Parse("[{\"ruleCode\":\"style\",\"severity\":\"Warning\",\"message\":\"Kiểm tra thể thức\",\"location\":null}]").RootElement.Clone(),
                ApprovedByStaffId = approvedByStaffId,
                ApprovedAt = approvedAt
            };
            Context.OutgoingDocuments.Add(document);
            if (includeReview)
            {
                Context.ReviewHistory.Add(new ReviewHistory
                {
                    Id = Guid.NewGuid(),
                    OutgoingDocumentId = document.Id,
                    AttemptNo = 1,
                    ReviewSource = ReviewSource.Rule,
                    ReviewedByStaffId = draftedByStaffId,
                    ContentSnapshot = document.Content,
                    ReviewResult = ReviewResult.Passed,
                    ReviewIssues = document.ReviewIssues.Clone(),
                    ReviewedAt = DateTime.UtcNow
                });
            }

            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
            return document;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

public sealed class OutgoingDocumentApprovalApiTests
{
    private const string Password = "Valid1!Password";
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task Leader_can_return_then_drafter_must_review_again_before_approval()
    {
        using var factory = new StaffManagementApiFactory();
        var templateId = await CreateTemplateAsync(factory);
        using var drafter = factory.CreateApiClient();
        await AuthenticateAsync(drafter, "drafter");
        var created = await CreateOutgoingAsync(drafter, templateId);
        await MakePendingApprovalAsync(factory, drafter, created.Id);

        using var leader = factory.CreateApiClient();
        await AuthenticateAsync(leader, "leader");
        var returnedResponse = await leader.PostAsJsonAsync(
            $"/api/v1/outgoing-documents/{created.Id}/approval",
            new { decision = "Return" });
        Assert.Equal(HttpStatusCode.OK, returnedResponse.StatusCode);
        var returned = (await returnedResponse.Content
            .ReadFromJsonAsync<OutgoingDocumentResponse>(JsonOptions))!;
        Assert.Equal(OutgoingDocumentStatus.Editing, returned.Status);
        Assert.Null(returned.ApprovedByStaff);
        Assert.Null(returned.ApprovedAt);

        await ProblemDetailsAssert.HasContractAsync(
            await leader.PostAsJsonAsync(
                $"/api/v1/outgoing-documents/{created.Id}/approval",
                new { decision = "Approve" }),
            HttpStatusCode.Conflict,
            "conflict",
            $"/api/v1/outgoing-documents/{created.Id}/approval");

        var patch = await drafter.PatchAsJsonAsync(
            $"/api/v1/outgoing-documents/{created.Id}",
            new { content = "Nội dung đã chỉnh sửa sau khi trả lại" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        await MakePendingApprovalAsync(factory, drafter, created.Id);

        var approvedResponse = await leader.PostAsJsonAsync(
            $"/api/v1/outgoing-documents/{created.Id}/approval",
            new { decision = "Approve" });
        Assert.Equal(HttpStatusCode.OK, approvedResponse.StatusCode);
        var approved = (await approvedResponse.Content
            .ReadFromJsonAsync<OutgoingDocumentResponse>(JsonOptions))!;
        Assert.Equal(OutgoingDocumentStatus.Approved, approved.Status);
        Assert.Equal("F Leader", approved.ApprovedByStaff!.FullName);
        Assert.NotNull(approved.ApprovedAt);

        var history = (await leader.GetFromJsonAsync<PagedResponse<ReviewResponse>>(
            $"/api/v1/outgoing-documents/{created.Id}/reviews",
            JsonOptions))!;
        Assert.Equal(2, history.TotalCount);
        Assert.Equal([2, 1], history.Items.Select(item => item.AttemptNo).ToArray());
    }

    [Fact]
    public async Task Approval_endpoint_enforces_role_validation_and_preconditions()
    {
        using var factory = new StaffManagementApiFactory();
        var templateId = await CreateTemplateAsync(factory);
        using var drafter = factory.CreateApiClient();
        await AuthenticateAsync(drafter, "drafter");
        var created = await CreateOutgoingAsync(drafter, templateId);
        var path = $"/api/v1/outgoing-documents/{created.Id}/approval";

        using var anonymous = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(path, new { decision = "Approve" })).StatusCode);

        using var administrator = factory.CreateApiClient();
        await AuthenticateAsync(administrator, "admin");
        Assert.Equal(HttpStatusCode.Forbidden, (await administrator.PostAsJsonAsync(path, new { decision = "Approve" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await drafter.PostAsJsonAsync(path, new { decision = "Approve" })).StatusCode);

        using var leader = factory.CreateApiClient();
        await AuthenticateAsync(leader, "leader");
        await ProblemDetailsAssert.HasContractAsync(
            await leader.PostAsJsonAsync(path, new { decision = "Approve" }),
            HttpStatusCode.Conflict,
            "conflict",
            path);
        Assert.Equal(HttpStatusCode.BadRequest, (await leader.PostAsJsonAsync(path, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await leader.PostAsJsonAsync(path, new { decision = "Invalid" })).StatusCode);

        await SetStatusAsync(factory, created.Id, OutgoingDocumentStatus.PendingApproval);
        await ProblemDetailsAssert.HasContractAsync(
            await leader.PostAsJsonAsync(path, new { decision = "Approve" }),
            HttpStatusCode.Conflict,
            "conflict",
            path);
    }

    [Fact]
    public async Task Concurrent_approval_decisions_allow_only_one_winner()
    {
        using var factory = new StaffManagementApiFactory();
        var templateId = await CreateTemplateAsync(factory);
        using var drafter = factory.CreateApiClient();
        await AuthenticateAsync(drafter, "drafter");
        var created = await CreateOutgoingAsync(drafter, templateId);
        await MakePendingApprovalAsync(factory, drafter, created.Id);
        using var leader = factory.CreateApiClient();
        await AuthenticateAsync(leader, "leader");
        var path = $"/api/v1/outgoing-documents/{created.Id}/approval";

        var approve = leader.PostAsJsonAsync(path, new { decision = "Approve" });
        var returned = leader.PostAsJsonAsync(path, new { decision = "Return" });
        var responses = await Task.WhenAll(approve, returned);

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        var finalDocument = await leader.GetFromJsonAsync<OutgoingDocumentResponse>(
            $"/api/v1/outgoing-documents/{created.Id}",
            JsonOptions);
        Assert.NotNull(finalDocument);
        Assert.True(
            finalDocument!.Status is OutgoingDocumentStatus.Approved or OutgoingDocumentStatus.Editing);
    }

    private static async Task MakePendingApprovalAsync(
        StaffManagementApiFactory factory,
        HttpClient drafter,
        Guid outgoingDocumentId)
    {
        factory.DocumentReviewGenerator.Handler = (_, _) => Task.FromResult(
            new DocumentReviewGenerationResult(
                ReviewSource.Rule,
                [new ReviewIssueResponse("style", "Warning", "Cần kiểm tra cách trình bày.", null)]));
        var response = await drafter.PostAsync(
            $"/api/v1/outgoing-documents/{outgoingDocumentId}/reviews",
            content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var review = (await response.Content.ReadFromJsonAsync<ReviewResponse>(JsonOptions))!;
        Assert.Equal(OutgoingDocumentStatus.PendingApproval, review.DocumentStatus);
    }

    private static async Task<OutgoingDocumentResponse> CreateOutgoingAsync(HttpClient client, Guid templateId)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/outgoing-documents",
            new { templateId, title = "Văn bản phê duyệt" });
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
            Code = $"APPROVAL-{Guid.NewGuid():N}"[..20],
            Name = "Loại phê duyệt",
            IsActive = true
        };
        var template = new DocumentTemplate
        {
            Id = Guid.NewGuid(),
            DocumentTypeId = type.Id,
            DocumentType = type,
            Name = "Mẫu phê duyệt",
            TemplateContent = "Nội dung mẫu",
            FormatRules = JsonDocument.Parse("{\"version\":1,\"rules\":[]}").RootElement.Clone(),
            IsActive = true
        };
        dbContext.DocumentTemplates.Add(template);
        await dbContext.SaveChangesAsync();
        return template.Id;
    }

    private static async Task SetStatusAsync(
        StaffManagementApiFactory factory,
        Guid outgoingDocumentId,
        OutgoingDocumentStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DigitalOpsDbContext>();
        var document = await dbContext.OutgoingDocuments.SingleAsync(item => item.Id == outgoingDocumentId);
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
