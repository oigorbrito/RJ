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
    DateTimeOffset ObservedAt)
{
    public string CaseId { get; } = Require(CaseId, nameof(CaseId));

    public string AttachmentId { get; } = Require(AttachmentId, nameof(AttachmentId));

    public int ChunkIndex { get; } = RequireNonNegative(ChunkIndex, nameof(ChunkIndex));

    public int TokenStart { get; } = RequireNonNegative(TokenStart, nameof(TokenStart));

    public int TokenCount { get; } = RequirePositive(TokenCount, nameof(TokenCount));

    public string Text { get; } = Require(Text, nameof(Text));

    public string SourceName { get; } = Require(SourceName, nameof(SourceName));

    public string SourceReference { get; } = Require(SourceReference, nameof(SourceReference));

    public string ContentSha256 { get; } = RequireSha256(ContentSha256, nameof(ContentSha256));

    public DateTimeOffset ObservedAt { get; } = RequireObservedAt(ObservedAt, nameof(ObservedAt));

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Attachment chunk value cannot be empty.", parameterName);
        }

        return value.Trim();
    }

    private static int RequireNonNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentException("Attachment chunk numeric value cannot be negative.", parameterName);
        }

        return value;
    }

    private static int RequirePositive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentException("Attachment chunk token count must be positive.", parameterName);
        }

        return value;
    }

    private static string RequireSha256(string value, string parameterName)
    {
        var normalized = Require(value, parameterName).ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Attachment chunk content hash must be a SHA-256 hex digest.", parameterName);
        }

        return normalized;
    }

    private static DateTimeOffset RequireObservedAt(DateTimeOffset value, string parameterName)
    {
        if (value == default)
        {
            throw new ArgumentException("Attachment chunk observed instant cannot be empty.", parameterName);
        }

        return value;
    }
}

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
