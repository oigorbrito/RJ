using Npgsql;
using RJ.Infrastructure.Persistence;

const int successExitCode = 0;
const int configurationOrExecutionErrorExitCode = 2;

try
{
    var connectionString = Environment.GetEnvironmentVariable("RJ_POSTGRES_CONNECTION");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.Error.WriteLine("RJ_POSTGRES_CONNECTION is required.");
        return configurationOrExecutionErrorExitCode;
    }

    await using var dataSource = NpgsqlDataSource.Create(connectionString);
    await PostgresSchema.MigrateAsync(dataSource);
    Console.Out.WriteLine($"Applied database schema version {PostgresSchema.Version}.");
    return successExitCode;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"{exception.GetType().Name}: database migration failed.");
    return configurationOrExecutionErrorExitCode;
}
