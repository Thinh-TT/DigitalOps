using System.Text.Json;
using DigitalOps.API.Features.Drafting;
using DigitalOps.API.Shared.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DigitalOps.API.Tests;

public sealed class DocumentCatalogSeedTests
{
    [Fact]
    public async Task Seeder_creates_the_expected_catalog_idempotently()
    {
        await using var database = await SeedDatabase.CreateAsync();
        var seeder = CreateSeeder(database.Context);

        await seeder.SeedAsync();

        var firstTypes = await database.Context.DocumentTypes
            .AsNoTracking()
            .OrderBy(documentType => documentType.Code)
            .ToArrayAsync();
        var firstTemplates = await database.Context.DocumentTemplates
            .AsNoTracking()
            .OrderBy(template => template.Name)
            .ToArrayAsync();

        Assert.Equal(ExpectedTypes, firstTypes.Select(item => (item.Code, item.Name)));
        Assert.Equal(7, firstTemplates.Length);
        Assert.All(firstTypes, documentType => Assert.True(documentType.IsActive));
        Assert.All(firstTemplates, template =>
        {
            Assert.True(template.IsActive);
            Assert.Contains(
                "CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM",
                template.TemplateContent,
                StringComparison.Ordinal);
            Assert.Contains("...", template.TemplateContent, StringComparison.Ordinal);
            Assert.DoesNotContain("{{", template.TemplateContent, StringComparison.Ordinal);
            AssertSeedFormatRules(template.FormatRules);
        });
        Assert.All(
            firstTypes,
            documentType => Assert.Single(
                firstTemplates,
                template => template.DocumentTypeId == documentType.Id));

        var firstTypeTimestamps = firstTypes.ToDictionary(
            documentType => documentType.Id,
            documentType => (documentType.CreatedAt, documentType.UpdatedAt));
        var firstTemplateTimestamps = firstTemplates.ToDictionary(
            template => template.Id,
            template => (template.CreatedAt, template.UpdatedAt));

        await seeder.SeedAsync();

        var secondTypes = await database.Context.DocumentTypes
            .AsNoTracking()
            .ToArrayAsync();
        var secondTemplates = await database.Context.DocumentTemplates
            .AsNoTracking()
            .ToArrayAsync();
        Assert.Equal(7, secondTypes.Length);
        Assert.Equal(7, secondTemplates.Length);
        Assert.All(secondTypes, documentType =>
            Assert.Equal(
                firstTypeTimestamps[documentType.Id],
                (documentType.CreatedAt, documentType.UpdatedAt)));
        Assert.All(secondTemplates, template =>
            Assert.Equal(
                firstTemplateTimestamps[template.Id],
                (template.CreatedAt, template.UpdatedAt)));
    }

    [Fact]
    public async Task Seeder_preserves_existing_changes_and_skips_an_inactive_parent()
    {
        await using var database = await SeedDatabase.CreateAsync();
        var service = new DocumentCatalogService(database.Context);

        var plan = await service.CreateDocumentTypeAsync(
            new DocumentTypeRequest
            {
                Code = "PLAN",
                Name = "Kế hoạch do quản trị sửa",
                Description = "Không ghi đè mô tả này"
            });
        Assert.True(plan.Succeeded);
        var planTemplate = await service.CreateDocumentTemplateAsync(
            new DocumentTemplateRequest
            {
                DocumentTypeId = plan.Value!.Id,
                Name = "Mẫu kế hoạch cơ bản",
                TemplateContent = "Nội dung do quản trị sửa",
                FormatRules = ParseJson(
                    "{\"version\":2,\"rules\":[{\"code\":\"custom\",\"required\":false}]}"),
                IsActive = false
            });
        Assert.True(planTemplate.Succeeded);

        var report = await service.CreateDocumentTypeAsync(
            new DocumentTypeRequest
            {
                Code = "REPORT",
                Name = "Báo cáo đang ngừng dùng",
                IsActive = false
            });
        Assert.True(report.Succeeded);

        var planUpdatedAt = planTemplate.Value!.UpdatedAt;
        var reportUpdatedAt = report.Value!.UpdatedAt;

        await CreateSeeder(database.Context).SeedAsync();

        var preservedPlan = await database.Context.DocumentTypes
            .AsNoTracking()
            .SingleAsync(documentType => documentType.Code == "PLAN");
        var preservedPlanTemplate = await database.Context.DocumentTemplates
            .AsNoTracking()
            .SingleAsync(template => template.DocumentTypeId == preservedPlan.Id);
        var preservedReport = await database.Context.DocumentTypes
            .AsNoTracking()
            .SingleAsync(documentType => documentType.Code == "REPORT");

        Assert.Equal("Kế hoạch do quản trị sửa", preservedPlan.Name);
        Assert.Equal("Không ghi đè mô tả này", preservedPlan.Description);
        Assert.Equal("Nội dung do quản trị sửa", preservedPlanTemplate.TemplateContent);
        Assert.False(preservedPlanTemplate.IsActive);
        Assert.Equal(2, preservedPlanTemplate.FormatRules.GetProperty("version").GetInt32());
        Assert.Equal(planUpdatedAt, preservedPlanTemplate.UpdatedAt);
        Assert.False(preservedReport.IsActive);
        Assert.Equal("Báo cáo đang ngừng dùng", preservedReport.Name);
        Assert.Equal(reportUpdatedAt, preservedReport.UpdatedAt);
        Assert.False(await database.Context.DocumentTemplates
            .AnyAsync(template => template.DocumentTypeId == preservedReport.Id));
        Assert.Equal(7, await database.Context.DocumentTypes.CountAsync());
        Assert.Equal(6, await database.Context.DocumentTemplates.CountAsync());
    }

    [Fact]
    public async Task Disabled_hosted_service_does_not_resolve_the_seeder()
    {
        var invoked = false;
        var services = new ServiceCollection();
        services.AddScoped<IDocumentCatalogSeeder>(_ =>
            new RecordingSeeder(() => invoked = true));
        await using var provider = services.BuildServiceProvider();
        var hostedService = new DocumentCatalogSeedHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DocumentCatalogSeedOptions { Enabled = false }),
            NullLogger<DocumentCatalogSeedHostedService>.Instance);

        await hostedService.StartAsync(CancellationToken.None);

        Assert.False(invoked);
    }

    [Fact]
    public async Task Seeder_rolls_back_all_changes_when_a_later_insert_fails()
    {
        await using var database = await SeedDatabase.CreateAsync();
        await database.Context.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER fail_resolution_seed
            BEFORE INSERT ON document_types
            WHEN NEW.code = 'RESOLUTION'
            BEGIN
                SELECT RAISE(ABORT, 'forced seed failure');
            END;
            """);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSeeder(database.Context).SeedAsync());

        Assert.Equal(
            "Document catalog seed failed. No seed data was committed.",
            exception.Message);
        database.Context.ChangeTracker.Clear();
        Assert.Empty(await database.Context.DocumentTypes.AsNoTracking().ToArrayAsync());
        Assert.Empty(await database.Context.DocumentTemplates.AsNoTracking().ToArrayAsync());
    }

    private static DocumentCatalogSeeder CreateSeeder(DigitalOpsDbContext context) =>
        new(
            context,
            new DocumentCatalogService(context),
            NullLogger<DocumentCatalogSeeder>.Instance);

    private static JsonElement ParseJson(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    private static void AssertSeedFormatRules(JsonElement formatRules)
    {
        Assert.Equal(JsonValueKind.Object, formatRules.ValueKind);
        Assert.Equal(1, formatRules.GetProperty("version").GetInt32());
        var rules = formatRules.GetProperty("rules").EnumerateArray().ToArray();
        Assert.Equal(
            ["national_header", "reference_number", "signature_block"],
            rules.Select(rule => rule.GetProperty("code").GetString()));
        Assert.All(rules, rule => Assert.True(rule.GetProperty("required").GetBoolean()));
    }

    private static readonly (string Code, string Name)[] ExpectedTypes =
    [
        ("CONCLUSION_NOTICE", "Thông báo kết luận"),
        ("DECISION", "Quyết định"),
        ("INVITATION", "Giấy mời"),
        ("PLAN", "Kế hoạch"),
        ("PROGRAM", "Chương trình"),
        ("REPORT", "Báo cáo"),
        ("RESOLUTION", "Nghị quyết")
    ];

    private sealed class RecordingSeeder(Action onSeed) : IDocumentCatalogSeeder
    {
        public Task SeedAsync(CancellationToken cancellationToken = default)
        {
            onSeed();
            return Task.CompletedTask;
        }
    }

    private sealed class SeedDatabase : IAsyncDisposable
    {
        private SeedDatabase(
            SqliteConnection connection,
            DigitalOpsDbContext context)
        {
            Connection = connection;
            Context = context;
        }

        public SqliteConnection Connection { get; }

        public DigitalOpsDbContext Context { get; }

        public static async Task<SeedDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<DigitalOpsDbContext>()
                .UseSqlite(connection)
                .ReplaceService<IModelCustomizer, AuthenticationTestModelCustomizer>()
                .Options;
            var context = new DigitalOpsDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new SeedDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
