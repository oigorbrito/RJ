namespace RJ.Application.Sources;

public interface IProcessAttachmentContentStore
{
    IReadOnlyList<ProcessAttachmentContent> ListByCase(string caseId);
}

public sealed class EmptyProcessAttachmentContentStore : IProcessAttachmentContentStore
{
    public IReadOnlyList<ProcessAttachmentContent> ListByCase(string caseId)
    {
        if (string.IsNullOrWhiteSpace(caseId))
        {
            throw new ArgumentException("Case identifier cannot be empty.", nameof(caseId));
        }

        return [];
    }
}

public sealed class InMemoryProcessAttachmentContentStore(IReadOnlyList<ProcessAttachmentContent> contents)
    : IProcessAttachmentContentStore
{
    private readonly ProcessAttachmentContent[] contents = (contents ?? throw new ArgumentNullException(nameof(contents))).ToArray();

    public IReadOnlyList<ProcessAttachmentContent> ListByCase(string caseId)
    {
        if (string.IsNullOrWhiteSpace(caseId))
        {
            throw new ArgumentException("Case identifier cannot be empty.", nameof(caseId));
        }

        return contents
            .Where(item => StringComparer.Ordinal.Equals(item.CaseId, caseId.Trim()))
            .OrderBy(item => item.AttachmentId, StringComparer.Ordinal)
            .ThenBy(item => item.SourceReference, StringComparer.Ordinal)
            .ToArray();
    }
}
