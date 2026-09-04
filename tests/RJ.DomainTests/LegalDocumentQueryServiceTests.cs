using RJ.Application.Retrieval;
using RJ.Domain.Cases;
using RJ.Domain.Documents;

namespace RJ.DomainTests;

public sealed class LegalDocumentQueryServiceTests
{
    [Fact]
    public async Task GetAsync_converts_transport_identifiers_and_preserves_case_scope()
    {
        var reader = new CapturingReader();
        var search = new CapturingSearch();
        var service = new LegalDocumentQueryService(reader, search);

        await service.GetAsync(" case-1 ", " doc-1 ", CancellationToken.None);

        Assert.Equal("case-1", reader.CaseId?.Value);
        Assert.Equal("doc-1", reader.DocumentId?.Value);
    }

    [Fact]
    public async Task SearchAsync_forwards_requested_case_query_and_limit()
    {
        var reader = new CapturingReader();
        var search = new CapturingSearch();
        var service = new LegalDocumentQueryService(reader, search);

        await service.SearchAsync("case-1", "tutela provisoria", 25, CancellationToken.None);

        Assert.Equal("case-1", search.CaseId?.Value);
        Assert.Equal("tutela provisoria", search.Query);
        Assert.Equal(25, search.Limit);
    }

    [Fact]
    public async Task RetrieveEvidenceAsync_returns_excerpt_with_verifiable_raw_offsets()
    {
        const string raw = "cabecalho\r\nA tutela provisoria foi deferida pelo juizo.\r\nrodape";
        var snapshot = new LegalDocumentSnapshot(
            "case-1",
            "doc-1",
            "decisao.txt",
            raw,
            raw.Replace("\r\n", "\n", StringComparison.Ordinal),
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
        var search = new CapturingSearch([new LegalDocumentSearchHit(snapshot, 1.5f)]);
        var service = new LegalDocumentQueryService(new CapturingReader(), search);

        var evidence = await service.RetrieveEvidenceAsync("case-1", "tutela provisoria", 10, CancellationToken.None);

        var hit = Assert.Single(evidence);
        Assert.Equal(raw.Substring(hit.Position.StartOffset, hit.Position.Length), hit.Excerpt);
        Assert.Contains("tutela provisoria", hit.Excerpt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(snapshot.ContentSha256, hit.ContentSha256);
    }

    [Fact]
    public async Task RetrieveEvidenceAsync_drops_hit_when_raw_text_cannot_be_located()
    {
        var snapshot = new LegalDocumentSnapshot(
            "case-1",
            "doc-1",
            "decisao.txt",
            "texto sem os termos pesquisados",
            "texto sem os termos pesquisados",
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
        var search = new CapturingSearch([new LegalDocumentSearchHit(snapshot, 1.0f)]);
        var service = new LegalDocumentQueryService(new CapturingReader(), search);

        var evidence = await service.RetrieveEvidenceAsync("case-1", "tutela provisoria", 10, CancellationToken.None);

        Assert.Empty(evidence);
    }

    [Fact]
    public void SourcePosition_rejects_ranges_outside_source()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SourcePosition.Create(-1, 1, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => SourcePosition.Create(0, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => SourcePosition.Create(8, 3, 10));
    }

    [Fact]
    public async Task Invalid_case_identifier_is_rejected_before_reader_execution()
    {
        var reader = new CapturingReader();
        var service = new LegalDocumentQueryService(reader, new CapturingSearch());

        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync(" ", CancellationToken.None));

        Assert.False(reader.WasCalled);
    }

    private sealed class CapturingReader : ILegalDocumentReader
    {
        public LegalCaseId? CaseId { get; private set; }
        public LegalDocumentId? DocumentId { get; private set; }
        public bool WasCalled { get; private set; }

        public Task<LegalDocumentSnapshot?> GetAsync(LegalCaseId caseId, LegalDocumentId documentId, CancellationToken cancellationToken)
        {
            WasCalled = true;
            CaseId = caseId;
            DocumentId = documentId;
            return Task.FromResult<LegalDocumentSnapshot?>(null);
        }

        public Task<IReadOnlyList<LegalDocumentSnapshot>> ListByCaseAsync(LegalCaseId caseId, CancellationToken cancellationToken)
        {
            WasCalled = true;
            CaseId = caseId;
            return Task.FromResult<IReadOnlyList<LegalDocumentSnapshot>>([]);
        }
    }

    private sealed class CapturingSearch(IReadOnlyList<LegalDocumentSearchHit>? hits = null) : ILegalDocumentSearch
    {
        private readonly IReadOnlyList<LegalDocumentSearchHit> _hits = hits ?? [];

        public LegalCaseId? CaseId { get; private set; }
        public string? Query { get; private set; }
        public int Limit { get; private set; }

        public Task<IReadOnlyList<LegalDocumentSearchHit>> SearchAsync(LegalCaseId caseId, string query, int limit, CancellationToken cancellationToken)
        {
            CaseId = caseId;
            Query = query;
            Limit = limit;
            return Task.FromResult(_hits);
        }
    }
}
