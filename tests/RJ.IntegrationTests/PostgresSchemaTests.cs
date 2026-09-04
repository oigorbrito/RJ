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
        await using var dataSource = CreateDataSourceOrSkip();
        await PostgresSchema.MigrateAsync(dataSource);

        await PostgresSchema.EnsureCurrentAsync(dataSource);
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
                      AND i.indkey::smallint[] = ARRAY[a.search_vector_attnum]::smallint[]
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
    public async Task Sha256_check_accepts_only_lowercase_hexadecimal_length_64_in_isolated_schema()
    {
        await using var dataSource = CreateDataSourceOrSkip();
        var schemaName = $"rj_sha_check_{Guid.NewGuid():N}";

        await using var connection = await dataSource.OpenConnectionAsync();
        try
        {
            await using (var setup = new NpgsqlCommand($"""
                CREATE SCHEMA "{schemaName}";
                CREATE TABLE "{schemaName}".hash_probe (
                    content_sha256 char(64) NOT NULL,
                    CONSTRAINT ck_hash_probe_sha256 CHECK (content_sha256 ~ '^[0-9a-f]{{64}}$')
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
                         new string('a', 65),
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
}
