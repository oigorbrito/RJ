using Npgsql;

namespace RJ.Infrastructure.Persistence;

public static class PostgresSchema
{
    public const string Version = "3";

    private const string Sql = """
        CREATE TABLE IF NOT EXISTS legal_documents (
            case_id text NOT NULL,
            document_id text NOT NULL,
            source_name text NOT NULL,
            raw_content text NOT NULL,
            content text NOT NULL,
            content_sha256 char(64) NOT NULL,
            search_vector tsvector GENERATED ALWAYS AS (to_tsvector('portuguese', content)) STORED,
            created_at timestamptz NOT NULL DEFAULT now(),
            CONSTRAINT pk_legal_documents PRIMARY KEY (case_id, document_id),
            CONSTRAINT uq_legal_documents_case_hash UNIQUE (case_id, content_sha256),
            CONSTRAINT ck_legal_documents_sha256 CHECK (content_sha256 ~ '^[0-9a-f]{64}$')
        );

        ALTER TABLE legal_documents
            ADD COLUMN IF NOT EXISTS raw_content text;

        UPDATE legal_documents
        SET raw_content = content
        WHERE raw_content IS NULL;

        ALTER TABLE legal_documents
            ALTER COLUMN raw_content SET NOT NULL;

        ALTER TABLE legal_documents
            ADD COLUMN IF NOT EXISTS search_vector tsvector
            GENERATED ALWAYS AS (to_tsvector('portuguese', content)) STORED;

        CREATE INDEX IF NOT EXISTS ix_legal_documents_search_vector
            ON legal_documents USING GIN (search_vector);
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
