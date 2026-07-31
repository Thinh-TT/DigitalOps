using System.Net;
using System.Net.Http.Headers;
using DigitalOps.API.Shared.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalOps.API.Tests;

public sealed class AuthorizationPipelineTests(AuthorizationApiFactory factory)
    : IClassFixture<AuthorizationApiFactory>
{
    [Fact]
    public async Task Business_access_returns_unauthorized_without_a_token()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/_test/authorization/business");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Business_access_accepts_a_normal_token_and_rejects_a_forced_password_token()
    {
        using var client = CreateClient();

        SetBearerToken(client, CreateToken(mustChangePassword: false));
        var allowedResponse = await client.GetAsync("/_test/authorization/business");

        SetBearerToken(client, CreateToken(mustChangePassword: true));
        var forbiddenResponse = await client.GetAsync("/_test/authorization/business");

        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
    }

    [Fact]
    public async Task Password_change_required_accepts_only_a_forced_password_token()
    {
        using var client = CreateClient();

        SetBearerToken(client, CreateToken(mustChangePassword: true));
        var allowedResponse = await client.GetAsync("/_test/authorization/password-change-required");

        SetBearerToken(client, CreateToken(mustChangePassword: false));
        var forbiddenResponse = await client.GetAsync("/_test/authorization/password-change-required");

        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
    }

    [Fact]
    public async Task Business_access_rejects_an_inactive_staff_link()
    {
        using var client = CreateClient();
        SetBearerToken(
            client,
            CreateToken(
                mustChangePassword: false,
                staffId: AuthorizationTestStaffAccessChecker.InactiveStaffId));

        var response = await client.GetAsync("/_test/authorization/business");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("administrator", SystemRoles.Administrator)]
    [InlineData("clerk", SystemRoles.Clerk)]
    [InlineData("drafter", SystemRoles.Drafter)]
    [InlineData("leader", SystemRoles.Leader)]
    public async Task Role_policies_accept_the_matching_role_and_reject_a_missing_role(
        string route,
        string role)
    {
        using var client = CreateClient();

        SetBearerToken(client, CreateToken(mustChangePassword: false, roles: [role]));
        var allowedResponse = await client.GetAsync($"/_test/authorization/{route}");

        SetBearerToken(client, CreateToken(mustChangePassword: false));
        var forbiddenResponse = await client.GetAsync($"/_test/authorization/{route}");

        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
    }

    private HttpClient CreateClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    private string CreateToken(
        bool mustChangePassword,
        Guid? staffId = null,
        IReadOnlyCollection<string>? roles = null)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            MustChangePassword = mustChangePassword
        };
        var staff = new Staff
        {
            Id = staffId ?? Guid.NewGuid(),
            IdentityUserId = user.Id
        };

        return factory.Services
            .GetRequiredService<IAccessTokenService>()
            .CreateToken(user, staff, roles ?? [])
            .AccessToken;
    }

    private static void SetBearerToken(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
}

public sealed class AuthorizationApiFactory : DigitalOpsApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services
                .AddControllers()
                .AddApplicationPart(typeof(AuthorizationProbeController).Assembly);
            services.RemoveAll<IStaffAccessChecker>();
            services.AddSingleton<IStaffAccessChecker, AuthorizationTestStaffAccessChecker>();
        });
    }
}

public sealed class AuthorizationTestStaffAccessChecker : IStaffAccessChecker
{
    public static readonly Guid InactiveStaffId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public Task<bool> IsActiveAsync(
        Guid identityUserId,
        Guid staffId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            identityUserId != Guid.Empty
            && staffId != Guid.Empty
            && staffId != InactiveStaffId);
}

[ApiController]
[Route("_test/authorization")]
public sealed class AuthorizationProbeController : ControllerBase
{
    [HttpGet("business")]
    [Authorize(Policy = AuthorizationPolicies.BusinessAccess)]
    public IActionResult BusinessAccess() => Ok();

    [HttpGet("password-change-required")]
    [Authorize(Policy = AuthorizationPolicies.PasswordChangeRequired)]
    public IActionResult PasswordChangeRequired() => Ok();

    [HttpGet("administrator")]
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public IActionResult Administrator() => Ok();

    [HttpGet("clerk")]
    [Authorize(Policy = AuthorizationPolicies.Clerk)]
    public IActionResult Clerk() => Ok();

    [HttpGet("drafter")]
    [Authorize(Policy = AuthorizationPolicies.Drafter)]
    public IActionResult Drafter() => Ok();

    [HttpGet("leader")]
    [Authorize(Policy = AuthorizationPolicies.Leader)]
    public IActionResult Leader() => Ok();
}
