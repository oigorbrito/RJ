namespace RJ.Infrastructure.Persistence;

public sealed class LegalDocumentPersistenceConflictException : InvalidOperationException
{
    public LegalDocumentPersistenceConflictException(string message)
        : base(message)
    {
    }
}
