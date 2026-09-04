using RJ.Domain.Documents;

namespace RJ.Application.Ingestion;

public sealed class IngestLegalDocumentHandler(ILegalDocumentWriter writer)
{
    public Task HandleAsync(IngestLegalDocumentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var document = new LegalDocument(
            command.DocumentId,
            command.CaseId,
            command.SourceName,
            command.Content,
            command.ContentSha256);

        return writer.StoreAsync(document, cancellationToken);
    }
}
