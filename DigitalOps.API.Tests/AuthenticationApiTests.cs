using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using DigitalOps.API.Features.Attachments;
using DigitalOps.API.Features.Authentication;
using DigitalOps.API.Features.Drafting;
using DigitalOps.API.Features.OutgoingDocuments;
using DigitalOps.API.Features.Review;
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
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace DigitalOps.API.Tests;

public sealed class AuthenticationApiTests
{
    private const string ValidPassword = "Valid1!Password";
    private const string ChangedPassword = "Changed2!Password";

    [Fact]
    public async Task Login_by_username_or_email_returns_a_token_and_current_staff()
    {
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateApiClient();

        var usernameResponse = await LoginAsync(client, "clerk", ValidPassword);
        var usernameLogin = await ReadLoginAsync(usernameResponse);

        Assert.False(usernameLogin.MustChangePassword);
        Assert.Equal("Cán bộ Văn thư", usernameLogin.Staff.FullName);
        Assert.Equal(
            [SystemRoles.Clerk, SystemRoles.Leader],
            usernameLogin.Roles);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(usernameLogin.AccessToken);
        Assert.Equal(
            ["Clerk", "Leader"],
            token.Claims
                .Where(claim => claim.Type == JwtClaimNames.Role)
                .Select(claim => claim.Value)
                .Order()
                .ToArray());

        SetBearerToken(client, usernameLogin.AccessToken);
        var meResponse = await client.GetAsync("/api/v1/auth/me");
        var currentUser = await meResponse.Content.ReadFromJsonAsync<CurrentUserResponse>();

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        Assert.NotNull(currentUser);
        Assert.Equal(usernameLogin.Staff, currentUser.Staff);
        Assert.Equal(usernameLogin.Roles, currentUser.Roles);
        Assert.False(currentUser.MustChangePassword);

        client.DefaultRequestHeaders.Authorization = null;
        var emailResponse = await LoginAsync(
            client,
            "clerk@digitalops.local",
            ValidPassword);

        Assert.Equal(HttpStatusCode.OK, emailResponse.StatusCode);
        Assert.DoesNotContain(
            "passwordHash",
            await emailResponse.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("missing", ValidPassword)]
    [InlineData("clerk", "Wrong1!Password")]
    [InlineData("inactive", ValidPassword)]
    [InlineData("unlinked", ValidPassword)]
    [InlineData("locked", ValidPassword)]
    public async Task Invalid_login_cases_share_the_same_unauthorized_response(
        string identifier,
        string password)
    {
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateApiClient();

        var response = await LoginAsync(client, identifier, password);
        var body = await response.Content.ReadAsStringAsync();

        await ProblemDetailsAssert.HasContractAsync(
            response,
            HttpStatusCode.Unauthorized,
            "unauthorized",
            "/api/v1/auth/login");
        Assert.Contains(
            "Tên đăng nhập/email hoặc mật khẩu không đúng.",
            body,
            StringComparison.Ordinal);
        Assert.DoesNotContain(identifier, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Forced_password_user_can_change_password_and_receives_a_normal_token()
    {
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateApiClient();

        var login = await ReadLoginAsync(
            await LoginAsync(client, "forced", ValidPassword));
        Assert.True(login.MustChangePassword);

        SetBearerToken(client, login.AccessToken);

        var meResponse = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        var blockedResponse = await client.GetAsync("/_test/authorization/business");
        await ProblemDetailsAssert.HasContractAsync(
            blockedResponse,
            HttpStatusCode.Forbidden,
            "password-change-required",
            "/_test/authorization/business");

        var wrongCurrentPassword = await client.PostAsJsonAsync(
            "/api/v1/auth/change-password",
            new ChangePasswordRequest("Wrong1!Password", ChangedPassword));
        await AssertValidationErrorAsync(wrongCurrentPassword, "currentPassword");

        var weakPassword = await client.PostAsJsonAsync(
            "/api/v1/auth/change-password",
            new ChangePasswordRequest(ValidPassword, "weak"));
        await AssertValidationErrorAsync(weakPassword, "newPassword");

        var changedResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/change-password",
            new ChangePasswordRequest(ValidPassword, ChangedPassword));
        var changedLogin = await ReadLoginAsync(changedResponse);

        Assert.False(changedLogin.MustChangePassword);
        SetBearerToken(client, changedLogin.AccessToken);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/_test/authorization/business")).StatusCode);

        SetBearerToken(client, login.AccessToken);
        await ProblemDetailsAssert.HasContractAsync(
            await client.GetAsync("/_test/authorization/business"),
            HttpStatusCode.Forbidden,
            "password-change-required",
            "/_test/authorization/business");

        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await LoginAsync(client, "forced", ValidPassword)).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(client, "forced", ChangedPassword)).StatusCode);
    }

    [Fact]
    public async Task Normal_user_can_change_password_voluntarily()
    {
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateApiClient();

        var login = await ReadLoginAsync(
            await LoginAsync(client, "clerk", ValidPassword));
        SetBearerToken(client, login.AccessToken);

        var changedLogin = await ReadLoginAsync(
            await client.PostAsJsonAsync(
                "/api/v1/auth/change-password",
                new ChangePasswordRequest(ValidPassword, ChangedPassword)));

        Assert.False(changedLogin.MustChangePassword);
        Assert.Equal(login.Roles, changedLogin.Roles);
    }

    [Fact]
    public async Task Expired_token_is_rejected_by_current_user_and_change_password()
    {
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateApiClient();
        SetBearerToken(client, factory.CreateExpiredToken());

        await ProblemDetailsAssert.HasContractAsync(
            await client.GetAsync("/api/v1/auth/me"),
            HttpStatusCode.Unauthorized,
            "unauthorized",
            "/api/v1/auth/me");
        await ProblemDetailsAssert.HasContractAsync(
            await client.PostAsJsonAsync(
                "/api/v1/auth/change-password",
                new ChangePasswordRequest(ValidPassword, ChangedPassword)),
            HttpStatusCode.Unauthorized,
            "unauthorized",
            "/api/v1/auth/change-password");
    }

    private static Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        string identifier,
        string password) =>
        client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(identifier, password));

    private static async Task<LoginResponse> ReadLoginAsync(
        HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<LoginResponse>()
            ?? throw new InvalidOperationException("Login response was empty.");
    }

    private static async Task AssertValidationErrorAsync(
        HttpResponseMessage response,
        string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(
            "https://digitalops/errors/validation-error",
            root.GetProperty("type").GetString());
        Assert.True(root.GetProperty("errors").TryGetProperty(field, out var errors));
        Assert.NotEmpty(errors.EnumerateArray());
    }

    private static void SetBearerToken(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
}

public sealed class AuthenticationApiFactory : DigitalOpsApiFactory
{
    private const string SigningKey =
        "digitalops-tests-only-signing-key-32-bytes-minimum";
    private const string SeedPassword = "Valid1!Password";
    private readonly SqliteConnection _connection =
        new("Data Source=:memory:");

    public AuthenticationApiFactory()
    {
        _connection.Open();
    }

    public HttpClient CreateApiClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    public string CreateExpiredToken()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DigitalOpsDbContext>();
        var staff = dbContext.Staff.AsNoTracking().Single(item => item.FullName == "Cán bộ Văn thư");
        var user = dbContext.Users.AsNoTracking().Single(item => item.Id == staff.IdentityUserId);
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: "DigitalOps.API",
            audience: "DigitalOps.Web",
            claims:
            [
                new Claim(JwtClaimNames.Subject, user.Id.ToString()),
                new Claim(JwtClaimNames.StaffId, staff.Id.ToString()),
                new Claim(JwtClaimNames.MustChangePassword, "false", ClaimValueTypes.Boolean),
                new Claim(JwtClaimNames.Role, SystemRoles.Clerk)
            ],
            notBefore: now.AddMinutes(-10),
            expires: now.AddMinutes(-5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
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
        }

        base.Dispose(disposing);
    }

    private static async Task SeedAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var dbContext = services.GetRequiredService<DigitalOpsDbContext>();

        foreach (var roleName in new[] { SystemRoles.Clerk, SystemRoles.Leader })
        {
            var role = new IdentityRole<Guid>(roleName)
            {
                Id = Guid.NewGuid()
            };
            Assert.True((await roleManager.CreateAsync(role)).Succeeded);
        }

        await CreateUserAsync(
            userManager,
            dbContext,
            "clerk",
            "clerk@digitalops.local",
            "Cán bộ Văn thư",
            mustChangePassword: false,
            isActive: true,
            roles: [SystemRoles.Clerk, SystemRoles.Leader]);
        await CreateUserAsync(
            userManager,
            dbContext,
            "forced",
            "forced@digitalops.local",
            "Cán bộ dùng mật khẩu tạm",
            mustChangePassword: true,
            isActive: true,
            roles: [SystemRoles.Clerk]);
        await CreateUserAsync(
            userManager,
            dbContext,
            "inactive",
            "inactive@digitalops.local",
            "Cán bộ đã vô hiệu hóa",
            mustChangePassword: false,
            isActive: false,
            roles: [SystemRoles.Clerk]);
        await CreateUserAsync(
            userManager,
            dbContext,
            "unlinked",
            "unlinked@digitalops.local",
            fullName: null,
            mustChangePassword: false,
            isActive: true,
            roles: [SystemRoles.Clerk]);
        var lockedUser = await CreateUserAsync(
            userManager,
            dbContext,
            "locked",
            "locked@digitalops.local",
            "Cán bộ bị khóa",
            mustChangePassword: false,
            isActive: true,
            roles: [SystemRoles.Clerk]);
        Assert.True(
            (await userManager.SetLockoutEndDateAsync(
                lockedUser,
                DateTimeOffset.UtcNow.AddHours(1))).Succeeded);
    }

    private static async Task<ApplicationUser> CreateUserAsync(
        UserManager<ApplicationUser> userManager,
        DigitalOpsDbContext dbContext,
        string userName,
        string email,
        string? fullName,
        bool mustChangePassword,
        bool isActive,
        string[] roles)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Email = email,
            EmailConfirmed = true,
            MustChangePassword = mustChangePassword
        };

        Assert.True((await userManager.CreateAsync(user, SeedPassword)).Succeeded);
        Assert.True((await userManager.AddToRolesAsync(user, roles)).Succeeded);

        if (fullName is not null)
        {
            dbContext.Staff.Add(new Staff
            {
                Id = Guid.NewGuid(),
                IdentityUserId = user.Id,
                FullName = fullName,
                Email = email,
                IsActive = isActive
            });
            await dbContext.SaveChangesAsync();
        }

        return user;
    }
}

public sealed class AuthenticationTestModelCustomizer(
    ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.Entity<DocumentTemplate>()
            .Property(template => template.FormatRules)
            .HasConversion(
                value => value.GetRawText(),
                value => ParseJson(value))
            .HasColumnName("format_rules")
            .HasColumnType("TEXT")
            .HasDefaultValueSql("'{}'");
        modelBuilder.Entity<DocumentTemplate>()
            .ToTable("document_templates", table =>
                table.HasCheckConstraint(
                    "ck_document_templates_format_rules_object",
                    "json_type(format_rules) = 'object'"));
        modelBuilder.Entity<OutgoingDocument>()
            .Property(document => document.ReviewIssues)
            .HasConversion(
                value => value.GetRawText(),
                value => ParseJson(value))
            .HasColumnName("review_issues")
            .HasColumnType("TEXT")
            .HasDefaultValueSql("'[]'");
        modelBuilder.Entity<OutgoingDocument>()
            .ToTable("outgoing_documents", table =>
            {
                table.HasCheckConstraint(
                    "ck_outgoing_documents_review_issues_array",
                    "json_type(review_issues) = 'array'");
            });
        modelBuilder.Entity<ReviewHistory>()
            .Property(review => review.ReviewIssues)
            .HasConversion(
                value => value.GetRawText(),
                value => ParseJson(value))
            .HasColumnName("review_issues")
            .HasColumnType("TEXT")
            .HasDefaultValueSql("'[]'");
        modelBuilder.Entity<ReviewHistory>()
            .ToTable("review_history", table =>
            {
                table.HasCheckConstraint(
                    "ck_review_history_issues_array",
                    "json_type(review_issues) = 'array'");
            });
        modelBuilder.Entity<Attachment>()
            .ToTable("attachments", table =>
            {
                table.HasCheckConstraint(
                    "ck_attachments_exactly_one_parent",
                    "(incoming_document_id IS NOT NULL AND outgoing_document_id IS NULL) OR (incoming_document_id IS NULL AND outgoing_document_id IS NOT NULL)");
            });
    }

    private static JsonElement ParseJson(string value) =>
        JsonDocument.Parse(value).RootElement.Clone();
}
