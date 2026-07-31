using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalOps.API.Shared.Data;
using DigitalOps.API.Shared.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DigitalOps.API.Tests;

public sealed class ApiHostSmokeTests(DigitalOpsApiFactory factory) : IClassFixture<DigitalOpsApiFactory>
{
    [Fact]
    public async Task Host_registers_the_required_api_identity_and_authorization_services()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DigitalOpsDbContext>();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", dbContext.Database.ProviderName);
        Assert.NotNull(scope.ServiceProvider.GetService<IProblemDetailsService>());

        var jsonOptions = scope.ServiceProvider.GetRequiredService<IOptions<JsonOptions>>().Value;
        Assert.Same(JsonNamingPolicy.CamelCase, jsonOptions.JsonSerializerOptions.PropertyNamingPolicy);
        Assert.Contains(jsonOptions.JsonSerializerOptions.Converters, converter => converter is JsonStringEnumConverter);

        Assert.NotNull(scope.ServiceProvider.GetService<UserManager<ApplicationUser>>());
        Assert.NotNull(scope.ServiceProvider.GetService<RoleManager<IdentityRole<Guid>>>());
        Assert.NotNull(scope.ServiceProvider.GetService<SignInManager<ApplicationUser>>());
        Assert.NotNull(scope.ServiceProvider.GetService<IAccessTokenService>());
        Assert.NotNull(scope.ServiceProvider.GetService<IStaffAccessChecker>());

        var authenticationOptions =
            scope.ServiceProvider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, authenticationOptions.DefaultAuthenticateScheme);
        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, authenticationOptions.DefaultChallengeScheme);

        var jwtOptions = scope.ServiceProvider.GetRequiredService<IOptions<JwtOptions>>().Value;
        Assert.Equal("DigitalOps.API", jwtOptions.Issuer);
        Assert.Equal("DigitalOps.Web", jwtOptions.Audience);
        Assert.Equal(480, jwtOptions.AccessTokenLifetimeMinutes);

        var policyProvider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();
        var expectedPolicies = new[]
        {
            AuthorizationPolicies.BusinessAccess,
            AuthorizationPolicies.PasswordChangeRequired,
            AuthorizationPolicies.Administrator,
            AuthorizationPolicies.Clerk,
            AuthorizationPolicies.Drafter,
            AuthorizationPolicies.Leader
        };

        foreach (var policyName in expectedPolicies)
        {
            Assert.NotNull(await policyProvider.GetPolicyAsync(policyName));
        }
    }

    [Fact]
    public async Task Non_development_host_does_not_expose_openapi_or_swagger()
    {
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });

        var openApiResponse = await client.GetAsync("/openapi/v1.json");
        var swaggerResponse = await client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.NotFound, openApiResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, swaggerResponse.StatusCode);
    }
}

public class DigitalOpsApiFactory : WebApplicationFactory<Program>
{
    private const string TestConnectionString =
        "Host=localhost;Port=5432;Database=digitalops_test;Username=test;Password=test";
    private const string TestSigningKey =
        "digitalops-tests-only-signing-key-32-bytes-minimum";

    private readonly string? _previousConnectionString;

    public DigitalOpsApiFactory()
    {
        _previousConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DigitalOps");
        Environment.SetEnvironmentVariable("ConnectionStrings__DigitalOps", TestConnectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>(
                    "ConnectionStrings:DigitalOps",
                    "Host=localhost;Port=5432;Database=digitalops_test;Username=test;Password=test"),
                new KeyValuePair<string, string?>("Jwt:SigningKey", TestSigningKey)
            ]);
        });
        builder.ConfigureServices(services =>
            services.AddDataProtection().UseEphemeralDataProtectionProvider());
    }

    protected override void Dispose(bool disposing)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__DigitalOps", _previousConnectionString);
        base.Dispose(disposing);
    }
}
