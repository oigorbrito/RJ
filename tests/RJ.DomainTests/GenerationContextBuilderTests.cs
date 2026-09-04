using RJ.Application.Generation;
using RJ.Application.Retrieval;

namespace RJ.DomainTests;

public sealed class GenerationContextBuilderTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void Build_orders_deduplicates_and_accounts_budget_deterministically()
    {
        var builder = new GenerationContextBuilder();
        var high = Evidence("case-1", "doc-b", "BBBB", 8, 4, 2f);
        var low = Evidence("case-1", "doc-a", "AAAA", 2, 4, 1f);

        var context = builder.Build(" case-1 ", " tutela ", 8, [low, high, high]);

        Assert.Equal("case-1", context.CaseId);
        Assert.Equal("tutela", context.Query);
        Assert.Equal(8, context.UsedCharacters);
        Assert.Equal(["doc-b", "doc-a"], context.Items.Select(item => item.DocumentId).ToArray());
    }

    [Fact]
    public void Build_rejects_cross_case_evidence()
    {
        var builder = new GenerationContextBuilder();
        var evidence = Evidence("case-2", "doc-1", "texto", 0, 5, 1f);

        Assert.Throws<InvalidOperationException>(() => builder.Build("case-1", "texto", 100, [evidence]));
    }

    [Fact]
    public void Build_skips_whole_evidence_item_when_it_does_not_fit_budget()
    {
        var builder = new GenerationContextBuilder();
        var first = Evidence("case-1", "doc-1", "12345", 0, 5, 2f);
        var second = Evidence("case-1", "doc-2", "abcdef", 0, 6, 1f);

        var context = builder.Build("case-1", "query", 7, [first, second]);

        var item = Assert.Single(context.Items);
        Assert.Equal("doc-1", item.DocumentId);
        Assert.Equal(5, context.UsedCharacters);
    }

    [Fact]
    public void Build_rejects_excerpt_that_does_not_match_position_length()
    {
        var builder = new GenerationContextBuilder();
        var invalid = new LegalEvidenceHit(
            "case-1",
            "doc-1",
            "source.txt",
            Hash,
            "abc",
            SourcePosition.Create(0, 2, 3),
            1f);

        Assert.Throws<InvalidOperationException>(() => builder.Build("case-1", "abc", 100, [invalid]));
    }

    private static LegalEvidenceHit Evidence(
        string caseId,
        string documentId,
        string excerpt,
        int start,
        int length,
        float rank) => new(
            caseId,
            documentId,
            $"{documentId}.txt",
            Hash,
            excerpt,
            SourcePosition.Create(start, length, start + length),
            rank);
}
