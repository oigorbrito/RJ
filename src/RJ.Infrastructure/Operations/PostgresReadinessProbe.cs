using RJ.Application.Operations;
using RJ.Infrastructure.Persistence;

namespace RJ.Infrastructure.Operations;

public sealed class PostgresReadinessProbe(Npgsql.NpgsqlDataSource dataSource) : IReadinessProbe
{
    private readonly Npgsql.NpgsqlDataSource _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    public Task CheckAsync(CancellationToken cancellationToken) =>
        PostgresSchema.EnsureCurrentAsync(_dataSource, cancellationToken);
}
