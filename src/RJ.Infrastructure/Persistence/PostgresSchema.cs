using Npgsql;

namespace RJ.Infrastructure.Persistence;

public static class PostgresSchema
{
    public const string Version = "5";
    private const int VersionNumber = 5;
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

        CREATE TABLE IF NOT EXISTS process_summary_jobs (
            job_id text NOT NULL,
            scoped_idempotency_key text NOT NULL,
            tenant_id_hash char(64) NOT NULL,
            subject_id_hash char(64) NOT NULL,
            case_id text NOT NULL,
            snapshot_sha256 char(64) NOT NULL,
            job_json jsonb NOT NULL,
            created_at timestamptz NOT NULL,
            updated_at timestamptz NOT NULL,
            CONSTRAINT pk_process_summary_jobs PRIMARY KEY (job_id),
            CONSTRAINT uq_process_summary_jobs_scoped_idempotency UNIQUE (scoped_idempotency_key),
            CONSTRAINT ck_process_summary_jobs_tenant_hash CHECK (tenant_id_hash ~ '^[0-9a-f]{64}$'),
            CONSTRAINT ck_process_summary_jobs_subject_hash CHECK (subject_id_hash ~ '^[0-9a-f]{64}$'),
            CONSTRAINT ck_process_summary_jobs_snapshot_hash CHECK (snapshot_sha256 ~ '^[0-9a-f]{64}$')
        );

        CREATE INDEX IF NOT EXISTS ix_process_summary_jobs_case_id
            ON process_summary_jobs (case_id);

        CREATE TABLE IF NOT EXISTS process_cases (
            case_id text NOT NULL,
            cnj text NOT NULL,
            source_name text NOT NULL,
            canonical_json jsonb NOT NULL,
            created_at timestamptz NOT NULL DEFAULT now(),
            updated_at timestamptz NOT NULL DEFAULT now(),
            CONSTRAINT pk_process_cases PRIMARY KEY (case_id),
            CONSTRAINT uq_process_cases_cnj UNIQUE (cnj)
        );

        CREATE INDEX IF NOT EXISTS ix_process_cases_cnj
            ON process_cases (cnj);
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

        if (!await LegalDocumentStructureMatchesAsync(connection, cancellationToken)
            || !await ProcessSummaryStructureMatchesAsync(connection, cancellationToken)
            || !await ProcessCatalogStructureMatchesAsync(connection, cancellationToken))
        {
            throw new PostgresSchemaVersionException(
                $"Database schema ledger reports version {VersionNumber}, but required schema invariants are missing or degraded.");
        }
    }

    private static async Task<bool> LegalDocumentStructureMatchesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string structureSql = """
            WITH target AS (
                SELECT c.oid AS table_oid
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public'
                  AND c.relname = 'legal_documents'
                  AND c.relkind = 'r'
            ),
            attributes AS (
                SELECT
                    t.table_oid,
                    max(a.attnum) FILTER (WHERE a.attname = 'case_id' AND NOT a.attisdropped) AS case_id_attnum,
                    max(a.attnum) FILTER (WHERE a.attname = 'document_id' AND NOT a.attisdropped) AS document_id_attnum,
                    max(a.attnum) FILTER (WHERE a.attname = 'content_sha256' AND NOT a.attisdropped) AS content_sha256_attnum,
                    max(a.attnum) FILTER (WHERE a.attname = 'search_vector' AND NOT a.attisdropped) AS search_vector_attnum
                FROM target t
                JOIN pg_attribute a ON a.attrelid = t.table_oid
                GROUP BY t.table_oid
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
                          AND con.convalidated
                          AND con.conenforced
                          AND con.conkey = ARRAY[a.case_id_attnum, a.document_id_attnum]::smallint[]) = 1
                    AND count(*) FILTER (
                        WHERE con.conname = 'uq_legal_documents_case_hash'
                          AND con.contype = 'u'
                          AND con.convalidated
                          AND con.conenforced
                          AND con.conkey = ARRAY[a.case_id_attnum, a.content_sha256_attnum]::smallint[]) = 1
                    AND count(*) FILTER (
                        WHERE con.conname = 'ck_legal_documents_sha256'
                          AND con.contype = 'c'
                          AND con.convalidated
                          AND con.conenforced
                          AND con.conkey = ARRAY[a.content_sha256_attnum]::smallint[]
                          AND pg_get_expr(con.conbin, con.conrelid) LIKE '%content_sha256%^[0-9a-f]{64}$%') = 1 AS ok
                FROM pg_constraint con
                JOIN attributes a ON a.table_oid = con.conrelid
            ),
            required_index AS (
                SELECT count(*) = 1 AS ok
                FROM pg_class idx
                JOIN pg_namespace n ON n.oid = idx.relnamespace
                JOIN pg_index i ON i.indexrelid = idx.oid
                JOIN attributes a ON a.table_oid = i.indrelid
                JOIN pg_am am ON am.oid = idx.relam
                WHERE n.nspname = 'public'
                  AND idx.relname = 'ix_legal_documents_search_vector'
                  AND am.amname = 'gin'
                  AND i.indisvalid
                  AND i.indisready
                  AND i.indislive
                  AND i.indnkeyatts = 1
                  AND i.indnatts = 1
                  AND (
                      SELECT count(*) = 1 AND min(key_attnum) = a.search_vector_attnum
                      FROM unnest(i.indkey::smallint[]) AS key_columns(key_attnum))
                  AND i.indexprs IS NULL
                  AND i.indpred IS NULL
            )
            SELECT
                EXISTS (SELECT 1 FROM target)
                AND EXISTS (
                    SELECT 1
                    FROM attributes
                    WHERE case_id_attnum IS NOT NULL
                      AND document_id_attnum IS NOT NULL
                      AND content_sha256_attnum IS NOT NULL
                      AND search_vector_attnum IS NOT NULL)
                AND (SELECT ok FROM required_columns)
                AND (SELECT ok FROM required_constraints)
                AND (SELECT ok FROM required_index);
            """;

        await using var command = new NpgsqlCommand(structureSql, connection);
        return Convert.ToBoolean(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<bool> ProcessSummaryStructureMatchesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string structureSql = """
            WITH required_columns AS (
                SELECT count(*) = 9 AS ok
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'process_summary_jobs'
                  AND column_name IN (
                      'job_id',
                      'scoped_idempotency_key',
                      'tenant_id_hash',
                      'subject_id_hash',
                      'case_id',
                      'snapshot_sha256',
                      'job_json',
                      'created_at',
                      'updated_at')
            ),
            required_constraints AS (
                SELECT
                    count(*) FILTER (WHERE conname = 'pk_process_summary_jobs' AND contype = 'p' AND convalidated AND conenforced) = 1
                    AND count(*) FILTER (WHERE conname = 'uq_process_summary_jobs_scoped_idempotency' AND contype = 'u' AND convalidated AND conenforced) = 1
                    AND count(*) FILTER (WHERE conname = 'ck_process_summary_jobs_tenant_hash' AND contype = 'c' AND convalidated AND conenforced) = 1
                    AND count(*) FILTER (WHERE conname = 'ck_process_summary_jobs_subject_hash' AND contype = 'c' AND convalidated AND conenforced) = 1
                    AND count(*) FILTER (WHERE conname = 'ck_process_summary_jobs_snapshot_hash' AND contype = 'c' AND convalidated AND conenforced) = 1 AS ok
                FROM pg_constraint
                WHERE conrelid = to_regclass('public.process_summary_jobs')
            ),
            required_index AS (
                SELECT count(*) = 1 AS ok
                FROM pg_class idx
                JOIN pg_namespace n ON n.oid = idx.relnamespace
                JOIN pg_index i ON i.indexrelid = idx.oid
                WHERE n.nspname = 'public'
                  AND idx.relname = 'ix_process_summary_jobs_case_id'
                  AND i.indisvalid
                  AND i.indisready
                  AND i.indislive
                  AND i.indpred IS NULL
            )
            SELECT
                to_regclass('public.process_summary_jobs') IS NOT NULL
                AND (SELECT ok FROM required_columns)
                AND (SELECT ok FROM required_constraints)
                AND (SELECT ok FROM required_index);
            """;

        await using var command = new NpgsqlCommand(structureSql, connection);
        return Convert.ToBoolean(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<bool> ProcessCatalogStructureMatchesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string structureSql = """
            WITH required_columns AS (
                SELECT count(*) = 6 AS ok
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'process_cases'
                  AND column_name IN (
                      'case_id',
                      'cnj',
                      'source_name',
                      'canonical_json',
                      'created_at',
                      'updated_at')
            ),
            required_constraints AS (
                SELECT
                    count(*) FILTER (WHERE conname = 'pk_process_cases' AND contype = 'p' AND convalidated AND conenforced) = 1
                    AND count(*) FILTER (WHERE conname = 'uq_process_cases_cnj' AND contype = 'u' AND convalidated AND conenforced) = 1 AS ok
                FROM pg_constraint
                WHERE conrelid = to_regclass('public.process_cases')
            ),
            required_index AS (
                SELECT count(*) = 1 AS ok
                FROM pg_class idx
                JOIN pg_namespace n ON n.oid = idx.relnamespace
                JOIN pg_index i ON i.indexrelid = idx.oid
                WHERE n.nspname = 'public'
                  AND idx.relname = 'ix_process_cases_cnj'
                  AND i.indisvalid
                  AND i.indisready
                  AND i.indislive
                  AND i.indpred IS NULL
            )
            SELECT
                to_regclass('public.process_cases') IS NOT NULL
                AND (SELECT ok FROM required_columns)
                AND (SELECT ok FROM required_constraints)
                AND (SELECT ok FROM required_index);
            """;

        await using var command = new NpgsqlCommand(structureSql, connection);
        return Convert.ToBoolean(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
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
