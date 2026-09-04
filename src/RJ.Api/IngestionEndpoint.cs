using RJ.Application.Ingestion;

namespace RJ.Api;

public static class IngestionEndpoint
{
    public static async Task<IResult> HandleAsync(
        IngestLegalDocumentRequest request,
        IngestLegalDocumentHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handler);

        try
        {
            var command = new IngestLegalDocumentCommand(
                request.CaseId,
                request.DocumentId,
                request.SourceName,
                request.RawContent);

            await handler.HandleAsync(command, cancellationToken);
            return Results.Accepted();
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new ApiError("invalid_request", exception.Message));
        }
        catch (LegalDocumentConflictException exception)
        {
            return Results.Conflict(new ApiError("evidence_conflict", exception.Message));
        }
    }
}

public sealed record IngestLegalDocumentRequest(
    string CaseId,
    string DocumentId,
    string SourceName,
    string RawContent);

public sealed record ApiError(
    string Code,
    string Error);
