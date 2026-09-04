using Npgsql;
using RJ.Application.Ingestion;
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
    public async Task StoreAsync_preserves_raw_and_normalized_content()
    {
        await using var dataSource = await CreateDataSourceAsync();
        var writer = new PostgresLegalDocumentWriter(dataSource);
        var document = CreateDocument(HashA, "raw\r\ncontent", "raw\ncontent");

        await writer.StoreAsync(document, CancellationToken.None);

        var stored = await ReadStoredAsync(dataSource, document.CaseId.Value, document.Id.Value);
        Assert.NotNull(stored);
        Assert.Equal("raw\r\ncontent", stored.Value.RawContent);
        Assert.Equal("raw\ncontent", stored.Value.Content);
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
    public async Task StoreAsync_conflict_rolls_back_and_preserves_original_evidence()
    {
        await using var dataSource = await CreateDataSourceAsync();
        var writer = new PostgresLegalDocumentWriter(dataSource);
        var original = CreateDocument(HashA, "original raw", "original normalized");
        var conflicting = new LegalDocument(
            original.Id,
            original.CaseId,
            "conflicting-source.pdf",
            "different raw content",
            "different content",
            HashB);

        await writer.StoreAsync(original, CancellationToken.None);

        await Assert.ThrowsAsync<LegalDocumentConflictException>(
            () => writer.StoreAsync(conflicting, CancellationToken.None));

        Assert.Equal(1, await CountDocumentsAsync(dataSource, original.CaseId.Value));
        var stored = await ReadStoredAsync(dataSource, original.CaseId.Value, original.Id.Value);
        Assert.NotNull(stored);
        Assert.Equal(original.SourceName, stored.Value.SourceName);
        Assert.Equal(original.RawContent, stored.Value.RawContent);
        Assert.Equal(original.Content, stored.Value.Content);
        Assert.Equal(original.ContentSha256, stored.Value.ContentSha256);

        await writer.StoreAsync(original, CancellationToken.None);
        Assert.Equal(1, await CountDocumentsAsync(dataSource, original.CaseId.Value));
    }

    [Fact]
    public async Task StoreAsync_same_hash_conflict_rolls_back_without_second_identity()
    {
        await using var dataSource = await CreateDataSourceAsync();
        var writer = new PostgresLegalDocumentWriter(dataSource);
        var original = CreateDocument(HashA);
        var conflicting = new LegalDocument(
            new LegalDocumentId("doc-2"),
            original.CaseId,
            "source-2.pdf",
            original.RawContent,
            original.Content,
            original.ContentSha256);

        await writer.StoreAsync(original, CancellationToken.None);

        await Assert.ThrowsAsync<LegalDocumentConflictException>(
            () => writer.StoreAsync(conflicting, CancellationToken.None));

        Assert.Equal(1, await CountDocumentsAsync(dataSource, original.CaseId.Value));
        Assert.Null(await ReadStoredAsync(dataSource, original.CaseId.Value, conflicting.Id.Value));
        Assert.NotNull(await ReadStoredAsync(dataSource, original.CaseId.Value, original.Id.Value));
    }

    [Fact]
    public async Task StoreAsync_cancelled_attempt_writes_nothing_and_retry_succeeds_once()
    {
        await using var dataSource = await CreateDataSourceAsync();
        var writer = new PostgresLegalDocumentWriter(dataSource);
        var document = CreateDocument(HashA);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => writer.StoreAsync(document, cancellation.Token));

        Assert.Equal(0, await CountDocumentsAsync(dataSource, document.CaseId.Value));

        await writer.StoreAsync(document, CancellationToken.None);
        await writer.StoreAsync(document, CancellationToken.None);

        Assert.Equal(1, await CountDocumentsAsync(dataSource, document.CaseId.Value));
        var stored = await ReadStoredAsync(dataSource, document.CaseId.Value, document.Id.Value);
        Assert.NotNull(stored);
        Assert.Equal(document.ContentSha256, stored.Value.ContentSha256);
    }

    private static async Task<NpgsqlDataSource> CreateDataSourceAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("RJ_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip("RJ_POSTGRES_CONNECTION is required for PostgreSQL integration tests.");
        }

        var dataSource = NpgsqlDataSource.Create(connectionString);
        await PostgresSchema.MigrateAsync(dataSource);
        return dataSource;
    }

    private static LegalDocument CreateDocument(
        string hash,
        string rawContent = "document content",
        string normalizedContent = "document content")
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new LegalDocument(
            new LegalDocumentId("doc-1"),
            new LegalCaseId($"case-{suffix}"),
            "source.pdf",
            rawContent,
            normalizedContent,
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

    private static async Task<(string SourceName, string RawContent, string Content, string ContentSha256)?> ReadStoredAsync(
        NpgsqlDataSource dataSource,
        string caseId,
        string documentId)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT source_name, raw_content, content, content_sha256 FROM legal_documents WHERE case_id = @case_id AND document_id = @document_id;");
        command.Parameters.AddWithValue("case_id", caseId);
        command.Parameters.AddWithValue("document_id", documentId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return (
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3).TrimEnd());
    }
}
