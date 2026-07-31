using Npgsql;

namespace DigitalOps.API.Tests;

public sealed class SharedDatabasePreflightTests
{
    private const string MigrationHistoryTable = "__digitalops_ef_migrations_history";

    private static readonly string[] ManagedTables =
    [
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
        "document_templates",
        "incoming_documents",
        "attachments"
    ];

    [Fact]
    [Trait("Category", "DatabasePreflight")]
    public async Task Shared_database_is_empty_or_contains_the_current_migrated_schema()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DigitalOps");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var managedTables = await ReadStringColumnAsync(
            connection,
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name = ANY (ARRAY[
                  'asp_net_roles', 'asp_net_users', 'asp_net_role_claims', 'asp_net_user_claims',
                  'asp_net_user_logins', 'asp_net_user_roles', 'asp_net_user_tokens', 'staff',
                  'members', 'document_types', 'document_templates', 'incoming_documents',
                  'attachments'
              ]);
            """);

        var historyExists = await ScalarBooleanAsync(
            connection,
            "SELECT to_regclass('public.__digitalops_ef_migrations_history') IS NOT NULL;");

        if (managedTables.Length == 0 && !historyExists)
        {
            return;
        }

        Assert.All(ManagedTables, table => Assert.Contains(table, managedTables));
        Assert.True(historyExists, "Managed tables exist without the DigitalOps migration history table.");

        var migrationIds = await ReadStringColumnAsync(
            connection,
            "SELECT migration_id FROM public.__digitalops_ef_migrations_history;");

        Assert.Equal(3, migrationIds.Length);
        Assert.Contains(
            migrationIds,
            migrationId => migrationId.EndsWith("_InitialBaseline", StringComparison.Ordinal));
        Assert.Contains(
            migrationIds,
            migrationId => migrationId.EndsWith("_AddIncomingDocuments", StringComparison.Ordinal));
        Assert.Contains(
            migrationIds,
            migrationId => migrationId.EndsWith("_AddIncomingAttachments", StringComparison.Ordinal));
    }

    private static async Task<bool> ScalarBooleanAsync(NpgsqlConnection connection, string commandText)
    {
        await using var command = new NpgsqlCommand(commandText, connection);
        return (bool)(await command.ExecuteScalarAsync())!;
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
