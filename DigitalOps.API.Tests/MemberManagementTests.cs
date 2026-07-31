using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalOps.API.Features.Authentication;
using DigitalOps.API.Features.Members;
using DigitalOps.API.Features.StaffManagement;
using DigitalOps.API.Shared.Api;
using DigitalOps.API.Shared.Data;
using DigitalOps.API.Shared.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalOps.API.Tests;

public sealed class MemberManagementServiceTests
{
    [Fact]
    public async Task Create_normalizes_member_and_always_uses_active_status()
    {
        await using var database = await MemberServiceDatabase.CreateAsync();
        var service = new MemberManagementService(database.Context);
        var request = new MemberUpsertRequest
        {
            FullName = "  Nguyễn   Văn   An ",
            Gender = "Male",
            Phone = " 0901  234 567 ",
            Email = " MEMBER@EXAMPLE.COM ",
            Position = "  Chi hội trưởng ",
            Address = "  Phường 1 ",
            Notes = "  Ghi chú "
        };

        var result = await service.CreateAsync(request);

        Assert.True(result.Succeeded);
        Assert.Equal("Nguyễn Văn An", result.Value!.FullName);
        Assert.Equal("0901 234 567", result.Value.Phone);
        Assert.Equal("member@example.com", result.Value.Email);
        Assert.Equal("Chi hội trưởng", result.Value.Position);
        Assert.Equal(MemberStatus.Active, result.Value.Status);
        Assert.NotEqual(Guid.Empty, result.Value.Id);
        Assert.Single(await database.Context.Members.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Patch_distinguishes_omitted_fields_from_null_and_reactivates()
    {
        await using var database = await MemberServiceDatabase.CreateAsync();
        var member = new Member
        {
            Id = Guid.NewGuid(),
            FullName = "Hội viên cũ",
            Address = "Địa chỉ cũ",
            Position = "Chức vụ giữ nguyên",
            Status = MemberStatus.Inactive
        };
        database.Context.Members.Add(member);
        await database.Context.SaveChangesAsync();
        var service = new MemberManagementService(database.Context);

        var result = await service.UpdateAsync(
            member.Id,
            new MemberUpsertRequest
            {
                FullName = "  Hội   viên mới ",
                Address = null,
                Status = MemberStatus.Active
            });

        Assert.True(result.Succeeded);
        Assert.Equal("Hội viên mới", result.Value!.FullName);
        Assert.Null(result.Value.Address);
        Assert.Equal("Chức vụ giữ nguyên", result.Value.Position);
        Assert.Equal(MemberStatus.Active, result.Value.Status);
    }

    [Fact]
    public async Task Invalid_status_is_rejected_and_deactivate_is_idempotency_safe()
    {
        await using var database = await MemberServiceDatabase.CreateAsync();
        var member = new Member
        {
            Id = Guid.NewGuid(),
            FullName = "Hội viên Active",
            Status = MemberStatus.Active
        };
        database.Context.Members.Add(member);
        await database.Context.SaveChangesAsync();
        var service = new MemberManagementService(database.Context);

        var invalidPatch = await service.UpdateAsync(
            member.Id,
            new MemberUpsertRequest { Status = MemberStatus.Inactive });
        Assert.Equal(MemberServiceFailure.Validation, invalidPatch.Failure);
        Assert.Contains("status", invalidPatch.Errors.Keys);

        var firstDeactivate = await service.DeactivateAsync(member.Id);
        var secondDeactivate = await service.DeactivateAsync(member.Id);

        Assert.True(firstDeactivate.Succeeded);
        Assert.Equal(MemberStatus.Inactive, firstDeactivate.Value!.Status);
        Assert.Equal(MemberServiceFailure.Conflict, secondDeactivate.Failure);
        Assert.Single(await database.Context.Members.AsNoTracking().ToListAsync());
    }

    private sealed class MemberServiceDatabase : IAsyncDisposable
    {
        private MemberServiceDatabase(
            SqliteConnection connection,
            DigitalOpsDbContext context)
        {
            Connection = connection;
            Context = context;
        }

        public SqliteConnection Connection { get; }

        public DigitalOpsDbContext Context { get; }

        public static async Task<MemberServiceDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<DigitalOpsDbContext>()
                .UseSqlite(connection)
                .ReplaceService<
                    IModelCustomizer,
                    AuthenticationTestModelCustomizer>()
                .Options;
            var context = new DigitalOpsDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new MemberServiceDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}

public sealed class MemberManagementApiTests
{
    private const string Password = "Valid1!Password";
    private const string TemporaryPassword = "Temporary2!Password";
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task Member_endpoints_enforce_business_access_and_role_boundaries()
    {
        using var factory = new StaffManagementApiFactory();

        using var anonymousClient = factory.CreateApiClient();
        await ProblemDetailsAssert.HasContractAsync(
            await anonymousClient.GetAsync("/api/v1/members"),
            HttpStatusCode.Unauthorized,
            "unauthorized",
            "/api/v1/members");

        using var adminClient = factory.CreateApiClient();
        await AuthenticateAsync(adminClient, "admin");
        Assert.Equal(
            HttpStatusCode.OK,
            (await adminClient.GetAsync("/api/v1/members")).StatusCode);

        using var clerkClient = factory.CreateApiClient();
        await AuthenticateAsync(clerkClient, "clerk");
        Assert.Equal(
            HttpStatusCode.OK,
            (await clerkClient.GetAsync("/api/v1/members")).StatusCode);

        var createDrafter = await adminClient.PostAsJsonAsync(
            "/api/v1/staff",
            new StaffCreateRequest(
                "member.drafter",
                "member.drafter@digitalops.local",
                TemporaryPassword,
                "Member Drafter",
                null,
                null,
                null,
                [SystemRoles.Drafter]));
        Assert.Equal(HttpStatusCode.Created, createDrafter.StatusCode);

        using var drafterClient = factory.CreateApiClient();
        await AuthenticateAsync(drafterClient, "member.drafter", TemporaryPassword);
        await CompletePasswordChangeAsync(
            drafterClient,
            TemporaryPassword,
            Password);
        await ProblemDetailsAssert.HasContractAsync(
            await drafterClient.GetAsync("/api/v1/members"),
            HttpStatusCode.Forbidden,
            "forbidden",
            "/api/v1/members");
        Assert.Equal(
            HttpStatusCode.OK,
            (await drafterClient.GetAsync("/api/v1/members/lookup")).StatusCode);

        using var forcedClient = factory.CreateApiClient();
        await AuthenticateAsync(forcedClient, "forcedadmin");
        await ProblemDetailsAssert.HasContractAsync(
            await forcedClient.GetAsync("/api/v1/members"),
            HttpStatusCode.Forbidden,
            "password-change-required",
            "/api/v1/members");
    }

    [Fact]
    public async Task List_searches_supported_fields_filters_status_and_pages()
    {
        using var factory = new StaffManagementApiFactory();
        await SeedMembersAsync(
            factory,
            new Member
            {
                Id = Guid.NewGuid(),
                FullName = "An Alpha",
                Phone = "0901 111 111",
                Email = "alpha@example.local",
                Status = MemberStatus.Active
            },
            new Member
            {
                Id = Guid.NewGuid(),
                FullName = "Binh Beta",
                Phone = "0902 222 222",
                Email = "beta@example.local",
                Status = MemberStatus.Inactive
            },
            new Member
            {
                Id = Guid.NewGuid(),
                FullName = "Chi Gamma",
                Phone = "0903 333 333",
                Email = "gamma@example.local",
                Status = MemberStatus.Active
            });
        using var client = factory.CreateApiClient();
        await AuthenticateAsync(client, "admin");

        var firstPage = await ReadAsync<PagedResponse<MemberResponse>>(
            await client.GetAsync("/api/v1/members?page=1&pageSize=2"));
        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal(["An Alpha", "Binh Beta"], firstPage.Items.Select(x => x.FullName));

        var byName = await GetMembersAsync(client, "q=ALPHA");
        Assert.Single(byName.Items);
        Assert.Equal("An Alpha", byName.Items[0].FullName);

        var byPhone = await GetMembersAsync(client, "q=0902");
        Assert.Single(byPhone.Items);
        Assert.Equal("Binh Beta", byPhone.Items[0].FullName);

        var byEmail = await GetMembersAsync(client, "q=GAMMA%40EXAMPLE.LOCAL");
        Assert.Single(byEmail.Items);
        Assert.Equal("Chi Gamma", byEmail.Items[0].FullName);

        var inactive = await GetMembersAsync(client, "status=Inactive");
        Assert.Single(inactive.Items);
        Assert.Equal(MemberStatus.Inactive, inactive.Items[0].Status);

        var empty = await GetMembersAsync(client, "q=khong-co");
        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.TotalPages);
    }

    [Fact]
    public async Task Create_patch_deactivate_and_reactivate_follow_member_rules()
    {
        using var factory = new StaffManagementApiFactory();
        using var client = factory.CreateApiClient();
        await AuthenticateAsync(client, "admin");

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/members",
            new
            {
                fullName = "  Nguyễn   Văn   Minh ",
                gender = "Male",
                address = "Địa chỉ ban đầu",
                phone = "0901 234 567",
                email = "MEMBER@EXAMPLE.COM",
                position = "Hội viên",
                joinDate = "2026-07-01",
                notes = "Ghi chú"
            });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await ReadAsync<MemberResponse>(createResponse);
        Assert.Equal(
            $"/api/v1/members/{created.Id}",
            createResponse.Headers.Location?.AbsolutePath);
        Assert.Equal("Nguyễn Văn Minh", created.FullName);
        Assert.Equal("member@example.com", created.Email);
        Assert.Equal(MemberStatus.Active, created.Status);

        var inactiveCreate = await client.PostAsJsonAsync(
            "/api/v1/members",
            new { fullName = "Không hợp lệ", status = "Inactive" });
        await AssertValidationFieldAsync(inactiveCreate, "status");

        var invalidContact = await client.PostAsJsonAsync(
            "/api/v1/members",
            new { fullName = "Liên hệ lỗi", email = "khong-phai-email" });
        await AssertValidationFieldAsync(invalidContact, "email");

        await Task.Delay(10);
        var patchResponse = await client.PatchAsJsonAsync(
            $"/api/v1/members/{created.Id}",
            new { fullName = "Hội viên cập nhật", address = (string?)null });
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        var patched = await ReadAsync<MemberResponse>(patchResponse);
        Assert.Equal("Hội viên cập nhật", patched.FullName);
        Assert.Null(patched.Address);
        Assert.Equal("Hội viên", patched.Position);
        Assert.True(patched.UpdatedAt > created.UpdatedAt);

        var invalidPatch = await client.PatchAsJsonAsync(
            $"/api/v1/members/{created.Id}",
            new { status = "Inactive" });
        await AssertValidationFieldAsync(invalidPatch, "status");

        var deactivateResponse = await client.PostAsync(
            $"/api/v1/members/{created.Id}/deactivate",
            null);
        Assert.Equal(HttpStatusCode.OK, deactivateResponse.StatusCode);
        var deactivated = await ReadAsync<MemberResponse>(deactivateResponse);
        Assert.Equal(MemberStatus.Inactive, deactivated.Status);

        var detail = await ReadAsync<MemberResponse>(
            await client.GetAsync($"/api/v1/members/{created.Id}"));
        Assert.Equal(MemberStatus.Inactive, detail.Status);

        var lookup = await ReadAsync<PagedResponse<MemberLookupResponse>>(
            await client.GetAsync("/api/v1/members/lookup?q=H%E1%BB%99i"));
        Assert.DoesNotContain(lookup.Items, item => item.Id == created.Id);

        await ProblemDetailsAssert.HasContractAsync(
            await client.PostAsync(
                $"/api/v1/members/{created.Id}/deactivate",
                null),
            HttpStatusCode.Conflict,
            "conflict",
            $"/api/v1/members/{created.Id}/deactivate");

        var reactivateResponse = await client.PatchAsJsonAsync(
            $"/api/v1/members/{created.Id}",
            new { status = "Active" });
        Assert.Equal(HttpStatusCode.OK, reactivateResponse.StatusCode);
        Assert.Equal(
            MemberStatus.Active,
            (await ReadAsync<MemberResponse>(reactivateResponse)).Status);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DigitalOpsDbContext>();
        Assert.Equal(1, await dbContext.Members.CountAsync(x => x.Id == created.Id));

        var missingId = Guid.NewGuid();
        await ProblemDetailsAssert.HasContractAsync(
            await client.GetAsync($"/api/v1/members/{missingId}"),
            HttpStatusCode.NotFound,
            "not-found",
            $"/api/v1/members/{missingId}");
    }

    private static async Task<PagedResponse<MemberResponse>> GetMembersAsync(
        HttpClient client,
        string query) =>
        await ReadAsync<PagedResponse<MemberResponse>>(
            await client.GetAsync($"/api/v1/members?{query}"));

    private static async Task AuthenticateAsync(
        HttpClient client,
        string userName,
        string password = Password)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(userName, password));
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }

    private static async Task CompletePasswordChangeAsync(
        HttpClient client,
        string currentPassword,
        string newPassword)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/change-password",
            new ChangePasswordRequest(currentPassword, newPassword));
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }

    private static async Task SeedMembersAsync(
        StaffManagementApiFactory factory,
        params Member[] members)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DigitalOpsDbContext>();
        dbContext.Members.AddRange(members);
        await dbContext.SaveChangesAsync();
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
        Assert.NotNull(result);
        return result;
    }

    private static async Task AssertValidationFieldAsync(
        HttpResponseMessage response,
        string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(
            document.RootElement
                .GetProperty("errors")
                .TryGetProperty(field, out var errors));
        Assert.NotEmpty(errors.EnumerateArray());
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
