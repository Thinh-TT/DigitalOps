using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DigitalOps.API.Features.Authentication;
using DigitalOps.API.Features.Drafting;
using DigitalOps.API.Features.IncomingDocuments;
using DigitalOps.API.Shared.AI;
using DigitalOps.API.Shared.Api;
using DigitalOps.API.Shared.Data;
using DigitalOps.API.Shared.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalOps.API.Tests;

public sealed class IncomingDocumentServiceTests
{
    private static readonly DateTimeOffset CompletionTime =
        new(2026, 7, 31, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_trims_fields_starts_new_and_returns_empty_workflow_data()
    {
        await using var database = await IncomingDocumentDatabase.CreateAsync();
        var type = await database.CreateDocumentTypeAsync("REPORT", isActive: true);
        var service = database.CreateService();

        var result = await service.CreateAsync(new IncomingDocumentCreateRequest
        {
            ReferenceNumber = "  12/BC-MTTQ  ",
            SenderOrg = "  UBND phường  ",
            Summary = "  Báo cáo công tác tháng  ",
            ReceivedDate = new DateOnly(2026, 7, 30),
            Deadline = new DateOnly(2026, 8, 5),
            DocumentTypeId = type.Id
        });

        Assert.True(result.Succeeded);
        Assert.Equal("12/BC-MTTQ", result.Value!.ReferenceNumber);
        Assert.Equal("UBND phường", result.Value.SenderOrg);
        Assert.Equal("Báo cáo công tác tháng", result.Value.Summary);
        Assert.Equal(IncomingDocumentStatus.New, result.Value.Status);
        Assert.Equal("REPORT", result.Value.DocumentType.Code);
        Assert.Null(result.Value.SuggestedStaff);
        Assert.Null(result.Value.AssignedToStaff);
        Assert.Null(result.Value.CompletedAt);
        Assert.Empty(result.Value.Attachments);
    }

    [Fact]
    public async Task Create_rejects_missing_fields_bad_dates_and_inactive_type()
    {
        await using var database = await IncomingDocumentDatabase.CreateAsync();
        var inactiveType = await database.CreateDocumentTypeAsync("NOTICE", isActive: false);
        var service = database.CreateService();

        var invalid = await service.CreateAsync(new IncomingDocumentCreateRequest
        {
            ReferenceNumber = " ",
            SenderOrg = null,
            Summary = " ",
            ReceivedDate = new DateOnly(2026, 8, 2),
            Deadline = new DateOnly(2026, 8, 1),
            DocumentTypeId = inactiveType.Id
        });

        Assert.Equal(IncomingDocumentFailure.Validation, invalid.Failure);
        Assert.Contains("referenceNumber", invalid.Errors.Keys);
        Assert.Contains("senderOrg", invalid.Errors.Keys);
        Assert.Contains("summary", invalid.Errors.Keys);
        Assert.Contains("deadline", invalid.Errors.Keys);

        var validFields = CreateRequest(inactiveType.Id);
        var inactive = await service.CreateAsync(validFields);
        Assert.Equal(IncomingDocumentFailure.Validation, inactive.Failure);
        Assert.Contains("documentTypeId", inactive.Errors.Keys);
    }

    [Fact]
    public async Task Patch_tracks_presence_validates_final_dates_and_allows_other_edits_with_inactive_current_type()
    {
        await using var database = await IncomingDocumentDatabase.CreateAsync();
        var currentType = await database.CreateDocumentTypeAsync("REPORT", isActive: true);
        var nextType = await database.CreateDocumentTypeAsync("PLAN", isActive: true);
        var service = database.CreateService();
        var created = (await service.CreateAsync(CreateRequest(currentType.Id))).Value!;

        var empty = await service.UpdateAsync(created.Id, new IncomingDocumentUpdateRequest());
        Assert.Equal(IncomingDocumentFailure.Validation, empty.Failure);
        Assert.Contains("body", empty.Errors.Keys);

        var explicitNull = await service.UpdateAsync(
            created.Id,
            new IncomingDocumentUpdateRequest { Summary = null });
        Assert.Equal(IncomingDocumentFailure.Validation, explicitNull.Failure);
        Assert.Contains("summary", explicitNull.Errors.Keys);

        var invalidDate = await service.UpdateAsync(
            created.Id,
            new IncomingDocumentUpdateRequest
            {
                ReceivedDate = new DateOnly(2026, 8, 20)
            });
        Assert.Equal(IncomingDocumentFailure.Validation, invalidDate.Failure);
        Assert.Contains("deadline", invalidDate.Errors.Keys);

        currentType.IsActive = false;
        await database.Context.SaveChangesAsync();
        var otherField = await service.UpdateAsync(
            created.Id,
            new IncomingDocumentUpdateRequest { Summary = "  Nội dung mới  " });
        Assert.True(otherField.Succeeded);
        Assert.Equal("Nội dung mới", otherField.Value!.Summary);
        Assert.Equal(currentType.Id, otherField.Value.DocumentType.Id);

        var sameInactiveType = await service.UpdateAsync(
            created.Id,
            new IncomingDocumentUpdateRequest { DocumentTypeId = currentType.Id });
        Assert.True(sameInactiveType.Succeeded);

        var changed = await service.UpdateAsync(
            created.Id,
            new IncomingDocumentUpdateRequest { DocumentTypeId = nextType.Id });
        Assert.True(changed.Succeeded);
        Assert.Equal("PLAN", changed.Value!.DocumentType.Code);
    }

    [Fact]
    public async Task List_searches_filters_pages_and_uses_default_sort()
    {
        await using var database = await IncomingDocumentDatabase.CreateAsync();
        var report = await database.CreateDocumentTypeAsync("REPORT", isActive: true);
        var plan = await database.CreateDocumentTypeAsync("PLAN", isActive: true);
        var service = database.CreateService();

        var older = await service.CreateAsync(new IncomingDocumentCreateRequest
        {
            ReferenceNumber = "01/BC",
            SenderOrg = "UBND phường",
            Summary = "Kết quả cũ",
            ReceivedDate = new DateOnly(2026, 7, 1),
            Deadline = new DateOnly(2026, 7, 20),
            DocumentTypeId = report.Id
        });
        var newer = await service.CreateAsync(new IncomingDocumentCreateRequest
        {
            ReferenceNumber = "02/KH",
            SenderOrg = "Ủy ban MTTQ",
            Summary = "Kế hoạch đặc biệt",
            ReceivedDate = new DateOnly(2026, 7, 15),
            Deadline = new DateOnly(2026, 8, 10),
            DocumentTypeId = plan.Id
        });

        var search = await service.GetListAsync(new IncomingDocumentListQuery
        {
            Q = "  ĐẶC BIỆT  ",
            DocumentTypeId = plan.Id,
            DeadlineFrom = new DateOnly(2026, 8, 1),
            DeadlineTo = new DateOnly(2026, 8, 31),
            Page = 1,
            PageSize = 20
        });
        Assert.Single(search.Items);
        Assert.Equal(newer.Value!.Id, search.Items[0].Id);

        var firstPage = await service.GetListAsync(new IncomingDocumentListQuery
        {
            Page = 1,
            PageSize = 1
        });
        Assert.Equal(2, firstPage.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal(newer.Value.Id, firstPage.Items[0].Id);
        Assert.NotEqual(older.Value!.Id, firstPage.Items[0].Id);
    }

    [Fact]
    public async Task Complete_enforces_assignment_state_and_caller_and_uses_utc_time()
    {
        await using var database = await IncomingDocumentDatabase.CreateAsync();
        var type = await database.CreateDocumentTypeAsync("REPORT", isActive: true);
        var assigned = await database.CreateStaffAsync("Assigned staff");
        var confirmer = await database.CreateStaffAsync("Confirming clerk");
        var other = await database.CreateStaffAsync("Other staff");
        var service = database.CreateService();
        var created = (await service.CreateAsync(CreateRequest(type.Id))).Value!;

        var fromNew = await service.CompleteAsync(
            created.Id,
            confirmer.Id,
            callerIsClerk: true);
        Assert.Equal(IncomingDocumentFailure.Conflict, fromNew.Failure);

        var entity = await database.Context.IncomingDocuments.SingleAsync(
            document => document.Id == created.Id);
        entity.AssignedToStaffId = assigned.Id;
        entity.AssignmentConfirmedByStaffId = confirmer.Id;
        entity.AssignmentConfirmedAt = CompletionTime.AddHours(-1).UtcDateTime;
        entity.Status = IncomingDocumentStatus.Overdue;
        await database.Context.SaveChangesAsync();

        var forbidden = await service.CompleteAsync(
            created.Id,
            other.Id,
            callerIsClerk: false);
        Assert.Equal(IncomingDocumentFailure.Forbidden, forbidden.Failure);

        var completed = await service.CompleteAsync(
            created.Id,
            assigned.Id,
            callerIsClerk: false);
        Assert.True(completed.Succeeded);
        Assert.Equal(IncomingDocumentStatus.Completed, completed.Value!.Status);
        Assert.Equal(CompletionTime.UtcDateTime, completed.Value.CompletedAt);
        Assert.Equal("Assigned staff", completed.Value.AssignedToStaff!.FullName);

        var repeated = await service.CompleteAsync(
            created.Id,
            confirmer.Id,
            callerIsClerk: true);
        Assert.Equal(IncomingDocumentFailure.Conflict, repeated.Failure);

        var locked = await service.UpdateAsync(
            created.Id,
            new IncomingDocumentUpdateRequest { Summary = "Không được sửa" });
        Assert.Equal(IncomingDocumentFailure.Conflict, locked.Failure);
    }

    [Fact]
    public async Task Suggestion_replaces_only_suggestion_and_preserves_confirmed_assignment()
    {
        await using var database = await IncomingDocumentDatabase.CreateAsync();
        var type = await database.CreateDocumentTypeAsync("REPORT", isActive: true);
        var oldSuggestion = await database.CreateStaffAsync("Old suggestion");
        var newSuggestion = await database.CreateStaffAsync("New suggestion");
        var assigned = await database.CreateStaffAsync("Assigned staff");
        var confirmer = await database.CreateStaffAsync("Confirming clerk");
        var generator = new AssignmentSuggestionTestDouble
        {
            Handler = (_, _) => Task.FromResult(new AssignmentSuggestionDecision(
                AssignmentSuggestionDecisionKind.Suggested,
                newSuggestion.Id,
                "Phù hợp lĩnh vực báo cáo."))
        };
        var service = database.CreateService(generator);
        var created = (await service.CreateAsync(CreateRequest(type.Id))).Value!;
        var entity = await database.Context.IncomingDocuments.SingleAsync(
            document => document.Id == created.Id);
        entity.SuggestedStaffId = oldSuggestion.Id;
        entity.AssignmentSuggestionReason = "Gợi ý cũ";
        entity.AssignmentSuggestedAt = CompletionTime.AddDays(-1).UtcDateTime;
        entity.AssignedToStaffId = assigned.Id;
        entity.AssignmentConfirmedByStaffId = confirmer.Id;
        entity.AssignmentConfirmedAt = CompletionTime.AddHours(-1).UtcDateTime;
        entity.Status = IncomingDocumentStatus.InProgress;
        await database.Context.SaveChangesAsync();

        var result = await service.SuggestAssignmentAsync(created.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(newSuggestion.Id, result.Value!.SuggestedStaff!.Id);
        Assert.Equal("Phù hợp lĩnh vực báo cáo.", result.Value.Reason);
        Assert.Null(result.Value.Confidence);
        Assert.Equal(CompletionTime.UtcDateTime, result.Value.SuggestedAt);
        var persisted = await database.Context.IncomingDocuments
            .AsNoTracking()
            .SingleAsync(document => document.Id == created.Id);
        Assert.Equal(newSuggestion.Id, persisted.SuggestedStaffId);
        Assert.Equal(assigned.Id, persisted.AssignedToStaffId);
        Assert.Equal(confirmer.Id, persisted.AssignmentConfirmedByStaffId);
        Assert.Equal(IncomingDocumentStatus.InProgress, persisted.Status);
        Assert.Equal(1, generator.CallCount);
        Assert.Equal("REPORT", generator.LastInput!.DocumentTypeCode);
    }

    [Fact]
    public async Task Insufficient_evidence_clears_previous_suggestion_without_assigning()
    {
        await using var database = await IncomingDocumentDatabase.CreateAsync();
        var type = await database.CreateDocumentTypeAsync("REPORT", isActive: true);
        var oldSuggestion = await database.CreateStaffAsync("Old suggestion");
        var service = database.CreateService();
        var created = (await service.CreateAsync(CreateRequest(type.Id))).Value!;
        var entity = await database.Context.IncomingDocuments.SingleAsync(
            document => document.Id == created.Id);
        entity.SuggestedStaffId = oldSuggestion.Id;
        entity.AssignmentSuggestionReason = "Gợi ý cũ";
        entity.AssignmentConfidence = 0.75m;
        entity.AssignmentSuggestedAt = CompletionTime.AddDays(-1).UtcDateTime;
        await database.Context.SaveChangesAsync();

        var result = await service.SuggestAssignmentAsync(created.Id);

        Assert.True(result.Succeeded);
        Assert.Null(result.Value!.SuggestedStaff);
        Assert.Equal("Không đủ bằng chứng.", result.Value.Reason);
        var persisted = await database.Context.IncomingDocuments
            .AsNoTracking()
            .SingleAsync(document => document.Id == created.Id);
        Assert.Null(persisted.SuggestedStaffId);
        Assert.Null(persisted.AssignmentSuggestionReason);
        Assert.Null(persisted.AssignmentConfidence);
        Assert.Null(persisted.AssignmentSuggestedAt);
        Assert.Null(persisted.AssignedToStaffId);
        Assert.Equal(IncomingDocumentStatus.New, persisted.Status);
    }

    [Fact]
    public async Task Ai_failure_returns_unavailable_without_mutating_workflow()
    {
        await using var database = await IncomingDocumentDatabase.CreateAsync();
        var type = await database.CreateDocumentTypeAsync("REPORT", isActive: true);
        var oldSuggestion = await database.CreateStaffAsync("Old suggestion");
        var generator = new AssignmentSuggestionTestDouble
        {
            Handler = (_, _) => throw new AiProviderException("provider unavailable")
        };
        var service = database.CreateService(generator);
        var created = (await service.CreateAsync(CreateRequest(type.Id))).Value!;
        var entity = await database.Context.IncomingDocuments.SingleAsync(
            document => document.Id == created.Id);
        entity.SuggestedStaffId = oldSuggestion.Id;
        entity.AssignmentSuggestionReason = "Gợi ý đang lưu";
        entity.AssignmentSuggestedAt = CompletionTime.AddHours(-1).UtcDateTime;
        await database.Context.SaveChangesAsync();
        var before = await database.Context.IncomingDocuments
            .AsNoTracking()
            .SingleAsync(document => document.Id == created.Id);

        var result = await service.SuggestAssignmentAsync(created.Id);

        Assert.Equal(IncomingDocumentFailure.ServiceUnavailable, result.Failure);
        var after = await database.Context.IncomingDocuments
            .AsNoTracking()
            .SingleAsync(document => document.Id == created.Id);
        Assert.Equal(before.SuggestedStaffId, after.SuggestedStaffId);
        Assert.Equal(before.AssignmentSuggestionReason, after.AssignmentSuggestionReason);
        Assert.Equal(before.AssignmentSuggestedAt, after.AssignmentSuggestedAt);
        Assert.Equal(before.AssignedToStaffId, after.AssignedToStaffId);
        Assert.Equal(before.Status, after.Status);
    }

    [Fact]
    public async Task Confirm_assignment_requires_active_staff_and_preserves_overdue_on_reassignment()
    {
        await using var database = await IncomingDocumentDatabase.CreateAsync();
        var type = await database.CreateDocumentTypeAsync("REPORT", isActive: true);
        var firstAssignee = await database.CreateStaffAsync("First assignee");
        var nextAssignee = await database.CreateStaffAsync("Next assignee");
        var inactive = await database.CreateStaffAsync("Inactive staff", isActive: false);
        var confirmer = await database.CreateStaffAsync("Confirming clerk");
        var service = database.CreateService();
        var created = (await service.CreateAsync(CreateRequest(type.Id))).Value!;

        var inactiveResult = await service.ConfirmAssignmentAsync(
            created.Id,
            new AssignmentConfirmRequest(inactive.Id),
            confirmer.Id);
        Assert.Equal(IncomingDocumentFailure.Validation, inactiveResult.Failure);

        var first = await service.ConfirmAssignmentAsync(
            created.Id,
            new AssignmentConfirmRequest(firstAssignee.Id),
            confirmer.Id);
        Assert.True(first.Succeeded);
        Assert.Equal(IncomingDocumentStatus.InProgress, first.Value!.Status);
        Assert.Equal(firstAssignee.Id, first.Value.AssignedToStaff!.Id);
        Assert.Equal(confirmer.Id, first.Value.AssignmentConfirmedBy!.Id);

        var entity = await database.Context.IncomingDocuments.SingleAsync(
            document => document.Id == created.Id);
        entity.Status = IncomingDocumentStatus.Overdue;
        await database.Context.SaveChangesAsync();
        var reassigned = await service.ConfirmAssignmentAsync(
            created.Id,
            new AssignmentConfirmRequest(nextAssignee.Id),
            confirmer.Id);
        Assert.True(reassigned.Succeeded);
        Assert.Equal(IncomingDocumentStatus.Overdue, reassigned.Value!.Status);
        Assert.Equal(nextAssignee.Id, reassigned.Value.AssignedToStaff!.Id);
        Assert.Equal(CompletionTime.UtcDateTime, reassigned.Value.AssignmentConfirmedAt);
    }

    private static IncomingDocumentCreateRequest CreateRequest(Guid documentTypeId) =>
        new()
        {
            ReferenceNumber = "01/BC-MTTQ",
            SenderOrg = "UBND phường",
            Summary = "Báo cáo công tác",
            ReceivedDate = new DateOnly(2026, 7, 30),
            Deadline = new DateOnly(2026, 8, 5),
            DocumentTypeId = documentTypeId
        };

    private sealed class IncomingDocumentDatabase : IAsyncDisposable
    {
        private IncomingDocumentDatabase(
            SqliteConnection connection,
            DigitalOpsDbContext context)
        {
            Connection = connection;
            Context = context;
        }

        public SqliteConnection Connection { get; }

        public DigitalOpsDbContext Context { get; }

        public static async Task<IncomingDocumentDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<DigitalOpsDbContext>()
                .UseSqlite(connection)
                .ReplaceService<IModelCustomizer, AuthenticationTestModelCustomizer>()
                .Options;
            var context = new DigitalOpsDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new IncomingDocumentDatabase(connection, context);
        }

        public IncomingDocumentService CreateService(
            AssignmentSuggestionTestDouble? suggestionGenerator = null) =>
            new(
                Context,
                new FixedTimeProvider(CompletionTime),
                suggestionGenerator ?? new AssignmentSuggestionTestDouble(),
                NullLogger<IncomingDocumentService>.Instance);

        public async Task<DocumentType> CreateDocumentTypeAsync(
            string code,
            bool isActive)
        {
            var documentType = new DocumentType
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = code,
                IsActive = isActive
            };
            Context.DocumentTypes.Add(documentType);
            await Context.SaveChangesAsync();
            return documentType;
        }

        public async Task<Staff> CreateStaffAsync(
            string fullName,
            bool isActive = true)
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
                IsActive = isActive
            };
            Context.Staff.Add(staff);
            await Context.SaveChangesAsync();
            return staff;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

public sealed class IncomingDocumentApiTests
{
    private const string Password = "Valid1!Password";

    [Fact]
    public async Task Endpoints_enforce_auth_roles_and_password_change_boundary()
    {
        using var factory = new StaffManagementApiFactory();
        using var anonymous = factory.CreateApiClient();
        await ProblemDetailsAssert.HasContractAsync(
            await anonymous.GetAsync("/api/v1/incoming-documents"),
            HttpStatusCode.Unauthorized,
            "unauthorized",
            "/api/v1/incoming-documents");

        using var admin = factory.CreateApiClient();
        await AuthenticateAsync(admin, "admin");
        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.GetAsync("/api/v1/incoming-documents")).StatusCode);
        await ProblemDetailsAssert.HasContractAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/incoming-documents",
                new { referenceNumber = "01", senderOrg = "A" }),
            HttpStatusCode.Forbidden,
            "forbidden",
            "/api/v1/incoming-documents");

        using var forced = factory.CreateApiClient();
        await AuthenticateAsync(forced, "forcedadmin");
        await ProblemDetailsAssert.HasContractAsync(
            await forced.GetAsync("/api/v1/incoming-documents"),
            HttpStatusCode.Forbidden,
            "password-change-required",
            "/api/v1/incoming-documents");
    }

    [Fact]
    public async Task Clerk_crud_returns_contract_validation_not_found_and_conflict()
    {
        using var factory = new StaffManagementApiFactory();
        var documentType = await CreateTypeAsync(factory, isActive: true);
        using var clerk = factory.CreateApiClient();
        await AuthenticateAsync(clerk, "clerk");

        var create = await clerk.PostAsJsonAsync(
            "/api/v1/incoming-documents",
            CreatePayload(documentType.Id));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.NotNull(create.Headers.Location);
        var created = (await create.Content.ReadFromJsonAsync<IncomingDocumentResponse>())!;
        Assert.Equal("REPORT", created.DocumentType.Code);
        Assert.Equal(IncomingDocumentStatus.New, created.Status);
        Assert.Empty(created.Attachments);

        var list = await clerk.GetFromJsonAsync<PagedResponse<IncomingDocumentResponse>>(
            "/api/v1/incoming-documents?q=b%C3%A1o%20c%C3%A1o&status=New&deadlineFrom=2026-08-01&deadlineTo=2026-08-31&page=1&pageSize=20");
        Assert.Single(list!.Items);

        var emptyPatch = await clerk.PatchAsJsonAsync(
            $"/api/v1/incoming-documents/{created.Id}",
            new { });
        Assert.Equal(HttpStatusCode.BadRequest, emptyPatch.StatusCode);

        var invalidPatch = await clerk.PatchAsJsonAsync(
            $"/api/v1/incoming-documents/{created.Id}",
            new { receivedDate = "2026-09-01" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidPatch.StatusCode);

        var updated = await clerk.PatchAsJsonAsync(
            $"/api/v1/incoming-documents/{created.Id}",
            new { summary = "  Nội dung cập nhật  " });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(
            "Nội dung cập nhật",
            (await updated.Content.ReadFromJsonAsync<IncomingDocumentResponse>())!.Summary);

        var missingId = Guid.NewGuid();
        await ProblemDetailsAssert.HasContractAsync(
            await clerk.GetAsync($"/api/v1/incoming-documents/{missingId}"),
            HttpStatusCode.NotFound,
            "not-found",
            $"/api/v1/incoming-documents/{missingId}");

        await SetWorkflowAsync(
            factory,
            created.Id,
            assignedUserName: "clerk",
            status: IncomingDocumentStatus.Completed,
            completedAt: DateTime.UtcNow);
        await ProblemDetailsAssert.HasContractAsync(
            await clerk.PatchAsJsonAsync(
                $"/api/v1/incoming-documents/{created.Id}",
                new { summary = "Không sửa" }),
            HttpStatusCode.Conflict,
            "conflict",
            $"/api/v1/incoming-documents/{created.Id}");
    }

    [Fact]
    public async Task Clerk_or_assigned_staff_can_complete_and_other_staff_is_forbidden()
    {
        using var factory = new StaffManagementApiFactory();
        var type = await CreateTypeAsync(factory, isActive: true);
        using var clerk = factory.CreateApiClient();
        await AuthenticateAsync(clerk, "clerk");
        var create = await clerk.PostAsJsonAsync(
            "/api/v1/incoming-documents",
            CreatePayload(type.Id));
        var created = (await create.Content.ReadFromJsonAsync<IncomingDocumentResponse>())!;

        await SetWorkflowAsync(
            factory,
            created.Id,
            assignedUserName: "admin",
            status: IncomingDocumentStatus.InProgress,
            completedAt: null);

        using var otherAdmin = factory.CreateApiClient();
        await AuthenticateAsync(otherAdmin, "admin");
        var assignedCompletion = await otherAdmin.PostAsync(
            $"/api/v1/incoming-documents/{created.Id}/complete",
            content: null);
        Assert.Equal(HttpStatusCode.OK, assignedCompletion.StatusCode);
        var completed = (await assignedCompletion.Content
            .ReadFromJsonAsync<IncomingDocumentResponse>())!;
        Assert.Equal(IncomingDocumentStatus.Completed, completed.Status);
        Assert.NotNull(completed.CompletedAt);
        Assert.Equal("A Administrator", completed.AssignedToStaff!.FullName);

        await ProblemDetailsAssert.HasContractAsync(
            await clerk.PostAsync(
                $"/api/v1/incoming-documents/{created.Id}/complete",
                content: null),
            HttpStatusCode.Conflict,
            "conflict",
            $"/api/v1/incoming-documents/{created.Id}/complete");

        var secondCreate = await clerk.PostAsJsonAsync(
            "/api/v1/incoming-documents",
            CreatePayload(type.Id, "02/BC"));
        var second = (await secondCreate.Content.ReadFromJsonAsync<IncomingDocumentResponse>())!;
        await SetWorkflowAsync(
            factory,
            second.Id,
            assignedUserName: "clerk",
            status: IncomingDocumentStatus.Overdue,
            completedAt: null);

        await ProblemDetailsAssert.HasContractAsync(
            await otherAdmin.PostAsync(
                $"/api/v1/incoming-documents/{second.Id}/complete",
                content: null),
            HttpStatusCode.Forbidden,
            "forbidden",
            $"/api/v1/incoming-documents/{second.Id}/complete");
    }

    [Fact]
    public async Task Assignment_endpoints_enforce_clerk_and_preserve_workflow_on_ai_failure()
    {
        using var factory = new StaffManagementApiFactory();
        var type = await CreateTypeAsync(factory, isActive: true);
        var suggestedStaff = await factory.FindStaffAsync("admin");
        factory.AssignmentSuggestionGenerator.Handler = (_, _) =>
            Task.FromResult(new AssignmentSuggestionDecision(
                AssignmentSuggestionDecisionKind.Suggested,
                suggestedStaff.Id,
                "Phù hợp nội dung báo cáo."));

        using var clerk = factory.CreateApiClient();
        await AuthenticateAsync(clerk, "clerk");
        var create = await clerk.PostAsJsonAsync(
            "/api/v1/incoming-documents",
            CreatePayload(type.Id));
        var created = (await create.Content.ReadFromJsonAsync<IncomingDocumentResponse>())!;

        var suggestionResponse = await clerk.PostAsync(
            $"/api/v1/incoming-documents/{created.Id}/assignment-suggestion",
            content: null);
        Assert.Equal(HttpStatusCode.OK, suggestionResponse.StatusCode);
        var suggestion = (await suggestionResponse.Content
            .ReadFromJsonAsync<AssignmentSuggestionResponse>())!;
        Assert.Equal(suggestedStaff.Id, suggestion.SuggestedStaff!.Id);
        Assert.Null(suggestion.Confidence);

        var assignmentResponse = await clerk.PostAsJsonAsync(
            $"/api/v1/incoming-documents/{created.Id}/assignment",
            new AssignmentConfirmRequest(suggestedStaff.Id));
        Assert.Equal(HttpStatusCode.OK, assignmentResponse.StatusCode);
        var assigned = (await assignmentResponse.Content
            .ReadFromJsonAsync<IncomingDocumentResponse>())!;
        Assert.Equal(IncomingDocumentStatus.InProgress, assigned.Status);
        Assert.Equal(suggestedStaff.Id, assigned.AssignedToStaff!.Id);
        Assert.Equal("B Clerk", assigned.AssignmentConfirmedBy!.FullName);
        Assert.Equal(suggestedStaff.Id, assigned.SuggestedStaff!.Id);

        using var administrator = factory.CreateApiClient();
        await AuthenticateAsync(administrator, "admin");
        await ProblemDetailsAssert.HasContractAsync(
            await administrator.PostAsync(
                $"/api/v1/incoming-documents/{created.Id}/assignment-suggestion",
                content: null),
            HttpStatusCode.Forbidden,
            "forbidden",
            $"/api/v1/incoming-documents/{created.Id}/assignment-suggestion");

        factory.AssignmentSuggestionGenerator.Handler = (_, _) =>
            throw new AiProviderException("provider unavailable");
        await ProblemDetailsAssert.HasContractAsync(
            await clerk.PostAsync(
                $"/api/v1/incoming-documents/{created.Id}/assignment-suggestion",
                content: null),
            HttpStatusCode.ServiceUnavailable,
            "ai-service-unavailable",
            $"/api/v1/incoming-documents/{created.Id}/assignment-suggestion");
        var unchanged = await clerk.GetFromJsonAsync<IncomingDocumentResponse>(
            $"/api/v1/incoming-documents/{created.Id}");
        Assert.Equal(suggestedStaff.Id, unchanged!.SuggestedStaff!.Id);
        Assert.Equal(suggestedStaff.Id, unchanged.AssignedToStaff!.Id);
        Assert.Equal(IncomingDocumentStatus.InProgress, unchanged.Status);
    }

    private static object CreatePayload(Guid documentTypeId, string reference = "01/BC") =>
        new
        {
            referenceNumber = reference,
            senderOrg = "UBND phường",
            summary = "Báo cáo tháng",
            receivedDate = "2026-07-30",
            deadline = "2026-08-05",
            documentTypeId
        };

    private static async Task<DocumentType> CreateTypeAsync(
        StaffManagementApiFactory factory,
        bool isActive)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DigitalOpsDbContext>();
        var documentType = new DocumentType
        {
            Id = Guid.NewGuid(),
            Code = "REPORT",
            Name = "Báo cáo",
            IsActive = isActive
        };
        dbContext.DocumentTypes.Add(documentType);
        await dbContext.SaveChangesAsync();
        return documentType;
    }

    private static async Task SetWorkflowAsync(
        StaffManagementApiFactory factory,
        Guid documentId,
        string assignedUserName,
        IncomingDocumentStatus status,
        DateTime? completedAt)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DigitalOpsDbContext>();
        var assigned = await dbContext.Staff
            .Include(staff => staff.IdentityUser)
            .SingleAsync(staff => staff.IdentityUser.UserName == assignedUserName);
        var confirmer = await dbContext.Staff
            .Include(staff => staff.IdentityUser)
            .SingleAsync(staff => staff.IdentityUser.UserName == "clerk");
        var document = await dbContext.IncomingDocuments.SingleAsync(
            item => item.Id == documentId);
        document.AssignedToStaffId = assigned.Id;
        document.AssignmentConfirmedByStaffId = confirmer.Id;
        document.AssignmentConfirmedAt = DateTime.UtcNow;
        document.Status = status;
        document.CompletedAt = completedAt;
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
}
