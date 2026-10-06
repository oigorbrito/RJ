using System.Globalization;
using RJ.Application.Sources;

namespace RJ.DomainTests;

public sealed class ProcessAttachmentChunkerTests
{
    private static readonly DateTimeOffset ObservedAt =
        DateTimeOffset.Parse("2026-09-08T12:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Chunk_preserves_short_observed_content_as_single_chunk()
    {
        const string text = "Primeira linha.\nSegunda linha com pontuacao.";
        var content = Content(text);

        var chunk = Assert.Single(ProcessAttachmentChunker.Chunk(content));

        Assert.Equal(0, chunk.ChunkIndex);
        Assert.Equal(0, chunk.TokenStart);
        Assert.Equal(6, chunk.TokenCount);
        Assert.Equal(text, chunk.Text);
        Assert.Equal(content.ContentSha256, chunk.ContentSha256);
    }

    [Fact]
    public void Chunk_uses_exact_150_token_overlap_and_keeps_full_partitions_within_800_1200_when_feasible()
    {
        var content = Content(TokenText(2200));

        var chunks = ProcessAttachmentChunker.Chunk(content);

        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, chunk => Assert.InRange(chunk.TokenCount, 800, 1200));
        Assert.Equal(
            ProcessAttachmentChunker.OverlapTokenCount,
            (chunks[0].TokenStart + chunks[0].TokenCount) - chunks[1].TokenStart);
        Assert.Equal(2200, chunks[1].TokenStart + chunks[1].TokenCount);
    }

    [Fact]
    public void Chunk_allows_only_boundary_remainder_when_minimum_and_exact_overlap_are_mathematically_incompatible()
    {
        var content = Content(TokenText(1201));

        var chunks = ProcessAttachmentChunker.Chunk(content);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(ProcessAttachmentChunker.TargetTokenCount, chunks[0].TokenCount);
        Assert.True(chunks[1].TokenCount < ProcessAttachmentChunker.MinimumTokenCount);
        Assert.Equal(
            ProcessAttachmentChunker.OverlapTokenCount,
            (chunks[0].TokenStart + chunks[0].TokenCount) - chunks[1].TokenStart);
        Assert.Equal(1201, chunks[1].TokenStart + chunks[1].TokenCount);
    }

    [Fact]
    public void Chunk_is_deterministic_for_same_observed_content()
    {
        var content = Content(TokenText(3000));

        var first = ProcessAttachmentChunker.Chunk(content);
        var second = ProcessAttachmentChunker.Chunk(content);

        Assert.Equal(first, second);
    }

    private static ProcessAttachmentContent Content(string text) =>
        new(
            "case-001",
            "attachment-001",
            "fixture-extractor",
            "attachments/attachment-001.txt",
            text,
            ObservedAt);

    private static string TokenText(int count) =>
        string.Join(' ', Enumerable.Range(0, count).Select(index => $"token-{index}"));
}
