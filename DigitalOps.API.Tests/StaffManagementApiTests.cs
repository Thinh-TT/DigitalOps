using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalOps.API.Features.Attachments;
using DigitalOps.API.Features.Authentication;
using DigitalOps.API.Features.Drafting;
using DigitalOps.API.Features.IncomingDocuments;
using DigitalOps.API.Features.StaffManagement;
using DigitalOps.API.Shared.Api;
using DigitalOps.API.Shared.Data;
using DigitalOps.API.Shared.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DigitalOps.API.Tests;

public sealed class StaffManagementApiTests
{
    private const string Password = "Valid1!Password";
    private const string TemporaryPassword = "Temporary2!Password";

    [Fact]
    public async Task Administrator_can_page_all_staff_and_clerk_can_only_read_active_staff()
    {
        using var factory = new StaffManagementApiFactory();
        using var adminClient = factory.CreateApiClient();
        await AuthenticateAsync(adminClient, "admin", Password);

        var firstPage = await adminClient.GetFromJsonAsync<PagedResponse<StaffResponse>>(
            "/api/v1/staff?page=1&pageSize=2");

        Assert.NotNull(firstPage);
        Assert.Equal(5, firstPage.TotalCount);
        Assert.Equal(3, firstPage.TotalPages);
        Assert.Equal(
            firstPage.Items.OrderBy(item => item.FullName).Select(item => item.Id),
            firstPage.Items.Select(item => item.Id));

        using var clerkClient = factory.CreateApiClient();
        await AuthenticateAsync(clerkClient, "clerk", Password);

        var activeResponse = await clerkClient.GetAsync(
            "/api/v1/staff?activeOnly=true");
        Assert.Equal(HttpStatusCode.OK, activeResponse.StatusCode);
        var activeStaff = await activeResponse.Content
            .ReadFromJsonAsync<PagedResponse<StaffResponse>>();
        Assert.NotNull(activeStaff);
        Assert.All(activeStaff.Items, staff => Assert.True(staff.IsActive));

        await ProblemDetailsAssert.HasContractAsync(
            await clerkClient.GetAsync("/api/v1/staff"),
            HttpStatusCode.Forbidden,
            "forbidden",
            "/api/v1/staff");

        using var anonymousClient = factory.CreateApiClient();
        await ProblemDetailsAssert.HasContractAsync(
            await anonymousClient.GetAsync("/api/v1/staff?activeOnly=true"),
            HttpStatusCode.Unauthorized,
            "unauthorized",
            "/api/v1/staff");

        using var forcedClient = factory.CreateApiClient();
        await AuthenticateAsync(forcedClient, "forcedadmin", Password);
        await ProblemDetailsAssert.HasContractAsync(
            await forcedClient.GetAsync("/api/v1/staff?activeOnly=true"),
            HttpStatusCode.Forbidden,
            "password-change-required",
            "/api/v1/staff");

        var listBody = await adminClient.GetStringAsync("/api/v1/staff");
        Assert.DoesNotContain(
            "passwordHash",
            listBody,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "temporaryPassword",
            listBody,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_staff_is_transactional_and_supports_multiple_roles()
    {
        using var factory = new StaffManagementApiFactory();
        using var client = factory.CreateApiClient();
        await AuthenticateAsync(client, "admin", Password);

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/staff",
            new StaffCreateRequest(
                "new.multi",
                "new.multi@digitalops.local",
                TemporaryPassword,
                "Nhân sự đa vai trò",
                "Chuyên viên",
                "Văn phòng",
                "0901000000",
                [SystemRoles.Clerk, SystemRoles.Leader]));

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createResponse.Headers.Location);
        var created = await createResponse.Content.ReadFromJsonAsync<StaffResponse>();
        Assert.NotNull(created);
        Assert.Equal(
            [SystemRoles.Clerk, SystemRoles.Leader],
            created.Roles);

        using var loginClient = factory.CreateApiClient();
        var login = await LoginAsync(loginClient, "new.multi", TemporaryPassword);
        Assert.True(login.MustChangePassword);
        Assert.Equal(created.Id, login.Staff.Id);
        Assert.Equal(created.Roles, login.Roles);

        var duplicateResponse = await client.PostAsJsonAsync(
            "/api/v1/staff",
            new StaffCreateRequest(
                "new.multi",
                "other@digitalops.local",
                TemporaryPassword,
                "Trùng username",
                null,
                null,
                null,
                [SystemRoles.Clerk]));
        await ProblemDetailsAssert.HasContractAsync(
            duplicateResponse,
            HttpStatusCode.Conflict,
            "conflict",
            "/api/v1/staff");

        var duplicateEmailResponse = await client.PostAsJsonAsync(
            "/api/v1/staff",
            new StaffCreateRequest(
                "unique.username",
                "new.multi@digitalops.local",
                TemporaryPassword,
                "Trùng email",
                null,
                null,
                null,
                [SystemRoles.Clerk]));
        await ProblemDetailsAssert.HasContractAsync(
            duplicateEmailResponse,
            HttpStatusCode.Conflict,
            "conflict",
            "/api/v1/staff");

        var invalidRoleResponse = await client.PostAsJsonAsync(
            "/api/v1/staff",
            new StaffCreateRequest(
                "invalid.role",
                "invalid.role@digitalops.local",
                TemporaryPassword,
                "Role không hợp lệ",
                null,
                null,
                null,
                ["Reviewer"]));
        await AssertValidationErrorAsync(invalidRoleResponse, "roles");

        var weakPasswordResponse = await client.PostAsJsonAsync(
            "/api/v1/staff",
            new StaffCreateRequest(
                "weak.password",
                "weak.password@digitalops.local",
                "weak",
                "Mật khẩu yếu",
                null,
                null,
                null,
                [SystemRoles.Clerk]));
        await AssertValidationErrorAsync(
            weakPasswordResponse,
            "temporaryPassword");

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DigitalOpsDbContext>();
        Assert.False(await dbContext.Users.AnyAsync(
            user => user.UserName == "invalid.role"));
    }

    [Fact]
    public async Task Patch_distinguishes_omitted_fields_from_null_and_synchronizes_email()
    {
        using var factory = new StaffManagementApiFactory();
        using var client = factory.CreateApiClient();
        await AuthenticateAsync(client, "admin", Password);
        var target = await factory.FindStaffAsync("target");

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/staff/{target.Id}",
            new
            {
                fullName = "Cán bộ đã cập nhật",
                position = (string?)null,
                email = "target.updated@digitalops.local"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<StaffResponse>();
        Assert.NotNull(updated);
        Assert.Equal("Cán bộ đã cập nhật", updated.FullName);
        Assert.Null(updated.Position);
        Assert.Equal(target.Department, updated.Department);
        Assert.Equal("target.updated@digitalops.local", updated.Email);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<DigitalOpsDbContext>();
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync("target");
        Assert.NotNull(user);
        Assert.Equal(updated.Email, user.Email);

        var duplicateEmailResponse = await client.PatchAsJsonAsync(
            $"/api/v1/staff/{target.Id}",
            new { email = "admin@digitalops.local" });
        await ProblemDetailsAssert.HasContractAsync(
            duplicateEmailResponse,
            HttpStatusCode.Conflict,
            "conflict",
            $"/api/v1/staff/{target.Id}");

        dbContext.ChangeTracker.Clear();
        var unchangedStaff = await dbContext.Staff
            .AsNoTracking()
            .SingleAsync(staff => staff.Id == target.Id);
        Assert.Equal(updated.Email, unchangedStaff.Email);
    }

    [Fact]
    public async Task Deactivation_immediately_blocks_login_and_an_existing_token()
    {
        using var factory = new StaffManagementApiFactory();
        using var targetClient = factory.CreateApiClient();
        var targetLogin = await AuthenticateAsync(targetClient, "target", Password);

        using var adminClient = factory.CreateApiClient();
        await AuthenticateAsync(adminClient, "admin", Password);
        var response = await adminClient.PatchAsJsonAsync(
            $"/api/v1/staff/{targetLogin.Staff.Id}",
            new { isActive = false });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ProblemDetailsAssert.HasContractAsync(
            await targetClient.GetAsync("/api/v1/auth/me"),
            HttpStatusCode.Forbidden,
            "forbidden",
            "/api/v1/auth/me");

        using var loginClient = factory.CreateApiClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await PostLoginAsync(loginClient, "target", Password)).StatusCode);
    }

    [Fact]
    public async Task Role_changes_use_the_next_token_and_preserve_the_old_snapshot()
    {
        using var factory = new StaffManagementApiFactory();
        using var targetClient = factory.CreateApiClient();
        var oldLogin = await AuthenticateAsync(targetClient, "target", Password);
        Assert.Equal([SystemRoles.Clerk], oldLogin.Roles);

        using var adminClient = factory.CreateApiClient();
        await AuthenticateAsync(adminClient, "admin", Password);
        var response = await adminClient.PutAsJsonAsync(
            $"/api/v1/staff/{oldLogin.Staff.Id}/roles",
            new RoleAssignmentRequest([SystemRoles.Drafter, SystemRoles.Leader]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var oldMe = await targetClient.GetFromJsonAsync<CurrentUserResponse>(
            "/api/v1/auth/me");
        Assert.NotNull(oldMe);
        Assert.Equal([SystemRoles.Clerk], oldMe.Roles);
        Assert.Equal(
            HttpStatusCode.OK,
            (await targetClient.GetAsync("/_test/authorization/clerk")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await targetClient.GetAsync("/_test/authorization/leader")).StatusCode);

        using var newTokenClient = factory.CreateApiClient();
        var newLogin = await AuthenticateAsync(newTokenClient, "target", Password);
        Assert.Equal(
            [SystemRoles.Drafter, SystemRoles.Leader],
            newLogin.Roles);
        Assert.Equal(
            HttpStatusCode.OK,
            (await newTokenClient.GetAsync("/_test/authorization/leader")).StatusCode);
    }

    [Fact]
    public async Task Last_active_administrator_cannot_be_disabled_or_lose_the_role()
    {
        using var factory = new StaffManagementApiFactory();
        using var client = factory.CreateApiClient();
        var admin = await AuthenticateAsync(client, "admin", Password);

        await ProblemDetailsAssert.HasContractAsync(
            await client.PatchAsJsonAsync(
                $"/api/v1/staff/{admin.Staff.Id}",
                new { isActive = false }),
            HttpStatusCode.Conflict,
            "conflict",
            $"/api/v1/staff/{admin.Staff.Id}");

        await ProblemDetailsAssert.HasContractAsync(
            await client.PutAsJsonAsync(
                $"/api/v1/staff/{admin.Staff.Id}/roles",
                new RoleAssignmentRequest([SystemRoles.Clerk])),
            HttpStatusCode.Conflict,
            "conflict",
            $"/api/v1/staff/{admin.Staff.Id}/roles");
    }

    [Fact]
    public async Task Reset_password_blocks_the_old_business_token_and_forces_the_new_password()
    {
        using var factory = new StaffManagementApiFactory();
        using var targetClient = factory.CreateApiClient();
        var target = await AuthenticateAsync(targetClient, "target", Password);

        using var adminClient = factory.CreateApiClient();
        await AuthenticateAsync(adminClient, "admin", Password);

        var weakResponse = await adminClient.PostAsJsonAsync(
            $"/api/v1/staff/{target.Staff.Id}/reset-password",
            new ResetPasswordRequest("weak"));
        await AssertValidationErrorAsync(weakResponse, "temporaryPassword");

        var response = await adminClient.PostAsJsonAsync(
            $"/api/v1/staff/{target.Staff.Id}/reset-password",
            new ResetPasswordRequest(TemporaryPassword));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await ProblemDetailsAssert.HasContractAsync(
            await targetClient.GetAsync("/_test/authorization/business"),
            HttpStatusCode.Forbidden,
            "password-change-required",
            "/_test/authorization/business");

        var oldPasswordResponse = await PostLoginAsync(
            factory.CreateApiClient(),
            "target",
            Password);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordResponse.StatusCode);

        using var newPasswordClient = factory.CreateApiClient();
        var newLogin = await LoginAsync(
            newPasswordClient,
            "target",
            TemporaryPassword);
        Assert.True(newLogin.MustChangePassword);
    }

    [Fact]
    public async Task Missing_staff_returns_problem_details()
    {
        using var factory = new StaffManagementApiFactory();
        using var client = factory.CreateApiClient();
        await AuthenticateAsync(client, "admin", Password);
        var id = Guid.NewGuid();

        await ProblemDetailsAssert.HasContractAsync(
            await client.GetAsync($"/api/v1/staff/{id}"),
            HttpStatusCode.NotFound,
            "not-found",
            $"/api/v1/staff/{id}");
    }

    private static async Task<LoginResponse> AuthenticateAsync(
        HttpClient client,
        string userName,
        string password)
    {
        var login = await LoginAsync(client, userName, password);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return login;
    }

    private static async Task<LoginResponse> LoginAsync(
        HttpClient client,
        string userName,
        string password)
    {
        var response = await PostLoginAsync(client, userName, password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<LoginResponse>()
            ?? throw new InvalidOperationException("Login response was empty.");
    }

    private static Task<HttpResponseMessage> PostLoginAsync(
        HttpClient client,
        string userName,
        string password) =>
        client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(userName, password));

    private static async Task AssertValidationErrorAsync(
        HttpResponseMessage response,
        string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        Assert.True(
            document.RootElement
                .GetProperty("errors")
                .TryGetProperty(field, out var errors));
        Assert.NotEmpty(errors.EnumerateArray());
    }
}

public sealed class StaffManagementApiFactory : DigitalOpsApiFactory
{
    private const string Password = "Valid1!Password";
    private readonly SqliteConnection _connection =
        new("Data Source=:memory:");
    private readonly string _attachmentRootPath = Path.Combine(
        Path.GetTempPath(),
        "digitalops-api-attachment-tests",
        Guid.NewGuid().ToString("N"));

    public StaffManagementApiFactory()
    {
        _connection.Open();
    }

    internal AssignmentSuggestionTestDouble AssignmentSuggestionGenerator { get; } = new();

    public HttpClient CreateApiClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    public async Task<Staff> FindStaffAsync(string userName)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DigitalOpsDbContext>();
        return await dbContext.Staff
            .AsNoTracking()
            .Include(staff => staff.IdentityUser)
            .SingleAsync(staff => staff.IdentityUser.UserName == userName);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>(
                    $"{AttachmentStorageOptions.SectionName}:RootPath",
                    _attachmentRootPath)
            ]));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAssignmentSuggestionGenerator>();
            services.AddSingleton<IAssignmentSuggestionGenerator>(
                AssignmentSuggestionGenerator);
            services.RemoveAll<DbContextOptions<DigitalOpsDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<DigitalOpsDbContext>>();
            services.AddDbContext<DigitalOpsDbContext>(
                options => options
                    .UseSqlite(_connection)
                    .ReplaceService<IModelCustomizer, AuthenticationTestModelCustomizer>());
            services
                .AddControllers()
                .AddApplicationPart(typeof(AuthorizationProbeController).Assembly);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<DigitalOpsDbContext>();
        dbContext.Database.EnsureCreated();
        SeedAsync(services).GetAwaiter().GetResult();

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connection.Dispose();
            if (Directory.Exists(_attachmentRootPath))
            {
                Directory.Delete(_attachmentRootPath, recursive: true);
            }
        }

        base.Dispose(disposing);
    }

    private static async Task SeedAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var dbContext = services.GetRequiredService<DigitalOpsDbContext>();

        foreach (var roleName in SystemRoles.Ordered)
        {
            var role = new IdentityRole<Guid>(roleName) { Id = Guid.NewGuid() };
            Assert.True((await roleManager.CreateAsync(role)).Succeeded);
        }

        await CreateUserAsync(
            userManager,
            dbContext,
            "admin",
            "admin@digitalops.local",
            "A Administrator",
            isActive: true,
            mustChangePassword: false,
            roles: [SystemRoles.Administrator]);
        await CreateUserAsync(
            userManager,
            dbContext,
            "clerk",
            "clerk@digitalops.local",
            "B Clerk",
            isActive: true,
            mustChangePassword: false,
            roles: [SystemRoles.Clerk]);
        await CreateUserAsync(
            userManager,
            dbContext,
            "target",
            "target@digitalops.local",
            "C Target",
            isActive: true,
            mustChangePassword: false,
            roles: [SystemRoles.Clerk],
            position: "Chuyên viên",
            department: "Văn phòng");
        await CreateUserAsync(
            userManager,
            dbContext,
            "inactive",
            "inactive@digitalops.local",
            "D Inactive",
            isActive: false,
            mustChangePassword: false,
            roles: [SystemRoles.Drafter]);
        await CreateUserAsync(
            userManager,
            dbContext,
            "forcedadmin",
            "forcedadmin@digitalops.local",
            "E Forced Administrator",
            isActive: true,
            mustChangePassword: true,
            roles: [SystemRoles.Clerk]);
    }

    private static async Task CreateUserAsync(
        UserManager<ApplicationUser> userManager,
        DigitalOpsDbContext dbContext,
        string userName,
        string email,
        string fullName,
        bool isActive,
        bool mustChangePassword,
        string[] roles,
        string? position = null,
        string? department = null)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Email = email,
            EmailConfirmed = true,
            LockoutEnabled = true,
            MustChangePassword = mustChangePassword
        };

        Assert.True((await userManager.CreateAsync(user, Password)).Succeeded);
        Assert.True((await userManager.AddToRolesAsync(user, roles)).Succeeded);

        dbContext.Staff.Add(new Staff
        {
            Id = Guid.NewGuid(),
            IdentityUserId = user.Id,
            FullName = fullName,
            Position = position,
            Department = department,
            Email = email,
            IsActive = isActive
        });
        await dbContext.SaveChangesAsync();
    }
}

public sealed class IdentityInitializerTests
{
    [Fact]
    public void Enabled_bootstrap_requires_the_account_fields()
    {
        var result = new IdentityBootstrapOptionsValidator().Validate(
            Options.DefaultName,
            new IdentityBootstrapOptions { Enabled = true });

        Assert.True(result.Failed);
        Assert.NotNull(result.FailureMessage);
        Assert.Contains(nameof(IdentityBootstrapOptions.UserName), result.FailureMessage);
        Assert.Contains(nameof(IdentityBootstrapOptions.Email), result.FailureMessage);
        Assert.Contains(
            nameof(IdentityBootstrapOptions.TemporaryPassword),
            result.FailureMessage);
        Assert.Contains(nameof(IdentityBootstrapOptions.FullName), result.FailureMessage);
    }

    [Fact]
    public async Task Initializer_creates_roles_and_bootstrap_admin_idempotently()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var provider = CreateProvider(
            connection,
            new IdentityBootstrapOptions
            {
                Enabled = true,
                UserName = "bootstrap.admin",
                Email = "bootstrap@digitalops.local",
                TemporaryPassword = "Bootstrap1!Password",
                FullName = "Quản trị khởi tạo"
            });

        await EnsureCreatedAndInitializeTwiceAsync(provider);

        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DigitalOpsDbContext>();
        Assert.Equal(4, await dbContext.Roles.CountAsync());
        var user = await dbContext.Users.SingleAsync();
        var staff = await dbContext.Staff.SingleAsync();
        Assert.True(user.MustChangePassword);
        Assert.True(staff.IsActive);
        Assert.Equal(user.Id, staff.IdentityUserId);
        Assert.Equal(
            SystemRoles.Administrator,
            (await dbContext.Roles
                .SingleAsync(role => dbContext.UserRoles
                    .Any(userRole =>
                        userRole.UserId == user.Id
                        && userRole.RoleId == role.Id)))
                .Name);
    }

    [Fact]
    public async Task Disabled_bootstrap_creates_only_the_system_roles()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var provider = CreateProvider(
            connection,
            new IdentityBootstrapOptions { Enabled = false });

        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<DigitalOpsDbContext>()
                .Database.EnsureCreatedAsync();
            await scope.ServiceProvider
                .GetRequiredService<IIdentityInitializer>()
                .InitializeAsync();
        }

        using var assertionScope = provider.CreateScope();
        var dbContext = assertionScope.ServiceProvider
            .GetRequiredService<DigitalOpsDbContext>();
        Assert.Equal(4, await dbContext.Roles.CountAsync());
        Assert.Empty(await dbContext.Users.ToListAsync());
    }

    private static async Task EnsureCreatedAndInitializeTwiceAsync(
        ServiceProvider provider)
    {
        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<DigitalOpsDbContext>()
                .Database.EnsureCreatedAsync();
        }

        for (var iteration = 0; iteration < 2; iteration++)
        {
            using var scope = provider.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<IIdentityInitializer>()
                .InitializeAsync();
        }
    }

    private static ServiceProvider CreateProvider(
        SqliteConnection connection,
        IdentityBootstrapOptions options)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton(Options.Create(options));
        services.AddDbContext<DigitalOpsDbContext>(
            dbOptions => dbOptions
                .UseSqlite(connection)
                .ReplaceService<IModelCustomizer, AuthenticationTestModelCustomizer>());
        services
            .AddIdentityCore<ApplicationUser>(
                identityOptions => identityOptions.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<DigitalOpsDbContext>()
            .AddDefaultTokenProviders();
        services.AddScoped<IIdentityInitializer, IdentityInitializer>();
        return services.BuildServiceProvider();
    }
}
