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

    private sealed class CapturingSearch : ILegalDocumentSearch
    {
        public LegalCaseId? CaseId { get; private set; }
        public string? Query { get; private set; }
        public int Limit { get; private set; }

        public Task<IReadOnlyList<LegalDocumentSearchHit>> SearchAsync(LegalCaseId caseId, string query, int limit, CancellationToken cancellationToken)
        {
            CaseId = caseId;
            Query = query;
            Limit = limit;
            return Task.FromResult<IReadOnlyList<LegalDocumentSearchHit>>([]);
        }
    }
}
