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
        await using var database = await CreateTemporaryDatabaseAsync();
        await PostgresSchema.MigrateAsync(database.DataSource);

        await PostgresSchema.EnsureCurrentAsync(database.DataSource);
    }

    [Fact]
    public async Task EnsureCurrentAsync_rejects_missing_ledger_without_mutating_schema()
    {
        await using var database = await CreateTemporaryDatabaseAsync();

        var exception = await Assert.ThrowsAsync<PostgresSchemaVersionException>(
            () => PostgresSchema.EnsureCurrentAsync(database.DataSource));

        Assert.Contains("ledger is missing", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, await CountRowsAsync(database.DataSource, "rj_schema_migrations"));
        Assert.False(await TableExistsAsync(database.DataSource, "legal_documents"));
    }

    [Fact]
    public async Task EnsureCurrentAsync_rejects_older_ledger_version_without_mutating_schema()
    {
        await using var database = await CreateTemporaryDatabaseAsync();
        await CreateLedgerOnlyAsync(database.DataSource, 2);

        var exception = await Assert.ThrowsAsync<PostgresSchemaVersionException>(
            () => PostgresSchema.EnsureCurrentAsync(database.DataSource));

        Assert.Contains("does not match required version 3", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, await ReadLedgerVersionAsync(database.DataSource));
        Assert.False(await TableExistsAsync(database.DataSource, "legal_documents"));
    }

    [Fact]
    public async Task EnsureCurrentAsync_rejects_newer_ledger_version_without_mutating_schema()
    {
        await using var database = await CreateTemporaryDatabaseAsync();
        await CreateLedgerOnlyAsync(database.DataSource, 4);

        var exception = await Assert.ThrowsAsync<PostgresSchemaVersionException>(
            () => PostgresSchema.EnsureCurrentAsync(database.DataSource));

        Assert.Contains("does not match required version 3", exception.Message, StringComparison.Ordinal);
        Assert.Equal(4, await ReadLedgerVersionAsync(database.DataSource));
        Assert.False(await TableExistsAsync(database.DataSource, "legal_documents"));
    }

    [Fact]
    public async Task EnsureCurrentAsync_rejects_structurally_degraded_schema_without_recreating_index()
    {
        await using var database = await CreateTemporaryDatabaseAsync();
        await PostgresSchema.MigrateAsync(database.DataSource);
        await DropSearchIndexAsync(database.DataSource);

        var exception = await Assert.ThrowsAsync<PostgresSchemaVersionException>(
            () => PostgresSchema.EnsureCurrentAsync(database.DataSource));

        Assert.Contains("required schema invariants are missing or degraded", exception.Message, StringComparison.Ordinal);
        Assert.Equal(3, await ReadLedgerVersionAsync(database.DataSource));
        Assert.False(await SearchIndexExistsAsync(database.DataSource));
    }

    [Fact]
    public async Task Migrated_schema_contains_required_structural_constraints_and_gin_index()
    {
        await using var dataSource = CreateDataSourceOrSkip();
        await PostgresSchema.MigrateAsync(dataSource);

        const string sql = """
            WITH attrs AS (
                SELECT
                    max(attnum) FILTER (WHERE attname = 'case_id' AND NOT attisdropped) AS case_id_attnum,
                    max(attnum) FILTER (WHERE attname = 'document_id' AND NOT attisdropped) AS document_id_attnum,
                    max(attnum) FILTER (WHERE attname = 'content_sha256' AND NOT attisdropped) AS content_sha256_attnum,
                    max(attnum) FILTER (WHERE attname = 'search_vector' AND NOT attisdropped) AS search_vector_attnum
                FROM pg_attribute
                WHERE attrelid = 'public.legal_documents'::regclass
            )
            SELECT
                EXISTS (
                    SELECT 1
                    FROM pg_constraint con
                    CROSS JOIN attrs a
                    WHERE con.conrelid = 'public.legal_documents'::regclass
                      AND con.conname = 'pk_legal_documents'
                      AND con.contype = 'p'
                      AND con.convalidated
                      AND con.conenforced
                      AND con.conkey = ARRAY[a.case_id_attnum, a.document_id_attnum]::smallint[]) AS has_primary_key,
                EXISTS (
                    SELECT 1
                    FROM pg_constraint con
                    CROSS JOIN attrs a
                    WHERE con.conrelid = 'public.legal_documents'::regclass
                      AND con.conname = 'uq_legal_documents_case_hash'
                      AND con.contype = 'u'
                      AND con.convalidated
                      AND con.conenforced
                      AND con.conkey = ARRAY[a.case_id_attnum, a.content_sha256_attnum]::smallint[]) AS has_case_hash_unique,
                EXISTS (
                    SELECT 1
                    FROM pg_constraint con
                    CROSS JOIN attrs a
                    WHERE con.conrelid = 'public.legal_documents'::regclass
                      AND con.conname = 'ck_legal_documents_sha256'
                      AND con.contype = 'c'
                      AND con.convalidated
                      AND con.conenforced
                      AND con.conkey = ARRAY[a.content_sha256_attnum]::smallint[]
                      AND pg_get_expr(con.conbin, con.conrelid) LIKE '%content_sha256%^[0-9a-f]{64}$%') AS has_sha_check,
                EXISTS (
                    SELECT 1
                    FROM pg_class idx
                    JOIN pg_index i ON i.indexrelid = idx.oid
                    JOIN pg_am am ON am.oid = idx.relam
                    CROSS JOIN attrs a
                    WHERE i.indrelid = 'public.legal_documents'::regclass
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
                      AND i.indpred IS NULL) AS has_search_gin;
            """;

        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.True(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
        Assert.True(reader.GetBoolean(3));
    }

    [Fact]
    public async Task Sha256_check_accepts_valid_value_and_rejects_invalid_values_in_isolated_schema()
    {
        await using var dataSource = CreateDataSourceOrSkip();
        var schemaName = $"rj_sha_check_{Guid.NewGuid():N}";

        await using var connection = await dataSource.OpenConnectionAsync();
        try
        {
            await using (var setup = new NpgsqlCommand($$"""
                CREATE SCHEMA "{{schemaName}}";
                CREATE TABLE "{{schemaName}}".hash_probe (
                    content_sha256 char(64) NOT NULL,
                    CONSTRAINT ck_hash_probe_sha256 CHECK (content_sha256 ~ '^[0-9a-f]{64}$')
                );
                """, connection))
            {
                await setup.ExecuteNonQueryAsync();
            }

            await using (var valid = new NpgsqlCommand(
                $"INSERT INTO \"{schemaName}\".hash_probe (content_sha256) VALUES ($1);",
                connection))
            {
                valid.Parameters.AddWithValue(new string('a', 64));
                Assert.Equal(1, await valid.ExecuteNonQueryAsync());
            }

            foreach (var invalidHash in new[]
                     {
                         new string('a', 63),
                         new string('A', 64),
                         new string('g', 64)
                     })
            {
                await using var invalid = new NpgsqlCommand(
                    $"INSERT INTO \"{schemaName}\".hash_probe (content_sha256) VALUES ($1);",
                    connection);
                invalid.Parameters.AddWithValue(invalidHash);
                var exception = await Assert.ThrowsAsync<PostgresException>(
                    () => invalid.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
            }

            await using (var overlength = new NpgsqlCommand(
                $"INSERT INTO \"{schemaName}\".hash_probe (content_sha256) VALUES ($1);",
                connection))
            {
                overlength.Parameters.AddWithValue(new string('a', 65));
                var exception = await Assert.ThrowsAsync<PostgresException>(
                    () => overlength.ExecuteNonQueryAsync());
                Assert.Equal("22001", exception.SqlState);
            }
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand(
                $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE;",
                connection);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Migrated_sha256_check_rejects_invalid_values_without_schema_mutation()
    {
        await using var dataSource = CreateDataSourceOrSkip();
        await PostgresSchema.MigrateAsync(dataSource);

        foreach (var invalidHash in new[]
                 {
                     new string('a', 63),
                     new string('A', 64),
                     new string('g', 64)
                 })
        {
            var suffix = Guid.NewGuid().ToString("N");
            await using var command = dataSource.CreateCommand("""
                INSERT INTO legal_documents (
                    case_id,
                    document_id,
                    source_name,
                    raw_content,
                    content,
                    content_sha256)
                VALUES ($1, $2, $3, $4, $5, $6);
                """);
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

    private static async Task<TemporaryPostgresDatabase> CreateTemporaryDatabaseAsync() =>
        await TemporaryPostgresDatabase.CreateAsync();

    private static async Task CreateLedgerOnlyAsync(NpgsqlDataSource dataSource, int version)
    {
        await using var command = dataSource.CreateCommand("""
            CREATE TABLE IF NOT EXISTS rj_schema_migrations (
                version integer PRIMARY KEY,
                applied_at timestamptz NOT NULL DEFAULT now()
            );

            INSERT INTO rj_schema_migrations (version)
            VALUES ($1)
            ON CONFLICT (version) DO NOTHING;
            """);
        command.Parameters.AddWithValue(version);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ReadLedgerVersionAsync(NpgsqlDataSource dataSource)
    {
        await using var command = dataSource.CreateCommand("SELECT COALESCE(MAX(version), 0) FROM rj_schema_migrations;");
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<long> CountRowsAsync(NpgsqlDataSource dataSource, string tableName)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT count(*)
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name = @table_name;
            """);
        command.Parameters.AddWithValue("table_name", tableName);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<bool> TableExistsAsync(NpgsqlDataSource dataSource, string tableName) =>
        await CountRowsAsync(dataSource, tableName) > 0;

    private static async Task DropSearchIndexAsync(NpgsqlDataSource dataSource)
    {
        await using var command = dataSource.CreateCommand("""
            DROP INDEX IF EXISTS public.ix_legal_documents_search_vector;
            """);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> SearchIndexExistsAsync(NpgsqlDataSource dataSource)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM pg_class idx
                JOIN pg_namespace n ON n.oid = idx.relnamespace
                WHERE n.nspname = 'public'
                  AND idx.relname = 'ix_legal_documents_search_vector');
            """);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
