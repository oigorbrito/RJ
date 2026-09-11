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
        catch (ArgumentException)
        {
            return InvalidRequest();
        }
    }

    public static async Task<IResult> GetDocumentAsync(
        string caseId,
        string documentId,
        LegalDocumentQueryService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var document = await service.GetAsync(caseId, documentId, cancellationToken);
            return document is null ? Results.NotFound() : Results.Ok(ToDetail(document));
        }
        catch (ArgumentException)
        {
            return InvalidRequest();
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
        catch (ArgumentException)
        {
            return InvalidRequest();
        }
    }

    public static async Task<IResult> RetrieveEvidenceAsync(
        string caseId,
        string? q,
        int? limit,
        LegalDocumentQueryService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var evidence = await service.RetrieveEvidenceAsync(
                caseId,
                q ?? string.Empty,
                limit ?? 20,
                cancellationToken);
            return Results.Ok(evidence.Select(ToEvidenceResult).ToArray());
        }
        catch (ArgumentException)
        {
            return InvalidRequest();
        }
    }

    private static IResult InvalidRequest() =>
        Results.BadRequest(new ApiReadError("invalid_request", "Invalid read request."));

    private static LegalDocumentSummary ToSummary(LegalDocumentSnapshot document) => new(
        document.CaseId,
        document.DocumentId,
        document.SourceName,
        document.ContentSha256);

    private static LegalDocumentDetail ToDetail(LegalDocumentSnapshot document) => new(
        document.CaseId,
        document.DocumentId,
        document.SourceName,
        document.RawContent,
        document.Content,
        document.ContentSha256);

    private static LegalEvidenceResult ToEvidenceResult(LegalEvidenceHit evidence) => new(
        evidence.CaseId,
        evidence.DocumentId,
        evidence.SourceName,
        evidence.ContentSha256,
        evidence.Excerpt,
        new EvidencePosition(evidence.Position.StartOffset, evidence.Position.Length),
        evidence.Rank);
}

public sealed record LegalDocumentSummary(
    string CaseId,
    string DocumentId,
    string SourceName,
    string ContentSha256);

public sealed record LegalDocumentDetail(
    string CaseId,
    string DocumentId,
    string SourceName,
    string RawContent,
    string Content,
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

public sealed record LegalEvidenceResult(
    string CaseId,
    string DocumentId,
    string SourceName,
    string ContentSha256,
    string Excerpt,
    EvidencePosition Position,
    float Rank);

public sealed record EvidencePosition(
    int StartOffset,
    int Length);

public sealed record ApiReadError(string Code, string Error);
