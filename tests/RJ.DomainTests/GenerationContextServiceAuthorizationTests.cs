using RJ.Application.Generation;
using RJ.Application.Retrieval;
using RJ.Domain.Cases;
using RJ.Domain.Documents;

namespace RJ.DomainTests;

public sealed class GenerationContextServiceAuthorizationTests
{
    [Fact]
    public async Task Build_authorized_filters_evidence_sources_before_context_construction()
    {
        var judit = Document("doc-judit", "Judit", "Prazo observado na fonte Judit.", 'a');
        var dataJud = Document("doc-datajud", "DataJud", "Prazo observado na fonte DataJud.", 'b');
        var retrieval = new LegalDocumentQueryService(
            new EmptyReader(),
            new FixedSearch([new LegalDocumentSearchHit(judit, 2f), new LegalDocumentSearchHit(dataJud, 1f)]));
        var service = new GenerationContextService(retrieval, new GenerationContextBuilder());

        var context = await service.BuildAuthorizedAsync(
            "case-1",
            "Prazo",
            20,
            12000,
            ["Judit"],
            CancellationToken.None);

        var item = Assert.Single(context.Items);
        Assert.Equal("Judit", item.SourceName);
        Assert.DoesNotContain(context.Items, evidence => evidence.SourceName == "DataJud");
    }

    [Fact]
    public async Task Build_authorized_with_no_allowed_sources_produces_no_context_items()
    {
        var judit = Document("doc-judit", "Judit", "Prazo observado na fonte Judit.", 'a');
        var retrieval = new LegalDocumentQueryService(
            new EmptyReader(),
            new FixedSearch([new LegalDocumentSearchHit(judit, 1f)]));
        var service = new GenerationContextService(retrieval, new GenerationContextBuilder());

        var context = await service.BuildAuthorizedAsync(
            "case-1",
            "Prazo",
            20,
            12000,
            [],
            CancellationToken.None);

        Assert.Empty(context.Items);
    }

    private static LegalDocumentSnapshot Document(
        string documentId,
        string sourceName,
        string rawContent,
        char hashCharacter) =>
        new(
            "case-1",
            documentId,
            sourceName,
            rawContent,
            rawContent,
            new string(hashCharacter, 64));

    private sealed class EmptyReader : ILegalDocumentReader
    {
        public Task<LegalDocumentSnapshot?> GetAsync(
            LegalCaseId caseId,
            LegalDocumentId documentId,
            CancellationToken cancellationToken) => Task.FromResult<LegalDocumentSnapshot?>(null);

        public Task<IReadOnlyList<LegalDocumentSnapshot>> ListByCaseAsync(
            LegalCaseId caseId,
            int offset,
            int limit,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<LegalDocumentSnapshot>>([]);
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
