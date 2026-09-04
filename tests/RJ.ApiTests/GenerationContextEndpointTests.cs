using Microsoft.AspNetCore.Http;
using RJ.Api;
using RJ.Application.Generation;
using RJ.Application.Retrieval;
using RJ.Domain.Cases;
using RJ.Domain.Documents;

namespace RJ.ApiTests;

public sealed class GenerationContextEndpointTests
{
    [Fact]
    public async Task HandleAsync_returns_explicit_generation_context_projection()
    {
        const string content = "tutela urgente requerida";
        var snapshot = new LegalDocumentSnapshot(
            "case-1",
            "doc-1",
            "source.txt",
            content,
            content,
            new string('a', 64));
        var retrieval = new LegalDocumentQueryService(
            new StubReader(),
            new StubSearch([new LegalDocumentSearchHit(snapshot, 0.75f)]));
        var service = new GenerationContextService(retrieval, new GenerationContextBuilder());

        var result = await GenerationContextEndpoint.HandleAsync(
            "case-1",
            "tutela",
            20,
            12000,
            service,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        var response = Assert.IsType<GenerationContextResponse>(((IValueHttpResult)result).Value);
        Assert.Equal("case-1", response.CaseId);
        Assert.Equal("tutela", response.Query);
        Assert.Equal(12000, response.CharacterBudget);
        var item = Assert.Single(response.Items);
        Assert.Equal("case-1", item.CaseId);
        Assert.Equal("doc-1", item.DocumentId);
        Assert.Equal("source.txt", item.SourceName);
        Assert.Equal(snapshot.ContentSha256, item.ContentSha256);
        Assert.Equal(content, item.Excerpt);
        Assert.Equal(0, item.Position.StartOffset);
        Assert.Equal(content.Length, item.Position.Length);
        Assert.Equal(item.Excerpt.Length, response.UsedCharacters);
        Assert.Equal(0.75f, item.Rank);
    }

    [Fact]
    public async Task HandleAsync_returns_sanitized_422_for_internal_evidence_invariant_failure()
    {
        var snapshot = new LegalDocumentSnapshot(
            "different-case",
            "doc-secret-456",
            "source.txt",
            "sensitive legal content",
            "sensitive legal content",
            new string('a', 64));
        var retrieval = new LegalDocumentQueryService(
            new StubReader(),
            new StubSearch([new LegalDocumentSearchHit(snapshot, 1.0f)]));
        var service = new GenerationContextService(retrieval, new GenerationContextBuilder());

        var result = await GenerationContextEndpoint.HandleAsync(
            "case-legal-123",
            "sensitive legal content",
            20,
            12000,
            service,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<GenerationContextError>(((IValueHttpResult)result).Value);
        Assert.Equal("Generation context could not be constructed from the retrieved evidence.", error.Error);
        Assert.DoesNotContain("different-case", error.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("doc-secret-456", error.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive legal content", error.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("belong exclusively", error.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleAsync_keeps_validation_failure_as_400()
    {
        var retrieval = new LegalDocumentQueryService(new StubReader(), new StubSearch([]));
        var service = new GenerationContextService(retrieval, new GenerationContextBuilder());

        var result = await GenerationContextEndpoint.HandleAsync(
            "case-1",
            "",
            20,
            12000,
            service,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<GenerationContextError>(((IValueHttpResult)result).Value);
        Assert.Contains("Generation query cannot be empty.", error.Error, StringComparison.Ordinal);
    }

    private sealed class StubReader : ILegalDocumentReader
    {
        public Task<LegalDocumentSnapshot?> GetAsync(
            LegalCaseId caseId,
            LegalDocumentId documentId,
            CancellationToken cancellationToken) =>
            Task.FromResult<LegalDocumentSnapshot?>(null);

        public Task<IReadOnlyList<LegalDocumentSnapshot>> ListByCaseAsync(
            LegalCaseId caseId,
            int offset,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LegalDocumentSnapshot>>([]);
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
