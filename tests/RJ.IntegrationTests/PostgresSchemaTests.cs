using System.Globalization;
using Npgsql;
using RJ.Infrastructure.Operations;
using RJ.Infrastructure.Persistence;

namespace RJ.IntegrationTests;

public sealed class PostgresSchemaTests
{
    private static readonly int RequiredVersion = int.Parse(
        PostgresSchema.Version,
        CultureInfo.InvariantCulture);

    [Fact]
    public async Task MigrateAsync_is_idempotent_and_records_expected_version()
    {
        await using var dataSource = CreateDataSourceOrSkip();

        await PostgresSchema.MigrateAsync(dataSource);
        await PostgresSchema.MigrateAsync(dataSource);

        Assert.Equal(RequiredVersion, await ReadLedgerVersionAsync(dataSource));
    }

    [Fact]
    public async Task EnsureCurrentAsync_accepts_schema_after_explicit_migration()
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        await PostgresSchema.MigrateAsync(database.DataSource);

        await PostgresSchema.EnsureCurrentAsync(database.DataSource);
    }

    [Fact]
    public async Task EnsureCurrentAsync_rejects_missing_ledger_without_mutating_schema()
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();

        var exception = await Assert.ThrowsAsync<PostgresSchemaVersionException>(
            () => PostgresSchema.EnsureCurrentAsync(database.DataSource));

        Assert.Contains("ledger is missing", exception.Message, StringComparison.Ordinal);
        Assert.False(await TableExistsAsync(database.DataSource, "legal_documents"));
        Assert.False(await TableExistsAsync(database.DataSource, "process_summary_jobs"));
    }

    [Fact]
    public async Task EnsureCurrentAsync_rejects_older_ledger_version_without_mutating_schema()
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        await CreateLedgerOnlyAsync(database.DataSource, RequiredVersion - 1);

        var exception = await Assert.ThrowsAsync<PostgresSchemaVersionException>(
            () => PostgresSchema.EnsureCurrentAsync(database.DataSource));

        Assert.Contains($"does not match required version {RequiredVersion}", exception.Message, StringComparison.Ordinal);
        Assert.Equal(RequiredVersion - 1, await ReadLedgerVersionAsync(database.DataSource));
        Assert.False(await TableExistsAsync(database.DataSource, "legal_documents"));
        Assert.False(await TableExistsAsync(database.DataSource, "process_summary_jobs"));
    }

    [Fact]
    public async Task EnsureCurrentAsync_rejects_newer_ledger_version_without_mutating_schema()
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        await CreateLedgerOnlyAsync(database.DataSource, RequiredVersion + 1);

        var exception = await Assert.ThrowsAsync<PostgresSchemaVersionException>(
            () => PostgresSchema.EnsureCurrentAsync(database.DataSource));

        Assert.Contains($"does not match required version {RequiredVersion}", exception.Message, StringComparison.Ordinal);
        Assert.Equal(RequiredVersion + 1, await ReadLedgerVersionAsync(database.DataSource));
        Assert.False(await TableExistsAsync(database.DataSource, "legal_documents"));
        Assert.False(await TableExistsAsync(database.DataSource, "process_summary_jobs"));
    }

    [Fact]
    public async Task EnsureCurrentAsync_rejects_degraded_legal_document_schema_without_repairing_it()
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        await PostgresSchema.MigrateAsync(database.DataSource);
        await using (var command = database.DataSource.CreateCommand(
            "DROP INDEX IF EXISTS public.ix_legal_documents_search_vector;"))
        {
            await command.ExecuteNonQueryAsync();
        }

        var exception = await Assert.ThrowsAsync<PostgresSchemaVersionException>(
            () => PostgresSchema.EnsureCurrentAsync(database.DataSource));

        Assert.Contains("required schema invariants are missing or degraded", exception.Message, StringComparison.Ordinal);
        Assert.Equal(RequiredVersion, await ReadLedgerVersionAsync(database.DataSource));
        Assert.False(await IndexExistsAsync(database.DataSource, "ix_legal_documents_search_vector"));
    }

    [Fact]
    public async Task EnsureCurrentAsync_rejects_degraded_process_summary_schema_without_repairing_it()
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        await PostgresSchema.MigrateAsync(database.DataSource);
        await using (var command = database.DataSource.CreateCommand(
            "DROP INDEX IF EXISTS public.ix_process_summary_jobs_case_id;"))
        {
            await command.ExecuteNonQueryAsync();
        }

        var exception = await Assert.ThrowsAsync<PostgresSchemaVersionException>(
            () => PostgresSchema.EnsureCurrentAsync(database.DataSource));

        Assert.Contains("required schema invariants are missing or degraded", exception.Message, StringComparison.Ordinal);
        Assert.Equal(RequiredVersion, await ReadLedgerVersionAsync(database.DataSource));
        Assert.False(await IndexExistsAsync(database.DataSource, "ix_process_summary_jobs_case_id"));
    }

    [Fact]
    public async Task Migrated_schema_contains_legal_document_and_process_summary_structures()
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        await PostgresSchema.MigrateAsync(database.DataSource);

        Assert.True(await TableExistsAsync(database.DataSource, "legal_documents"));
        Assert.True(await TableExistsAsync(database.DataSource, "process_summary_jobs"));
        Assert.True(await ConstraintExistsAsync(database.DataSource, "legal_documents", "pk_legal_documents", "p"));
        Assert.True(await ConstraintExistsAsync(database.DataSource, "legal_documents", "uq_legal_documents_case_hash", "u"));
        Assert.True(await ConstraintExistsAsync(database.DataSource, "legal_documents", "ck_legal_documents_sha256", "c"));
        Assert.True(await IndexExistsAsync(database.DataSource, "ix_legal_documents_search_vector"));
        Assert.True(await ConstraintExistsAsync(database.DataSource, "process_summary_jobs", "pk_process_summary_jobs", "p"));
        Assert.True(await ConstraintExistsAsync(database.DataSource, "process_summary_jobs", "uq_process_summary_jobs_scoped_idempotency", "u"));
        Assert.True(await ConstraintExistsAsync(database.DataSource, "process_summary_jobs", "ck_process_summary_jobs_tenant_hash", "c"));
        Assert.True(await ConstraintExistsAsync(database.DataSource, "process_summary_jobs", "ck_process_summary_jobs_subject_hash", "c"));
        Assert.True(await ConstraintExistsAsync(database.DataSource, "process_summary_jobs", "ck_process_summary_jobs_snapshot_hash", "c"));
        Assert.True(await IndexExistsAsync(database.DataSource, "ix_process_summary_jobs_case_id"));
    }

    [Fact]
    public async Task Migrated_legal_document_sha256_check_rejects_invalid_values()
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        await PostgresSchema.MigrateAsync(database.DataSource);

        foreach (var invalidHash in InvalidHashes())
        {
            await using var command = database.DataSource.CreateCommand("""
                INSERT INTO legal_documents (
                    case_id,
                    document_id,
                    source_name,
                    raw_content,
                    content,
                    content_sha256)
                VALUES ($1, $2, $3, $4, $5, $6);
                """);
            var suffix = Guid.NewGuid().ToString("N");
            command.Parameters.AddWithValue($"schema-check-case-{suffix}");
            command.Parameters.AddWithValue($"schema-check-document-{suffix}");
            command.Parameters.AddWithValue("schema-check");
            command.Parameters.AddWithValue("raw");
            command.Parameters.AddWithValue("normalized");
            command.Parameters.AddWithValue(invalidHash);

            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        }
    }

    [Fact]
    public async Task Migrated_process_summary_hash_checks_reject_invalid_values()
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        await PostgresSchema.MigrateAsync(database.DataSource);

        foreach (var invalidHash in InvalidHashes())
        {
            await using var command = database.DataSource.CreateCommand("""
                INSERT INTO process_summary_jobs (
                    job_id,
                    scoped_idempotency_key,
                    tenant_id_hash,
                    subject_id_hash,
                    case_id,
                    snapshot_sha256,
                    job_json,
                    created_at,
                    updated_at)
                VALUES ($1, $2, $3, $4, $5, $6, '{}'::jsonb, now(), now());
                """);
            command.Parameters.AddWithValue($"job-{Guid.NewGuid():N}");
            command.Parameters.AddWithValue($"scope-{Guid.NewGuid():N}");
            command.Parameters.AddWithValue(invalidHash);
            command.Parameters.AddWithValue(new string('b', 64));
            command.Parameters.AddWithValue("case-1");
            command.Parameters.AddWithValue(new string('c', 64));

            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        }
    }

    [Fact]
    public async Task PostgresReadinessProbe_passes_after_explicit_migration()
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        await PostgresSchema.MigrateAsync(database.DataSource);
        var probe = new PostgresReadinessProbe(database.DataSource);

        await probe.CheckAsync(CancellationToken.None);
    }

    private static string[] InvalidHashes() =>
    [
        new string('a', 63),
        new string('A', 64),
        new string('g', 64)
    ];

    private static NpgsqlDataSource CreateDataSourceOrSkip()
    {
        var connectionString = Environment.GetEnvironmentVariable("RJ_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip("RJ_POSTGRES_CONNECTION is required for PostgreSQL integration tests.");
        }

        return NpgsqlDataSource.Create(connectionString);
    }

    private static async Task CreateLedgerOnlyAsync(NpgsqlDataSource dataSource, int version)
    {
        await using var createCommand = dataSource.CreateCommand("""
            CREATE TABLE IF NOT EXISTS rj_schema_migrations (
                version integer PRIMARY KEY,
                applied_at timestamptz NOT NULL DEFAULT now()
            );
            """);
        await createCommand.ExecuteNonQueryAsync();

        await using var insertCommand = dataSource.CreateCommand("""
            INSERT INTO rj_schema_migrations (version)
            VALUES ($1)
            ON CONFLICT (version) DO NOTHING;
            """);
        insertCommand.Parameters.AddWithValue(version);
        await insertCommand.ExecuteNonQueryAsync();
    }

    private static async Task<int> ReadLedgerVersionAsync(NpgsqlDataSource dataSource)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT COALESCE(MAX(version), 0) FROM rj_schema_migrations;");
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(),
            CultureInfo.InvariantCulture);
    }

    private static async Task<bool> TableExistsAsync(NpgsqlDataSource dataSource, string tableName)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = 'public'
                  AND table_name = $1);
            """);
        command.Parameters.AddWithValue(tableName);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<bool> ConstraintExistsAsync(
        NpgsqlDataSource dataSource,
        string tableName,
        string constraintName,
        string constraintType)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM pg_constraint con
                WHERE con.conrelid = to_regclass('public.' || $1)
                  AND con.conname = $2
                  AND con.contype::text = $3
                  AND con.convalidated
                  AND con.conenforced);
            """);
        command.Parameters.AddWithValue(tableName);
        command.Parameters.AddWithValue(constraintName);
        command.Parameters.AddWithValue(constraintType);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<bool> IndexExistsAsync(NpgsqlDataSource dataSource, string indexName)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM pg_class idx
                JOIN pg_namespace n ON n.oid = idx.relnamespace
                JOIN pg_index i ON i.indexrelid = idx.oid
                WHERE n.nspname = 'public'
                  AND idx.relname = $1
                  AND i.indisvalid
                  AND i.indisready
                  AND i.indislive);
            """);
        command.Parameters.AddWithValue(indexName);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }
}
