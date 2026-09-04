using RJ.Domain.Cases;

namespace RJ.Application.Retrieval;

public interface ILegalDocumentSearch
{
    Task<IReadOnlyList<LegalDocumentSearchHit>> SearchAsync(
        LegalCaseId caseId,
        string query,
        int limit,
        CancellationToken cancellationToken);
}
