namespace RJ.Infrastructure.Persistence;

public sealed class PostgresSchemaVersionException : InvalidOperationException
{
    public PostgresSchemaVersionException(string message)
        : base(message)
    {
    }
}
