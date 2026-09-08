using System.Text.RegularExpressions;

namespace RJ.Application.Sources;

public sealed record ProcessAttachmentChunk(
    string CaseId,
    string AttachmentId,
    int ChunkIndex,
    int TokenStart,
    int TokenCount,
    string Text,
    string SourceName,
    string SourceReference,
    string ContentSha256,
    DateTimeOffset ObservedAt);

public static class ProcessAttachmentChunker
{
    public const int MinimumTokenCount = 800;
    public const int MaximumTokenCount = 1200;
    public const int TargetTokenCount = 1000;
    public const int OverlapTokenCount = 150;

    public static IReadOnlyList<ProcessAttachmentChunk> Chunk(ProcessAttachmentContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var tokens = Regex.Matches(content.ExtractedText, @"\S+", RegexOptions.CultureInvariant);
        if (tokens.Count == 0)
        {
            throw new InvalidOperationException("Observed attachment content must contain at least one token.");
        }

        var chunks = new List<ProcessAttachmentChunk>();
        var start = 0;
        var index = 0;

        while (start < tokens.Count)
        {
            var endExclusive = Math.Min(start + TargetTokenCount, tokens.Count);
            if (start == 0 && tokens.Count <= MaximumTokenCount)
            {
                endExclusive = tokens.Count;
            }

            var first = tokens[start];
            var last = tokens[endExclusive - 1];
            var textStart = first.Index;
            var textLength = (last.Index + last.Length) - textStart;
            var text = content.ExtractedText.Substring(textStart, textLength);

            chunks.Add(new ProcessAttachmentChunk(
                content.CaseId,
                content.AttachmentId,
                index,
                start,
                endExclusive - start,
                text,
                content.SourceName,
                content.SourceReference,
                content.ContentSha256,
                content.ObservedAt));

            if (endExclusive == tokens.Count)
            {
                break;
            }

            start = endExclusive - OverlapTokenCount;
            index++;
        }

        return chunks;
    }
}
