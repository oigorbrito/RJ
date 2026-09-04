namespace RJ.Application.Retrieval;

public sealed record LegalDocumentSnapshot(
    string CaseId,
    string DocumentId,
    string SourceName,
    string Content,
    string ContentSha256);
