using Npgsql;

namespace RJ.Infrastructure.Persistence;

public static class PostgresSchema
{
    public const string Version = "1";

    private const string Sql = """
        CREATE TABLE IF NOT EXISTS legal_documents (
            case_id text NOT NULL,
            document_id text NOT NULL,
            source_name text NOT NULL,
            content text NOT NULL,
            content_sha256 char(64) NOT NULL,
            created_at timestamptz NOT NULL DEFAULT now(),
            CONSTRAINT pk_legal_documents PRIMARY KEY (case_id, document_id),
            CONSTRAINT uq_legal_documents_case_hash UNIQUE (case_id, content_sha256),
            CONSTRAINT ck_legal_documents_sha256 CHECK (content_sha256 ~ '^[0-9a-f]{64}$')
        );
        """;

    public static async Task InitializeAsync(
        NpgsqlDataSource dataSource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        await using var command = dataSource.CreateCommand(Sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
