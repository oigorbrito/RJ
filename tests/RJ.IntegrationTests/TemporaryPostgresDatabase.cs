using Npgsql;

namespace RJ.IntegrationTests;

internal sealed class TemporaryPostgresDatabase : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly NpgsqlDataSource _adminDataSource;

    private TemporaryPostgresDatabase(
        string databaseName,
        NpgsqlDataSource dataSource,
        NpgsqlDataSource adminDataSource)
    {
        DatabaseName = databaseName;
        _dataSource = dataSource;
        _adminDataSource = adminDataSource;
    }

    public string DatabaseName { get; }

    public NpgsqlDataSource DataSource => _dataSource;

    public static async Task<TemporaryPostgresDatabase> CreateAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("RJ_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip("RJ_POSTGRES_CONNECTION is required for PostgreSQL integration tests.");
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres"
        };

        var adminDataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        var databaseName = $"rj_test_{Guid.NewGuid():N}";

        await using (var create = adminDataSource.CreateCommand($$"""
            CREATE DATABASE "{{databaseName}}";
            """))
        {
            await create.ExecuteNonQueryAsync();
        }

        var databaseBuilder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = databaseName
        };

        var dataSource = NpgsqlDataSource.Create(databaseBuilder.ConnectionString);
        return new TemporaryPostgresDatabase(databaseName, dataSource, adminDataSource);
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();

        await using (var terminate = _adminDataSource.CreateCommand($$"""
            SELECT pg_terminate_backend(pid)
            FROM pg_stat_activity
            WHERE datname = '{{DatabaseName}}'
              AND pid <> pg_backend_pid();
            """))
        {
            await terminate.ExecuteNonQueryAsync();
        }

        await using (var drop = _adminDataSource.CreateCommand($$"""
            DROP DATABASE IF EXISTS "{{DatabaseName}}";
            """))
        {
            await drop.ExecuteNonQueryAsync();
        }

        await _adminDataSource.DisposeAsync();
    }
}
