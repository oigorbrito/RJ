using Microsoft.AspNetCore.Http;
using RJ.Api;
using RJ.Application.Retrieval;
using RJ.Domain.Cases;
using RJ.Domain.Documents;

namespace RJ.ApiTests;

public sealed class ReadEndpointTests
{
    [Fact]
    public async Task ListDocumentsAsync_returns_compact_page_and_passes_offset_limit_to_reader()
    {
        var snapshot = Snapshot();
        var reader = new StubReader([snapshot]);
        var service = new LegalDocumentQueryService(reader, new StubSearch([]));

        var result = await ReadEndpoint.ListDocumentsAsync(
            "case-1",
            2,
            25,
            service,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        var page = Assert.IsType<LegalDocumentPage>(((IValueHttpResult)result).Value);
        var item = Assert.Single(page.Items);
        Assert.Equal(2, page.Page);
        Assert.Equal(25, page.PageSize);
        Assert.Equal("doc-1", item.DocumentId);
        Assert.Equal(25, reader.ObservedOffset);
        Assert.Equal(25, reader.ObservedLimit);
    }

    [Fact]
    public async Task ListDocumentsAsync_rejects_invalid_page_before_reader_execution()
    {
        var reader = new StubReader([Snapshot()]);
        var service = new LegalDocumentQueryService(reader, new StubSearch([]));

        var result = await ReadEndpoint.ListDocumentsAsync(
            "case-1",
            0,
            20,
            service,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ApiReadError>(((IValueHttpResult)result).Value);
        Assert.Equal("invalid_request", error.Code);
        Assert.Null(reader.ObservedOffset);
    }

    [Fact]
    public async Task GetDocumentAsync_returns_explicit_detail_projection()
    {
        var snapshot = Snapshot();
        var service = new LegalDocumentQueryService(new StubReader([snapshot]), new StubSearch([]));

        var result = await ReadEndpoint.GetDocumentAsync(
            "case-1",
            "doc-1",
            service,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        var detail = Assert.IsType<LegalDocumentDetail>(((IValueHttpResult)result).Value);
        Assert.Equal(snapshot.CaseId, detail.CaseId);
        Assert.Equal(snapshot.DocumentId, detail.DocumentId);
        Assert.Equal(snapshot.SourceName, detail.SourceName);
        Assert.Equal(snapshot.RawContent, detail.RawContent);
        Assert.Equal(snapshot.Content, detail.Content);
        Assert.Equal(snapshot.ContentSha256, detail.ContentSha256);
    }

    [Fact]
    public async Task GetDocumentAsync_returns_404_for_missing_document()
    {
        var service = new LegalDocumentQueryService(new StubReader([]), new StubSearch([]));

        var result = await ReadEndpoint.GetDocumentAsync(
            "case-1",
            "doc-missing",
            service,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task GetDocumentAsync_returns_stable_invalid_request_error()
    {
        var service = new LegalDocumentQueryService(new StubReader([]), new StubSearch([]));

        var result = await ReadEndpoint.GetDocumentAsync(
            "case-1",
            "",
            service,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ApiReadError>(((IValueHttpResult)result).Value);
        Assert.Equal("invalid_request", error.Code);
    }

    [Fact]
    public async Task SearchAsync_returns_metadata_and_rank_without_raw_or_normalized_content()
    {
        var snapshot = Snapshot();
        var service = new LegalDocumentQueryService(
            new StubReader([]),
            new StubSearch([new LegalDocumentSearchHit(snapshot, 0.75f)]));

        var result = await ReadEndpoint.SearchAsync(
            "case-1",
            "tutela",
            20,
            service,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        var hits = Assert.IsType<LegalDocumentSearchResult[]>(((IValueHttpResult)result).Value);
        var hit = Assert.Single(hits);
        Assert.Equal("doc-1", hit.DocumentId);
        Assert.Equal(snapshot.ContentSha256, hit.ContentSha256);
        Assert.Equal(0.75f, hit.Rank);
    }

    [Fact]
    public async Task RetrieveEvidenceAsync_returns_stable_invalid_request_error()
    {
        var service = new LegalDocumentQueryService(new StubReader([]), new StubSearch([]));

        var result = await ReadEndpoint.RetrieveEvidenceAsync(
            "",
            "tutela",
            20,
            service,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ApiReadError>(((IValueHttpResult)result).Value);
        Assert.Equal("invalid_request", error.Code);
    }

    private static LegalDocumentSnapshot Snapshot() => new(
        "case-1",
        "doc-1",
        "source.txt",
        new string('r', 10_000),
        new string('n', 10_000),
        new string('a', 64));

    private sealed class StubReader(IReadOnlyList<LegalDocumentSnapshot> documents) : ILegalDocumentReader
    {
        public int? ObservedOffset { get; private set; }
        public int? ObservedLimit { get; private set; }

        public Task<LegalDocumentSnapshot?> GetAsync(
            LegalCaseId caseId,
            LegalDocumentId documentId,
            CancellationToken cancellationToken) =>
            Task.FromResult<LegalDocumentSnapshot?>(documents.FirstOrDefault());

        public Task<IReadOnlyList<LegalDocumentSnapshot>> ListByCaseAsync(
            LegalCaseId caseId,
            int offset,
            int limit,
            CancellationToken cancellationToken)
        {
            ObservedOffset = offset;
            ObservedLimit = limit;
            return Task.FromResult(documents);
        }
    }

    private sealed class StubSearch(IReadOnlyList<LegalDocumentSearchHit> hits) : ILegalDocumentSearch
    {
        public Task<IReadOnlyList<LegalDocumentSearchHit>> SearchAsync(
            LegalCaseId caseId,
            string query,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult(hits);
    }
}
