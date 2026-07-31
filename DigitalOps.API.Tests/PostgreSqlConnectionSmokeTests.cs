using System.Text.Json;
using DigitalOps.API.Features.Drafting;
using DigitalOps.API.Features.Members;
using DigitalOps.API.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DigitalOps.API.Tests;

public sealed class PostgreSqlConnectionSmokeTests
{
    [Fact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Can_connect_when_a_development_connection_string_is_provided()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DigitalOps");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<DigitalOpsDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var dbContext = new DigitalOpsDbContext(options);

        Assert.True(await dbContext.Database.CanConnectAsync());
    }

    [Fact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Initial_baseline_creates_the_expected_schema_when_applied()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DigitalOps");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        Assert.True(await ScalarBooleanAsync(
            connection,
            "SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pgcrypto');"));

        var expectedTables = new[]
        {
            "asp_net_roles",
            "asp_net_users",
            "asp_net_role_claims",
            "asp_net_user_claims",
            "asp_net_user_logins",
            "asp_net_user_roles",
            "asp_net_user_tokens",
            "staff",
            "members",
            "document_types",
            "document_templates"
        };

        var tables = await ReadStringColumnAsync(
            connection,
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name = ANY (ARRAY[
                  'asp_net_roles', 'asp_net_users', 'asp_net_role_claims', 'asp_net_user_claims',
                  'asp_net_user_logins', 'asp_net_user_roles', 'asp_net_user_tokens', 'staff',
                  'members', 'document_types', 'document_templates'
              ]);
            """);

        Assert.All(expectedTables, table => Assert.Contains(table, tables));

        Assert.Equal(
            "jsonb",
            await ScalarStringAsync(
                connection,
                """
                SELECT udt_name
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'document_templates'
                  AND column_name = 'format_rules';
                """));

        var indexes = await ReadStringColumnAsync(
            connection,
            """
            SELECT indexname
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename IN ('members', 'staff', 'document_types', 'document_templates');
            """);

        var expectedIndexes = new[]
        {
            "ix_members_full_name",
            "ix_members_status",
            "ix_members_phone",
            "ix_members_email",
            "ux_staff_identity_user_id",
            "ix_staff_is_active",
            "ix_staff_email",
            "ux_document_types_code",
            "ix_document_types_is_active",
            "ix_document_templates_document_type_id",
            "ix_document_templates_is_active",
            "ux_document_templates_type_name"
        };

        Assert.All(expectedIndexes, index => Assert.Contains(index, indexes));
    }

    [Fact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Baseline_constraints_and_unique_indexes_reject_invalid_data_inside_rolled_back_transactions()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DigitalOps");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await AssertDuplicateDocumentTypeIsRejectedAsync(connectionString);
        await AssertInvalidFormatRulesAreRejectedAsync(connectionString);
        await AssertInvalidMemberStatusIsRejectedAsync(connectionString);
    }

    private static async Task AssertDuplicateDocumentTypeIsRejectedAsync(string connectionString)
    {
        await using var dbContext = CreateDbContext(connectionString);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var code = $"TEST-{Guid.NewGuid():N}";

        dbContext.DocumentTypes.Add(new DocumentType { Code = code, Name = "Test type" });
        await dbContext.SaveChangesAsync();
        dbContext.DocumentTypes.Add(new DocumentType { Code = code, Name = "Duplicate test type" });

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        await transaction.RollbackAsync();
    }

    private static async Task AssertInvalidFormatRulesAreRejectedAsync(string connectionString)
    {
        await using var dbContext = CreateDbContext(connectionString);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var documentType = new DocumentType
        {
            Code = $"TEST-{Guid.NewGuid():N}",
            Name = "Format rules test type"
        };

        dbContext.DocumentTypes.Add(documentType);
        await dbContext.SaveChangesAsync();
        dbContext.DocumentTemplates.Add(new DocumentTemplate
        {
            DocumentTypeId = documentType.Id,
            Name = "Invalid format rules",
            TemplateContent = "Test template",
            FormatRules = JsonDocument.Parse("[]").RootElement.Clone()
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        await transaction.RollbackAsync();
    }

    private static async Task AssertInvalidMemberStatusIsRejectedAsync(string connectionString)
    {
        await using var dbContext = CreateDbContext(connectionString);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        dbContext.Members.Add(new Member
        {
            FullName = "Invalid status test member",
            Status = (MemberStatus)999
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        await transaction.RollbackAsync();
    }

    private static DigitalOpsDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<DigitalOpsDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__digitalops_ef_migrations_history", "public"))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new DigitalOpsDbContext(options);
    }

    private static async Task<bool> ScalarBooleanAsync(NpgsqlConnection connection, string commandText)
    {
        await using var command = new NpgsqlCommand(commandText, connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> ScalarStringAsync(NpgsqlConnection connection, string commandText)
    {
        await using var command = new NpgsqlCommand(commandText, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string[]> ReadStringColumnAsync(NpgsqlConnection connection, string commandText)
    {
        await using var command = new NpgsqlCommand(commandText, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();

        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values.ToArray();
    }
}
