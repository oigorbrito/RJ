using System.Globalization;
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
    public async Task StoreAsync_concurrent_same_identity_and_hash_converges_to_one_row_without_error()
    {
        await using var dataSource = await CreateDataSourceAsync();
        var writer = new PostgresLegalDocumentWriter(dataSource);
        var document = CreateDocument(HashA, "concurrent raw", "concurrent normalized");

        await Task.WhenAll(
            writer.StoreAsync(document, CancellationToken.None),
            writer.StoreAsync(document, CancellationToken.None));

        Assert.Equal(1, await CountDocumentsAsync(dataSource, document.CaseId.Value));
        var stored = await ReadStoredAsync(dataSource, document.CaseId.Value, document.Id.Value);
        Assert.NotNull(stored);
        Assert.Equal(document.SourceName, stored.Value.SourceName);
        Assert.Equal(document.RawContent, stored.Value.RawContent);
        Assert.Equal(document.Content, stored.Value.Content);
        Assert.Equal(document.ContentSha256, stored.Value.ContentSha256);
    }

    [Fact]
    public async Task StoreAsync_concurrent_same_identity_with_different_hashes_commits_exactly_one_complete_document()
    {
        await using var dataSource = await CreateDataSourceAsync();
        var writer = new PostgresLegalDocumentWriter(dataSource);
        var caseId = new LegalCaseId($"case-{Guid.NewGuid():N}");
        var documentId = new LegalDocumentId("doc-1");
        var first = new LegalDocument(
            documentId,
            caseId,
            "first.pdf",
            "first raw",
            "first normalized",
            HashA);
        var second = new LegalDocument(
            documentId,
            caseId,
            "second.pdf",
            "second raw",
            "second normalized",
            HashB);

        var outcomes = await Task.WhenAll(
            CaptureStoreOutcomeAsync(writer, first),
            CaptureStoreOutcomeAsync(writer, second));

        Assert.Equal(1, outcomes.Count(outcome => outcome is null));
        var conflict = Assert.Single(outcomes.OfType<LegalDocumentConflictException>());
        Assert.Contains("Legal document conflict", conflict.Message, StringComparison.Ordinal);
        Assert.Equal(1, await CountDocumentsAsync(dataSource, caseId.Value));

        var stored = await ReadStoredAsync(dataSource, caseId.Value, documentId.Value);
        Assert.NotNull(stored);
        var matchesFirst = StoredMatches(stored.Value, first);
        var matchesSecond = StoredMatches(stored.Value, second);
        Assert.True(matchesFirst ^ matchesSecond);
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

    [Fact]
    public async Task StoreAsync_times_out_when_legal_documents_is_locked_exclusively()
    {
        await using var dataSource = await CreateDataSourceAsync();
        await using var lockConnection = await dataSource.OpenConnectionAsync();
        await using var lockTransaction = await lockConnection.BeginTransactionAsync();
        await using var lockCommand = new NpgsqlCommand(
            "LOCK TABLE legal_documents IN ACCESS EXCLUSIVE MODE;",
            lockConnection,
            lockTransaction);
        await lockCommand.ExecuteNonQueryAsync();

        var writer = new PostgresLegalDocumentWriter(dataSource);
        var document = CreateDocument(HashA, "timeout raw", "timeout normalized");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var storeTask = writer.StoreAsync(document, CancellationToken.None);
        var completed = await Task.WhenAny(storeTask, Task.Delay(TimeSpan.FromSeconds(30)));
        Assert.Same(storeTask, completed);

        Exception? exception = null;
        try
        {
            await storeTask;
        }
        catch (Exception caught)
        {
            exception = caught;
        }
        finally
        {
            await lockTransaction.RollbackAsync();
        }

        stopwatch.Stop();

        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(14), TimeSpan.FromSeconds(30));
        Assert.NotNull(exception);
        Assert.True(exception is NpgsqlException or TimeoutException or OperationCanceledException);
        Assert.Equal(0, await CountDocumentsAsync(dataSource, document.CaseId.Value));
    }

    private static async Task<Exception?> CaptureStoreOutcomeAsync(
        PostgresLegalDocumentWriter writer,
        LegalDocument document)
    {
        try
        {
            await writer.StoreAsync(document, CancellationToken.None);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static bool StoredMatches(
        (string SourceName, string RawContent, string Content, string ContentSha256) stored,
        LegalDocument document) =>
        StringComparer.Ordinal.Equals(stored.SourceName, document.SourceName)
        && StringComparer.Ordinal.Equals(stored.RawContent, document.RawContent)
        && StringComparer.Ordinal.Equals(stored.Content, document.Content)
        && StringComparer.Ordinal.Equals(stored.ContentSha256, document.ContentSha256);

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
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
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
