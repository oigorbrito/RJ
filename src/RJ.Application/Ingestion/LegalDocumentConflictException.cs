namespace RJ.Application.Ingestion;

public sealed class LegalDocumentConflictException : InvalidOperationException
{
    public LegalDocumentConflictException(string message)
        : base(message)
    {
    }
}
