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
    private readonly ProcessAttachmentContent[] contents = RequireContents(contents);

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

    private static ProcessAttachmentContent[] RequireContents(IReadOnlyList<ProcessAttachmentContent> value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var items = value.ToArray();
        if (items.Any(item => item is null))
        {
            throw new ArgumentException("Attachment content store cannot contain null items.", nameof(value));
        }

        return items;
    }
}
