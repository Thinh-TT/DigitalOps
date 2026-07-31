using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalOps.API.Shared.Data;
using DigitalOps.API.Shared.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DigitalOps")
    ?? throw new InvalidOperationException(
        "Connection string 'DigitalOps' is required. Configure ConnectionStrings__DigitalOps outside source control.");

builder.Services.AddProblemDetails();
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddDbContext<DigitalOpsDbContext>(options => options
    .UseNpgsql(
        connectionString,
        npgsql => npgsql.MigrationsHistoryTable("__digitalops_ef_migrations_history", "public"))
    .UseSnakeCaseNamingConvention());

builder.Services
    .AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<DigitalOpsDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAccessTokenService, JwtAccessTokenService>();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer();
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwtOptionsAccessor) =>
    {
        var jwtOptions = jwtOptionsAccessor.Value;

        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = JwtClaimNames.Subject,
            RoleClaimType = JwtClaimNames.Role
        };
    });

builder.Services.AddScoped<IStaffAccessChecker, StaffAccessChecker>();
builder.Services.AddScoped<IAuthorizationHandler, CurrentStaffAccessHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        AuthorizationPolicies.BusinessAccess,
        policy => RequireCurrentStaffAccess(policy, mustChangePassword: false));
    options.AddPolicy(
        AuthorizationPolicies.PasswordChangeRequired,
        policy => RequireCurrentStaffAccess(policy, mustChangePassword: true));

    AddRolePolicy(options, AuthorizationPolicies.Administrator, SystemRoles.Administrator);
    AddRolePolicy(options, AuthorizationPolicies.Clerk, SystemRoles.Clerk);
    AddRolePolicy(options, AuthorizationPolicies.Drafter, SystemRoles.Drafter);
    AddRolePolicy(options, AuthorizationPolicies.Leader, SystemRoles.Leader);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

static void AddRolePolicy(AuthorizationOptions options, string policyName, string role)
{
    options.AddPolicy(policyName, policy =>
    {
        RequireCurrentStaffAccess(policy, mustChangePassword: false);
        policy.RequireRole(role);
    });
}

static void RequireCurrentStaffAccess(
    AuthorizationPolicyBuilder policy,
    bool mustChangePassword)
{
    policy.RequireAuthenticatedUser();
    policy.AddRequirements(new CurrentStaffAccessRequirement(mustChangePassword));
}

public partial class Program;
