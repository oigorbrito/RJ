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

public interface IIdentifiedLegalDocumentSearch : ILegalDocumentSearch
{
    string ImplementationId { get; }
}
