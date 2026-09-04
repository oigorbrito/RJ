using Microsoft.AspNetCore.Http;
using RJ.Application.Retrieval;

namespace RJ.Api;

public static class ReadEndpoint
{
    public static async Task<IResult> ListDocumentsAsync(
        string caseId,
        int? page,
        int? pageSize,
        LegalDocumentQueryService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var pageNumber = page ?? 1;
            var size = pageSize ?? 20;
            var documents = await service.ListAsync(caseId, pageNumber, size, cancellationToken);
            var items = documents.Select(ToSummary).ToArray();
            return Results.Ok(new LegalDocumentPage(pageNumber, size, items));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new ApiReadError("invalid_request", exception.Message));
        }
    }

    public static async Task<IResult> SearchAsync(
        string caseId,
        string? q,
        int? limit,
        LegalDocumentQueryService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var hits = await service.SearchAsync(caseId, q ?? string.Empty, limit ?? 20, cancellationToken);
            var items = hits.Select(hit => new LegalDocumentSearchResult(
                hit.Document.CaseId,
                hit.Document.DocumentId,
                hit.Document.SourceName,
                hit.Document.ContentSha256,
                hit.Rank)).ToArray();
            return Results.Ok(items);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new ApiReadError("invalid_request", exception.Message));
        }
    }

    private static LegalDocumentSummary ToSummary(LegalDocumentSnapshot document) => new(
        document.CaseId,
        document.DocumentId,
        document.SourceName,
        document.ContentSha256);
}

public sealed record LegalDocumentSummary(
    string CaseId,
    string DocumentId,
    string SourceName,
    string ContentSha256);

public sealed record LegalDocumentPage(
    int Page,
    int PageSize,
    IReadOnlyList<LegalDocumentSummary> Items);

public sealed record LegalDocumentSearchResult(
    string CaseId,
    string DocumentId,
    string SourceName,
    string ContentSha256,
    float Rank);

public sealed record ApiReadError(string Code, string Error);
