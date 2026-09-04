using Npgsql;
using RJ.Infrastructure.Operations;
using RJ.Infrastructure.Persistence;

namespace RJ.IntegrationTests;

public sealed class PostgresSchemaTests
{
    [Fact]
    public async Task MigrateAsync_is_idempotent_and_records_expected_version()
    {
        await using var dataSource = CreateDataSourceOrSkip();

        await PostgresSchema.MigrateAsync(dataSource);
        await PostgresSchema.MigrateAsync(dataSource);

        await using var command = dataSource.CreateCommand(
            "SELECT max(version) FROM rj_schema_migrations;");
        var currentVersion = Convert.ToInt32(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(
            int.Parse(PostgresSchema.Version, System.Globalization.CultureInfo.InvariantCulture),
            currentVersion);
    }

    [Fact]
    public async Task EnsureCurrentAsync_accepts_schema_after_explicit_migration()
    {
        await using var dataSource = CreateDataSourceOrSkip();
        await PostgresSchema.MigrateAsync(dataSource);

        await PostgresSchema.EnsureCurrentAsync(dataSource);
    }

    [Fact]
    public async Task Migrated_schema_contains_required_constraints_and_gin_index()
    {
        await using var dataSource = CreateDataSourceOrSkip();
        await PostgresSchema.MigrateAsync(dataSource);

        const string sql = """
            SELECT
                EXISTS (
                    SELECT 1
                    FROM pg_constraint
                    WHERE conrelid = 'public.legal_documents'::regclass
                      AND conname = 'pk_legal_documents'
                      AND contype = 'p') AS has_primary_key,
                EXISTS (
                    SELECT 1
                    FROM pg_constraint
                    WHERE conrelid = 'public.legal_documents'::regclass
                      AND conname = 'uq_legal_documents_case_hash'
                      AND contype = 'u') AS has_case_hash_unique,
                EXISTS (
                    SELECT 1
                    FROM pg_constraint
                    WHERE conrelid = 'public.legal_documents'::regclass
                      AND conname = 'ck_legal_documents_sha256'
                      AND contype = 'c') AS has_sha_check,
                EXISTS (
                    SELECT 1
                    FROM pg_class idx
                    JOIN pg_index i ON i.indexrelid = idx.oid
                    JOIN pg_am am ON am.oid = idx.relam
                    WHERE i.indrelid = 'public.legal_documents'::regclass
                      AND idx.relname = 'ix_legal_documents_search_vector'
                      AND am.amname = 'gin'
                      AND pg_get_indexdef(idx.oid) LIKE '%USING gin (search_vector)%') AS has_search_gin;
            """;

        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.True(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
        Assert.True(reader.GetBoolean(3));
    }

    [Fact]
    public async Task PostgresReadinessProbe_passes_after_explicit_migration()
    {
        await using var dataSource = CreateDataSourceOrSkip();
        await PostgresSchema.MigrateAsync(dataSource);
        var probe = new PostgresReadinessProbe(dataSource);

        await probe.CheckAsync(CancellationToken.None);
    }

    private static NpgsqlDataSource CreateDataSourceOrSkip()
    {
        var connectionString = Environment.GetEnvironmentVariable("RJ_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip("RJ_POSTGRES_CONNECTION is required for PostgreSQL integration tests.");
        }

        return NpgsqlDataSource.Create(connectionString);
    }
}
