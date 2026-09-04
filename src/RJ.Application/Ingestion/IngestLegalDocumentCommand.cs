using RJ.Domain.Cases;
using RJ.Domain.Documents;

namespace RJ.Application.Ingestion;

public sealed record IngestLegalDocumentCommand(
    LegalCaseId CaseId,
    LegalDocumentId DocumentId,
    string SourceName,
    string Content,
    string ContentSha256);
