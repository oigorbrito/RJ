using Npgsql;
using RJ.Application.Retrieval;
using RJ.Domain.Cases;
using RJ.Domain.Documents;
using RJ.Infrastructure.Persistence;

namespace RJ.IntegrationTests;

public sealed class PostgresLegalDocumentRetrievalTests
{
    private const string HashA = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string HashB = "2222222222222222222222222222222222222222222222222222222222222222";
    private const string HashC = "3333333333333333333333333333333333333333333333333333333333333333";

    [Fact]
    public async Task Reader_gets_document_by_case_and_document_identity()
    {
        await using var dataSource = await CreateDataSourceAsync();
        var writer = new PostgresLegalDocumentWriter(dataSource);
        var reader = new PostgresLegalDocumentReader(dataSource);
        var caseId = NewCaseId();
        var document = CreateDocument("doc-1", caseId, "peticao.pdf", "pedido de tutela provisoria", HashA);

        await writer.StoreAsync(document, CancellationToken.None);

        var result = await reader.GetAsync(caseId, document.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(document.ContentSha256, result.ContentSha256);
    }

    [Fact]
    public async Task Reader_lists_only_documents_from_requested_case_in_document_order()
    {
        await using var dataSource = await CreateDataSourceAsync();
        var writer = new PostgresLegalDocumentWriter(dataSource);
        var reader = new PostgresLegalDocumentReader(dataSource);
        var targetCase = NewCaseId();
        var otherCase = NewCaseId();

        await writer.StoreAsync(CreateDocument("doc-b", targetCase, "b.pdf", "conteudo b", HashA), CancellationToken.None);
        await writer.StoreAsync(CreateDocument("doc-a", targetCase, "a.pdf", "conteudo a", HashB), CancellationToken.None);
        await writer.StoreAsync(CreateDocument("doc-x", otherCase, "x.pdf", "conteudo x", HashC), CancellationToken.None);

        var results = await reader.ListByCaseAsync(targetCase, CancellationToken.None);

        Assert.Equal(["doc-a", "doc-b"], results.Select(item => item.DocumentId).ToArray());
    }

    [Fact]
    public async Task Search_is_case_scoped_and_returns_matching_portuguese_text()
    {
        await using var dataSource = await CreateDataSourceAsync();
        var writer = new PostgresLegalDocumentWriter(dataSource);
        var search = new PostgresLegalDocumentSearch(dataSource);
        var targetCase = NewCaseId();
        var otherCase = NewCaseId();

        await writer.StoreAsync(CreateDocument("doc-1", targetCase, "decisao.pdf", "A tutela provisoria foi deferida pelo juizo.", HashA), CancellationToken.None);
        await writer.StoreAsync(CreateDocument("doc-2", targetCase, "contrato.pdf", "Contrato de prestacao de servicos.", HashB), CancellationToken.None);
        await writer.StoreAsync(CreateDocument("doc-3", otherCase, "decisao.pdf", "A tutela provisoria foi deferida.", HashC), CancellationToken.None);

        var results = await search.SearchAsync(targetCase, "tutela provisoria", 10, CancellationToken.None);

        var hit = Assert.Single(results);
        Assert.Equal("doc-1", hit.Document.DocumentId);
        Assert.True(hit.Rank > 0);
    }

    [Fact]
    public async Task Evidence_is_case_scoped_and_offsets_reproduce_exact_raw_excerpt()
    {
        await using var dataSource = await CreateDataSourceAsync();
        var writer = new PostgresLegalDocumentWriter(dataSource);
        var reader = new PostgresLegalDocumentReader(dataSource);
        var search = new PostgresLegalDocumentSearch(dataSource);
        var service = new LegalDocumentQueryService(reader, search);
        var targetCase = NewCaseId();
        var otherCase = NewCaseId();
        const string raw = "cabecalho\r\nA tutela provisoria foi deferida pelo juizo.\r\nrodape";
        var normalized = raw.Replace("\r\n", "\n", StringComparison.Ordinal);

        await writer.StoreAsync(new LegalDocument(new LegalDocumentId("doc-1"), targetCase, "decisao.txt", raw, normalized, HashA), CancellationToken.None);
        await writer.StoreAsync(new LegalDocument(new LegalDocumentId("doc-2"), otherCase, "decisao.txt", raw, normalized, HashB), CancellationToken.None);

        var evidence = await service.RetrieveEvidenceAsync(targetCase.Value, "tutela provisoria", 10, CancellationToken.None);

        var hit = Assert.Single(evidence);
        Assert.Equal(targetCase.Value, hit.CaseId);
        Assert.Equal("doc-1", hit.DocumentId);
        Assert.Equal(raw.Substring(hit.Position.StartOffset, hit.Position.Length), hit.Excerpt);
    }

    [Fact]
    public async Task Search_rejects_empty_query_and_invalid_limit_before_database_query()
    {
        await using var dataSource = await CreateDataSourceAsync();
        var search = new PostgresLegalDocumentSearch(dataSource);
        var caseId = NewCaseId();

        await Assert.ThrowsAsync<ArgumentException>(() => search.SearchAsync(caseId, " ", 10, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => search.SearchAsync(caseId, "tutela", 0, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => search.SearchAsync(caseId, "tutela", 101, CancellationToken.None));
    }

    private static LegalDocument CreateDocument(
        string id,
        LegalCaseId caseId,
        string sourceName,
        string content,
        string hash) => new(
            new LegalDocumentId(id),
            caseId,
            sourceName,
            content,
            content,
            hash);

    private static LegalCaseId NewCaseId() => new($"case-{Guid.NewGuid():N}");

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
}
