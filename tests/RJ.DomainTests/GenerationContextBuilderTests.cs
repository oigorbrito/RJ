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

    [Fact]
    public void Evidence_hit_rejects_invalid_citation_values()
    {
        var position = SourcePosition.Create(0, 5, 5);

        Assert.Throws<ArgumentException>(() => new LegalEvidenceHit(" ", "doc-1", "source.txt", Hash, "texto", position, 1f));
        Assert.Throws<ArgumentException>(() => new LegalEvidenceHit("case-1", " ", "source.txt", Hash, "texto", position, 1f));
        Assert.Throws<ArgumentException>(() => new LegalEvidenceHit("case-1", "doc-1", " ", Hash, "texto", position, 1f));
        Assert.Throws<ArgumentException>(() => new LegalEvidenceHit("case-1", "doc-1", "source.txt", "not-a-hash", "texto", position, 1f));
        Assert.Throws<ArgumentException>(() => new LegalEvidenceHit("case-1", "doc-1", "source.txt", Hash, " ", position, 1f));
        Assert.Throws<ArgumentNullException>(() => new LegalEvidenceHit("case-1", "doc-1", "source.txt", Hash, "texto", null!, 1f));
        Assert.Throws<ArgumentException>(() => new LegalEvidenceHit("case-1", "doc-1", "source.txt", Hash, "texto", position, float.NaN));
    }

    [Fact]
    public void Generation_context_item_rejects_invalid_citation_values()
    {
        var position = SourcePosition.Create(0, 5, 5);

        Assert.Throws<ArgumentException>(() => new GenerationContextItem(" ", "doc-1", "source.txt", Hash, "texto", position, 1f));
        Assert.Throws<ArgumentException>(() => new GenerationContextItem("case-1", " ", "source.txt", Hash, "texto", position, 1f));
        Assert.Throws<ArgumentException>(() => new GenerationContextItem("case-1", "doc-1", " ", Hash, "texto", position, 1f));
        Assert.Throws<ArgumentException>(() => new GenerationContextItem("case-1", "doc-1", "source.txt", "not-a-hash", "texto", position, 1f));
        Assert.Throws<ArgumentException>(() => new GenerationContextItem("case-1", "doc-1", "source.txt", Hash, " ", position, 1f));
        Assert.Throws<ArgumentNullException>(() => new GenerationContextItem("case-1", "doc-1", "source.txt", Hash, "texto", null!, 1f));
        Assert.Throws<ArgumentException>(() => new GenerationContextItem("case-1", "doc-1", "source.txt", Hash, "texto", position, float.PositiveInfinity));
    }

    [Fact]
    public void Generation_context_rejects_invalid_context_values()
    {
        var item = new GenerationContextItem(
            "case-1",
            "doc-1",
            "source.txt",
            Hash,
            "texto",
            SourcePosition.Create(0, 5, 5),
            1f);
        var otherCaseItem = new GenerationContextItem(
            "case-2",
            item.DocumentId,
            item.SourceName,
            item.ContentSha256,
            item.Excerpt,
            item.Position,
            item.Rank);

        Assert.Throws<ArgumentException>(() => new GenerationContext(" ", "query", 100, 0, []));
        Assert.Throws<ArgumentException>(() => new GenerationContext("case-1", " ", 100, 0, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GenerationContext("case-1", "query", 0, 0, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GenerationContext("case-1", "query", 100, -1, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GenerationContext("case-1", "query", 100, 101, []));
        Assert.Throws<ArgumentNullException>(() => new GenerationContext("case-1", "query", 100, 0, null!));
        Assert.Throws<ArgumentException>(() => new GenerationContext("case-1", "query", 100, 0, [null!]));
        Assert.Throws<ArgumentException>(() => new GenerationContext("case-1", "query", 100, 5, [otherCaseItem]));
        Assert.Equal("case-1", new GenerationContext(" case-1 ", " query ", 100, 5, [item]).CaseId);
    }

    [Fact]
    public void Generation_context_with_query_rebuilds_through_validated_constructor()
    {
        var context = new GenerationContext("case-1", "query", 100, 0, []);

        var updated = context.WithQuery("corrective query");

        Assert.NotSame(context, updated);
        Assert.Equal("corrective query", updated.Query);
        Assert.Equal(context.CaseId, updated.CaseId);
        Assert.Equal(context.CharacterBudget, updated.CharacterBudget);
        Assert.Equal(context.UsedCharacters, updated.UsedCharacters);
        Assert.Equal(context.Items, updated.Items);
        Assert.Throws<ArgumentException>(() => context.WithQuery(" "));
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
