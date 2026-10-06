using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using RJ.Api;
using RJ.Application.Retrieval;
using RJ.Domain.Cases;
using RJ.Domain.Documents;

namespace RJ.ApiTests;

public sealed class ReadEndpointAuthorizationTests
{
    [Fact]
    public async Task List_filters_documents_from_unauthorized_evidence_sources()
    {
        var judit = Document("doc-judit", "Judit", 'a');
        var dataJud = Document("doc-datajud", "DataJud", 'b');
        var service = new LegalDocumentQueryService(
            new FixedReader([judit, dataJud]),
            new FixedSearch([]));
        var context = Context("Judit");

        var result = await ReadEndpoint.ListDocumentsAuthorizedAsync(
            context,
            "case-1",
            1,
            20,
            new ClaimsProcessSummaryCallerContextResolver(),
            service,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        var page = Assert.IsType<LegalDocumentPage>(((IValueHttpResult)result).Value);
        var item = Assert.Single(page.Items);
        Assert.Equal("Judit", item.SourceName);
        Assert.DoesNotContain(page.Items, document => document.SourceName == "DataJud");
    }

    [Fact]
    public async Task Get_document_hides_document_from_unauthorized_source()
    {
        var dataJud = Document("doc-datajud", "DataJud", 'b');
        var service = new LegalDocumentQueryService(
            new FixedReader([dataJud]),
            new FixedSearch([]));
        var context = Context("Judit");

        var result = await ReadEndpoint.GetDocumentAuthorizedAsync(
            context,
            "case-1",
            "doc-datajud",
            new ClaimsProcessSummaryCallerContextResolver(),
            service,
            CancellationToken.None);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task Search_filters_hits_from_unauthorized_sources()
    {
        var judit = Document("doc-judit", "Judit", 'a');
        var dataJud = Document("doc-datajud", "DataJud", 'b');
        var service = new LegalDocumentQueryService(
            new FixedReader([]),
            new FixedSearch([
                new LegalDocumentSearchHit(judit, 2f),
                new LegalDocumentSearchHit(dataJud, 1f)
            ]));
        var context = Context("Judit");

        var result = await ReadEndpoint.SearchAuthorizedAsync(
            context,
            "case-1",
            "prazo",
            20,
            new ClaimsProcessSummaryCallerContextResolver(),
            service,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        var hits = Assert.IsAssignableFrom<IReadOnlyList<LegalDocumentSearchResult>>(((IValueHttpResult)result).Value);
        var hit = Assert.Single(hits);
        Assert.Equal("Judit", hit.SourceName);
    }

    private static DefaultHttpContext Context(string evidenceSource) =>
        new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "subject-1"),
                new Claim(ClaimsProcessSummaryCallerContextResolver.TenantIdClaim, "tenant-1"),
                new Claim(ClaimsProcessSummaryCallerContextResolver.CaseIdClaim, "case-1"),
                new Claim(ClaimsProcessSummaryCallerContextResolver.EvidenceSourceClaim, evidenceSource)
            ], "test"))
        };

    private static LegalDocumentSnapshot Document(string documentId, string sourceName, char hashCharacter) =>
        new(
            "case-1",
            documentId,
            sourceName,
            "prazo observado",
            "prazo observado",
            new string(hashCharacter, 64));

    private sealed class FixedReader(IReadOnlyList<LegalDocumentSnapshot> documents) : ILegalDocumentReader
    {
        public Task<LegalDocumentSnapshot?> GetAsync(
            LegalCaseId caseId,
            LegalDocumentId documentId,
            CancellationToken cancellationToken) =>
            Task.FromResult(documents.FirstOrDefault(document => document.DocumentId == documentId.Value));

        public Task<IReadOnlyList<LegalDocumentSnapshot>> ListByCaseAsync(
            LegalCaseId caseId,
            int offset,
            int limit,
            CancellationToken cancellationToken) => Task.FromResult(documents);
    }

    private sealed class FixedSearch(IReadOnlyList<LegalDocumentSearchHit> hits) : ILegalDocumentSearch
    {
        public Task<IReadOnlyList<LegalDocumentSearchHit>> SearchAsync(
            LegalCaseId caseId,
            string query,
            int limit,
            CancellationToken cancellationToken) => Task.FromResult(hits);
    }
}
