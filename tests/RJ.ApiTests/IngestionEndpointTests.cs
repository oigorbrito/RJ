using Microsoft.AspNetCore.Http;
using RJ.Api;
using RJ.Application.Ingestion;
using RJ.Domain.Documents;

namespace RJ.ApiTests;

public sealed class IngestionEndpointTests
{
    [Fact]
    public async Task HandleAsync_returns_202_for_valid_ingestion()
    {
        var writer = new StubWriter();
        var handler = new IngestLegalDocumentHandler(writer);

        var result = await IngestionEndpoint.HandleAsync(
            new IngestLegalDocumentRequest("case-1", "doc-1", "source.txt", "conteudo"),
            handler,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status202Accepted, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.NotNull(writer.StoredDocument);
    }

    [Fact]
    public async Task HandleAsync_maps_validation_error_to_400()
    {
        var handler = new IngestLegalDocumentHandler(new StubWriter());

        var result = await IngestionEndpoint.HandleAsync(
            new IngestLegalDocumentRequest("", "doc-1", "source.txt", "conteudo"),
            handler,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ApiError>(((IValueHttpResult)result).Value);
        Assert.Equal("invalid_request", error.Code);
    }

    [Fact]
    public async Task HandleAsync_rejects_oversized_raw_content_before_writer_execution()
    {
        var writer = new StubWriter();
        var handler = new IngestLegalDocumentHandler(writer);
        var oversized = new string('x', IngestionLimits.MaxRawContentBytes + 1);

        var result = await IngestionEndpoint.HandleAsync(
            new IngestLegalDocumentRequest("case-1", "doc-large", "source.txt", oversized),
            handler,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ApiError>(((IValueHttpResult)result).Value);
        Assert.Equal("payload_too_large", error.Code);
        Assert.Null(writer.StoredDocument);
    }

    [Fact]
    public async Task HandleAsync_maps_evidence_conflict_to_409()
    {
        var handler = new IngestLegalDocumentHandler(new StubWriter(
            new LegalDocumentConflictException("conflict")));

        var result = await IngestionEndpoint.HandleAsync(
            new IngestLegalDocumentRequest("case-1", "doc-1", "source.txt", "conteudo"),
            handler,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ApiError>(((IValueHttpResult)result).Value);
        Assert.Equal("evidence_conflict", error.Code);
        Assert.Equal("conflict", error.Error);
    }

    [Fact]
    public async Task HandleAsync_does_not_convert_cancellation_into_client_error()
    {
        var handler = new IngestLegalDocumentHandler(new StubWriter(
            new OperationCanceledException("cancelled")));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            IngestionEndpoint.HandleAsync(
                new IngestLegalDocumentRequest("case-1", "doc-1", "source.txt", "conteudo"),
                handler,
                CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_does_not_convert_timeout_into_client_error()
    {
        var handler = new IngestLegalDocumentHandler(new StubWriter(
            new TimeoutException("database timeout")));

        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            IngestionEndpoint.HandleAsync(
                new IngestLegalDocumentRequest("case-1", "doc-1", "source.txt", "conteudo"),
                handler,
                CancellationToken.None));

        Assert.Equal("database timeout", exception.Message);
    }

    [Fact]
    public async Task HandleAsync_does_not_convert_unexpected_failure_into_client_error()
    {
        var handler = new IngestLegalDocumentHandler(new StubWriter(
            new InvalidOperationException("unexpected")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            IngestionEndpoint.HandleAsync(
                new IngestLegalDocumentRequest("case-1", "doc-1", "source.txt", "conteudo"),
                handler,
                CancellationToken.None));

        Assert.Equal("unexpected", exception.Message);
    }

    private sealed class StubWriter(Exception? exception = null) : ILegalDocumentWriter
    {
        public LegalDocument? StoredDocument { get; private set; }

        public Task StoreAsync(LegalDocument document, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StoredDocument = document;
            if (exception is not null)
            {
                return Task.FromException(exception);
            }

            return Task.CompletedTask;
        }
    }
}
