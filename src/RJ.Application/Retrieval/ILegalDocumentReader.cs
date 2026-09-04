using RJ.Domain.Cases;
using RJ.Domain.Documents;

namespace RJ.Application.Retrieval;

public interface ILegalDocumentReader
{
    Task<LegalDocumentSnapshot?> GetAsync(
        LegalCaseId caseId,
        LegalDocumentId documentId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<LegalDocumentSnapshot>> ListByCaseAsync(
        LegalCaseId caseId,
        int offset,
        int limit,
        CancellationToken cancellationToken);
}
