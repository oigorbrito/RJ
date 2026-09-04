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
    public async Task HandleAsync_keeps_static_validation_message_for_invalid_request()
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
        Assert.Equal("Generation query cannot be empty. (Parameter 'query')", error.Error);
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
