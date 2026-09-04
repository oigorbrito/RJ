namespace RJ.Application.Ingestion;

public sealed record IngestLegalDocumentCommand(
    string CaseId,
    string DocumentId,
    string SourceName,
    string RawContent);
