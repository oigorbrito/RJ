using System.Text;
using RJ.Application.Ingestion;

namespace RJ.Api;

public static class IngestionEndpoint
{
    private const string EvidenceConflictMessage = "Evidence conflicts with an existing legal document.";

    public static async Task<IResult> HandleAsync(
        IngestLegalDocumentRequest request,
        IngestLegalDocumentHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handler);

        if (request.RawContent is not null
            && Encoding.UTF8.GetByteCount(request.RawContent) > IngestionLimits.MaxRawContentBytes)
        {
            return Results.Json(
                new ApiError("payload_too_large", "Raw content exceeds the ingestion size limit."),
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        try
        {
            var rawContent = request.RawContent
                ?? throw new ArgumentException("Raw content is required.");

            var command = new IngestLegalDocumentCommand(
                request.CaseId,
                request.DocumentId,
                request.SourceName,
                rawContent);

            await handler.HandleAsync(command, cancellationToken);
            return Results.Accepted();
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new ApiError("invalid_request", "Invalid ingestion request."));
        }
        catch (LegalDocumentConflictException)
        {
            return Results.Conflict(new ApiError("evidence_conflict", EvidenceConflictMessage));
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
