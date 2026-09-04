using Npgsql;
using RJ.Domain.Cases;
using RJ.Domain.Documents;
using RJ.Infrastructure.Persistence;

namespace RJ.IntegrationTests;

public sealed class PostgresLegalDocumentWriterTests
{
    private const string HashA = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string HashB = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    [Fact]
    public async Task StoreAsync_persists_document_once()
    {
        await using var dataSource = await CreateDataSourceAsync();
        var writer = new PostgresLegalDocumentWriter(dataSource);
        var document = CreateDocument(HashA);

        await writer.StoreAsync(document, CancellationToken.None);

        Assert.Equal(1, await CountDocumentsAsync(dataSource, document.CaseId.Value));
    }

    [Fact]
    public async Task StoreAsync_is_idempotent_for_same_identity_and_hash()
    {
        await using var dataSource = await CreateDataSourceAsync();
        var writer = new PostgresLegalDocumentWriter(dataSource);
        var document = CreateDocument(HashA);

        await writer.StoreAsync(document, CancellationToken.None);
        await writer.StoreAsync(document, CancellationToken.None);

        Assert.Equal(1, await CountDocumentsAsync(dataSource, document.CaseId.Value));
    }

    [Fact]
    public async Task StoreAsync_rejects_same_identity_with_different_hash()
    {
        await using var dataSource = await CreateDataSourceAsync();
        var writer = new PostgresLegalDocumentWriter(dataSource);
        var original = CreateDocument(HashA);
        var conflicting = new LegalDocument(
            original.Id,
            original.CaseId,
            original.SourceName,
            "different content",
            HashB);

        await writer.StoreAsync(original, CancellationToken.None);

        await Assert.ThrowsAsync<LegalDocumentPersistenceConflictException>(
            () => writer.StoreAsync(conflicting, CancellationToken.None));

        Assert.Equal(1, await CountDocumentsAsync(dataSource, original.CaseId.Value));
    }

    [Fact]
    public async Task StoreAsync_rejects_same_hash_with_different_identity_in_same_case()
    {
        await using var dataSource = await CreateDataSourceAsync();
        var writer = new PostgresLegalDocumentWriter(dataSource);
        var original = CreateDocument(HashA);
        var conflicting = new LegalDocument(
            new LegalDocumentId("doc-2"),
            original.CaseId,
            "source-2.pdf",
            original.Content,
            original.ContentSha256);

        await writer.StoreAsync(original, CancellationToken.None);

        await Assert.ThrowsAsync<LegalDocumentPersistenceConflictException>(
            () => writer.StoreAsync(conflicting, CancellationToken.None));

        Assert.Equal(1, await CountDocumentsAsync(dataSource, original.CaseId.Value));
    }

    private static async Task<NpgsqlDataSource> CreateDataSourceAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("RJ_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip("RJ_POSTGRES_CONNECTION is required for PostgreSQL integration tests.");
        }

        var dataSource = NpgsqlDataSource.Create(connectionString);
        await PostgresSchema.InitializeAsync(dataSource);
        return dataSource;
    }

    private static LegalDocument CreateDocument(string hash)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new LegalDocument(
            new LegalDocumentId("doc-1"),
            new LegalCaseId($"case-{suffix}"),
            "source.pdf",
            "document content",
            hash);
    }

    private static async Task<long> CountDocumentsAsync(NpgsqlDataSource dataSource, string caseId)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT count(*) FROM legal_documents WHERE case_id = @case_id;");
        command.Parameters.AddWithValue("case_id", caseId);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }
}
