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

        var tokenCounts = PlanTokenCounts(tokens.Count);
        var chunks = new List<ProcessAttachmentChunk>(tokenCounts.Count);
        var start = 0;

        for (var index = 0; index < tokenCounts.Count; index++)
        {
            var tokenCount = tokenCounts[index];
            var endExclusive = start + tokenCount;
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
                tokenCount,
                text,
                content.SourceName,
                content.SourceReference,
                content.ContentSha256,
                content.ObservedAt));

            start = endExclusive - OverlapTokenCount;
        }

        return chunks;
    }

    private static IReadOnlyList<int> PlanTokenCounts(int totalTokens)
    {
        if (totalTokens <= MaximumTokenCount)
        {
            return [totalTokens];
        }

        for (var chunkCount = 2; chunkCount <= totalTokens; chunkCount++)
        {
            var totalMemberships = totalTokens + (OverlapTokenCount * (chunkCount - 1));
            if (totalMemberships < MinimumTokenCount * chunkCount)
            {
                continue;
            }

            if (totalMemberships > MaximumTokenCount * chunkCount)
            {
                continue;
            }

            var baseSize = totalMemberships / chunkCount;
            var remainder = totalMemberships % chunkCount;
            var planned = new int[chunkCount];
            for (var index = 0; index < chunkCount; index++)
            {
                planned[index] = baseSize + (index < remainder ? 1 : 0);
            }

            return planned;
        }

        var fallback = new List<int>();
        var start = 0;
        while (start < totalTokens)
        {
            var count = Math.Min(TargetTokenCount, totalTokens - start);
            fallback.Add(count);
            if (start + count >= totalTokens)
            {
                break;
            }

            start += count - OverlapTokenCount;
        }

        return fallback;
    }
}
