using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalOps.API.Features.Attachments;
using DigitalOps.API.Features.Authentication;
using DigitalOps.API.Features.Drafting;
using DigitalOps.API.Features.IncomingDocuments;
using DigitalOps.API.Features.Members;
using DigitalOps.API.Features.OutgoingDocuments;
using DigitalOps.API.Features.Reminders;
using DigitalOps.API.Features.Review;
using DigitalOps.API.Features.StaffManagement;
using DigitalOps.API.Shared.Data;
using DigitalOps.API.Shared.Errors;
using DigitalOps.API.Shared.AI;
using DigitalOps.API.Shared.Identity;
using DigitalOps.API.Shared.OpenApi;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
MemberWorkbookGraphics.Configure();
AddLocalEnvironmentFile(builder);

var connectionString = builder.Configuration.GetConnectionString("DigitalOps")
    ?? throw new InvalidOperationException(
        "Connection string 'DigitalOps' is required. Configure ConnectionStrings__DigitalOps outside source control.");

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
        ProblemDetailsDefaults.Apply(context.HttpContext, context.ProblemDetails);
});
builder.Services.AddSingleton<ProblemDetailsFactory, DigitalOpsProblemDetailsFactory>();
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        ConfigureJsonSerializer(options.JsonSerializerOptions));
builder.Services.ConfigureHttpJsonOptions(options =>
    ConfigureJsonSerializer(options.SerializerOptions));
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddOperationTransformer<BearerSecuritySchemeTransformer>();
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
builder.Services.AddDigitalOpsAi(builder.Configuration);
builder.Services.AddSingleton<IAccessTokenService, JwtAccessTokenService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services
    .AddOptions<MemberImportOptions>()
    .Bind(builder.Configuration.GetSection(MemberImportOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<
    IValidateOptions<MemberImportOptions>,
    MemberImportOptionsValidator>();
builder.Services.AddScoped<IMemberImportService, MemberImportService>();
builder.Services.AddScoped<IMemberManagementService, MemberManagementService>();
builder.Services.AddScoped<IDocumentCatalogService, DocumentCatalogService>();
builder.Services.AddScoped<IIncomingDocumentService, IncomingDocumentService>();
builder.Services.AddScoped<IAssignmentSuggestionGenerator, AssignmentSuggestionGenerator>();
builder.Services.AddScoped<IAiDraftGenerator, AiDraftGenerator>();
builder.Services.AddScoped<IOutgoingDocumentService, OutgoingDocumentService>();
builder.Services.AddScoped<IDocumentReviewGenerator, DocumentReviewGenerator>();
builder.Services.AddScoped<IOutgoingDocumentReviewService, OutgoingDocumentReviewService>();
builder.Services
    .AddOptions<ReminderWorkerOptions>()
    .Bind(builder.Configuration.GetSection(ReminderWorkerOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<
    IValidateOptions<ReminderWorkerOptions>,
    ReminderWorkerOptionsValidator>();
builder.Services.AddScoped<IReminderService, ReminderService>();
builder.Services.AddHostedService<ReminderWorker>();
builder.Services
    .AddOptions<AttachmentStorageOptions>()
    .Bind(builder.Configuration.GetSection(AttachmentStorageOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<
    IValidateOptions<AttachmentStorageOptions>,
    AttachmentStorageOptionsValidator>();
builder.Services.AddSingleton<IAttachmentStorage, LocalAttachmentStorage>();
builder.Services.AddScoped<IAttachmentService, AttachmentService>();
builder.Services
    .AddOptions<DocumentCatalogSeedOptions>()
    .Bind(builder.Configuration.GetSection(DocumentCatalogSeedOptions.SectionName));
builder.Services.AddScoped<IDocumentCatalogSeeder, DocumentCatalogSeeder>();
builder.Services.AddScoped<IStaffManagementService, StaffManagementService>();
builder.Services
    .AddOptions<IdentityBootstrapOptions>()
    .Bind(builder.Configuration.GetSection(IdentityBootstrapOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<
    IValidateOptions<IdentityBootstrapOptions>,
    IdentityBootstrapOptionsValidator>();
builder.Services.AddScoped<IIdentityInitializer, IdentityInitializer>();
builder.Services.AddHostedService<IdentityInitializationHostedService>();
builder.Services.AddHostedService<DocumentCatalogSeedHostedService>();

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
builder.Services.AddSingleton<
    IAuthorizationMiddlewareResultHandler,
    DigitalOpsAuthorizationMiddlewareResultHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        AuthorizationPolicies.CurrentStaff,
        policy => RequireCurrentStaffAccess(policy, mustChangePassword: null));
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
var aiOptions = app.Services.GetRequiredService<IOptions<AiProviderOptions>>().Value;
app.Logger.LogInformation(
    "AI provider configured: {Provider}/{Model}; embedding: {EmbeddingProvider}/{EmbeddingModel}; Qdrant collection: {QdrantCollection}; automatic fallback: {AutomaticFallback}",
    aiOptions.Provider,
    string.Equals(aiOptions.Provider, AiProviderNames.External, StringComparison.OrdinalIgnoreCase)
        ? aiOptions.External.Model
        : aiOptions.Ollama.LlmModel,
    aiOptions.Embedding.Provider,
    aiOptions.Embedding.Model,
    aiOptions.Qdrant.CollectionName,
    aiOptions.AutomaticFallback);

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.SwaggerEndpoint("/openapi/v1.json", "DigitalOps API v1");
        options.DocumentTitle = "DigitalOps API";
    });
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
    bool? mustChangePassword)
{
    policy.RequireAuthenticatedUser();
    policy.AddRequirements(new CurrentStaffAccessRequirement(mustChangePassword));
}

static void ConfigureJsonSerializer(JsonSerializerOptions options)
{
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    options.Converters.Add(new JsonStringEnumConverter());
}

static void AddLocalEnvironmentFile(WebApplicationBuilder builder)
{
    var envPath = Path.Combine(builder.Environment.ContentRootPath, ".env");

    if (!File.Exists(envPath))
    {
        return;
    }

    var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    foreach (var line in File.ReadLines(envPath))
    {
        var trimmedLine = line.Trim();

        if (trimmedLine.Length == 0 || trimmedLine.StartsWith('#'))
        {
            continue;
        }

        if (trimmedLine.StartsWith("export ", StringComparison.Ordinal))
        {
            trimmedLine = trimmedLine["export ".Length..].TrimStart();
        }

        var separatorIndex = trimmedLine.IndexOf('=');

        if (separatorIndex <= 0)
        {
            continue;
        }

        var key = trimmedLine[..separatorIndex].Trim();
        var value = trimmedLine[(separatorIndex + 1)..].Trim();

        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"')
                || (value[0] == '\'' && value[^1] == '\'')))
        {
            value = value[1..^1];
        }

        var configurationKey = key.Replace("__", ":", StringComparison.Ordinal);

        if (Environment.GetEnvironmentVariable(key) is null
            && Environment.GetEnvironmentVariable(configurationKey) is null)
        {
            values[configurationKey] = value;
        }
    }

    if (values.Count > 0)
    {
        builder.Configuration.AddInMemoryCollection(values);
    }
}

public partial class Program;
