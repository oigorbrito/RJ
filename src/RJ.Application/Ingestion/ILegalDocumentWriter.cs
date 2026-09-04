using RJ.Domain.Documents;

namespace RJ.Application.Ingestion;

public interface ILegalDocumentWriter
{
    Task StoreAsync(LegalDocument document, CancellationToken cancellationToken);
}
