using RJ.Domain.Cases;
using RJ.Domain.Documents;

namespace RJ.Application.Retrieval;

public sealed class LegalDocumentQueryService(
    ILegalDocumentReader reader,
    ILegalDocumentSearch search)
{
    public Task<LegalDocumentSnapshot?> GetAsync(
        string caseId,
        string documentId,
        CancellationToken cancellationToken)
    {
        return reader.GetAsync(
            new LegalCaseId(caseId),
            new LegalDocumentId(documentId),
            cancellationToken);
    }

    public Task<IReadOnlyList<LegalDocumentSnapshot>> ListAsync(
        string caseId,
        CancellationToken cancellationToken)
    {
        return reader.ListByCaseAsync(new LegalCaseId(caseId), cancellationToken);
    }

    public Task<IReadOnlyList<LegalDocumentSearchHit>> SearchAsync(
        string caseId,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        return search.SearchAsync(new LegalCaseId(caseId), query, limit, cancellationToken);
    }
}
