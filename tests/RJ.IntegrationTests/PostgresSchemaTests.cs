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
