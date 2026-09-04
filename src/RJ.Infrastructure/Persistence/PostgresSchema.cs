using Npgsql;

namespace RJ.Infrastructure.Persistence;

public static class PostgresSchema
{
    public const string Version = "3";
    private const int VersionNumber = 3;
    private const long MigrationLockKey = 724_587_321;

    private const string MigrationSql = """
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

    public static async Task MigrateAsync(
        NpgsqlDataSource dataSource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(
            connection,
            transaction,
            "SELECT pg_advisory_xact_lock($1);",
            cancellationToken,
            MigrationLockKey);

        await ExecuteAsync(
            connection,
            transaction,
            """
            CREATE TABLE IF NOT EXISTS rj_schema_migrations (
                version integer PRIMARY KEY,
                applied_at timestamptz NOT NULL DEFAULT now()
            );
            """,
            cancellationToken);

        var currentVersion = await ReadCurrentVersionAsync(connection, transaction, cancellationToken);
        if (currentVersion > VersionNumber)
        {
            throw new PostgresSchemaVersionException(
                $"Database schema version {currentVersion} is newer than application schema version {VersionNumber}.");
        }

        if (currentVersion < VersionNumber)
        {
            await ExecuteAsync(connection, transaction, MigrationSql, cancellationToken);
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO rj_schema_migrations (version) VALUES ($1) ON CONFLICT (version) DO NOTHING;",
                cancellationToken,
                VersionNumber);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public static async Task EnsureCurrentAsync(
        NpgsqlDataSource dataSource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tableCheck = new NpgsqlCommand(
            "SELECT to_regclass('public.rj_schema_migrations') IS NOT NULL;",
            connection);
        var ledgerExists = Convert.ToBoolean(
            await tableCheck.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);

        if (!ledgerExists)
        {
            throw new PostgresSchemaVersionException(
                $"Database schema ledger is missing. Apply schema version {VersionNumber} before starting the API.");
        }

        await using var versionCommand = new NpgsqlCommand(
            "SELECT COALESCE(MAX(version), 0) FROM rj_schema_migrations;",
            connection);
        var currentVersion = Convert.ToInt32(
            await versionCommand.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);

        if (currentVersion != VersionNumber)
        {
            throw new PostgresSchemaVersionException(
                $"Database schema version {currentVersion} does not match required version {VersionNumber}.");
        }

        const string structureSql = """
            WITH target AS (
                SELECT c.oid AS table_oid
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public'
                  AND c.relname = 'legal_documents'
                  AND c.relkind = 'r'
            ),
            required_columns AS (
                SELECT count(*) = 7 AS ok
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'legal_documents'
                  AND column_name IN (
                      'case_id',
                      'document_id',
                      'source_name',
                      'raw_content',
                      'content',
                      'content_sha256',
                      'search_vector')
            ),
            required_constraints AS (
                SELECT
                    count(*) FILTER (
                        WHERE con.conname = 'pk_legal_documents'
                          AND con.contype = 'p'
                          AND pg_get_constraintdef(con.oid) = 'PRIMARY KEY (case_id, document_id)') = 1
                    AND count(*) FILTER (
                        WHERE con.conname = 'uq_legal_documents_case_hash'
                          AND con.contype = 'u'
                          AND pg_get_constraintdef(con.oid) = 'UNIQUE (case_id, content_sha256)') = 1
                    AND count(*) FILTER (
                        WHERE con.conname = 'ck_legal_documents_sha256'
                          AND con.contype = 'c'
                          AND pg_get_constraintdef(con.oid) LIKE 'CHECK ((content_sha256 ~%^[0-9a-f]{64}%') = 1 AS ok
                FROM pg_constraint con
                JOIN target t ON t.table_oid = con.conrelid
            ),
            required_index AS (
                SELECT count(*) = 1 AS ok
                FROM pg_class idx
                JOIN pg_namespace n ON n.oid = idx.relnamespace
                JOIN pg_index i ON i.indexrelid = idx.oid
                JOIN target t ON t.table_oid = i.indrelid
                JOIN pg_am am ON am.oid = idx.relam
                WHERE n.nspname = 'public'
                  AND idx.relname = 'ix_legal_documents_search_vector'
                  AND am.amname = 'gin'
                  AND pg_get_indexdef(idx.oid) LIKE '%USING gin (search_vector)%'
            )
            SELECT
                EXISTS (SELECT 1 FROM target)
                AND (SELECT ok FROM required_columns)
                AND (SELECT ok FROM required_constraints)
                AND (SELECT ok FROM required_index);
            """;
        await using var structureCommand = new NpgsqlCommand(structureSql, connection);
        var structureMatches = Convert.ToBoolean(
            await structureCommand.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);

        if (!structureMatches)
        {
            throw new PostgresSchemaVersionException(
                $"Database schema ledger reports version {VersionNumber}, but required schema invariants are missing or degraded.");
        }
    }

    private static async Task<int> ReadCurrentVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT COALESCE(MAX(version), 0) FROM rj_schema_migrations;",
            connection,
            transaction);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var value in values)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
