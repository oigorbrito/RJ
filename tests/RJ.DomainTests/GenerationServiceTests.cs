using RJ.Application.Generation;
using RJ.Application.Retrieval;

namespace RJ.DomainTests;

public sealed class GenerationServiceTests
{
    [Fact]
    public async Task GenerateAsync_passes_only_generation_context_and_accepts_valid_cited_claim()
    {
        var context = CreateContext();
        var item = Assert.Single(context.Items);
        var model = new DeterministicGenerationModel(new GenerationModelOutput(
            false,
            null,
            [new GenerationClaim(
                "A tutela provisoria foi deferida.",
                [new GenerationCitation(item.DocumentId, item.ContentSha256, item.Position.StartOffset, item.Position.Length)])]));
        var service = new GenerationService(model);

        var result = await service.GenerateAsync(context, CancellationToken.None);

        Assert.Same(context, model.ReceivedContext);
        Assert.False(result.Abstained);
        Assert.Single(result.Claims);
    }

    [Fact]
    public async Task GenerateAsync_accepts_explicit_abstention_without_claims()
    {
        var context = CreateContext();
        var model = new DeterministicGenerationModel(new GenerationModelOutput(true, "insufficient evidence", []));
        var service = new GenerationService(model);

        var result = await service.GenerateAsync(context, CancellationToken.None);

        Assert.True(result.Abstained);
        Assert.Equal("insufficient evidence", result.AbstentionReason);
        Assert.Empty(result.Claims);
    }

    [Fact]
    public async Task GenerateAsync_rejects_uncited_claim()
    {
        var context = CreateContext();
        var model = new DeterministicGenerationModel(new GenerationModelOutput(
            false,
            null,
            [new GenerationClaim("Afirmação sem fonte.", [])]));
        var service = new GenerationService(model);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_rejects_citation_outside_context()
    {
        var context = CreateContext();
        var model = new DeterministicGenerationModel(new GenerationModelOutput(
            false,
            null,
            [new GenerationClaim(
                "Afirmação com referência inventada.",
                [new GenerationCitation("doc-x", new string('f', 64), 0, 10)])]));
        var service = new GenerationService(model);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_rejects_ambiguous_abstention_state()
    {
        var context = CreateContext();
        var item = Assert.Single(context.Items);
        var model = new DeterministicGenerationModel(new GenerationModelOutput(
            true,
            "insufficient evidence",
            [new GenerationClaim(
                "claim",
                [new GenerationCitation(item.DocumentId, item.ContentSha256, item.Position.StartOffset, item.Position.Length)])]));
        var service = new GenerationService(model);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_does_not_call_model_without_context_evidence()
    {
        var context = new GenerationContext("case-1", "tutela", 100, 0, []);
        var model = new DeterministicGenerationModel(new GenerationModelOutput(true, "insufficient evidence", []));
        var service = new GenerationService(model);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateAsync(context, CancellationToken.None));

        Assert.Null(model.ReceivedContext);
    }

    private static GenerationContext CreateContext()
    {
        const string excerpt = "A tutela provisoria foi deferida.";
        var position = SourcePosition.Create(0, excerpt.Length, excerpt.Length);
        var item = new GenerationContextItem(
            "case-1",
            "doc-1",
            "decisao.pdf",
            new string('a', 64),
            excerpt,
            position,
            1.0f);

        return new GenerationContext("case-1", "tutela provisoria", 1000, excerpt.Length, [item]);
    }

    private sealed class DeterministicGenerationModel(GenerationModelOutput output) : IGenerationModel
    {
        public GenerationContext? ReceivedContext { get; private set; }

        public Task<GenerationModelOutput> GenerateAsync(
            GenerationContext context,
            CancellationToken cancellationToken)
        {
            ReceivedContext = context;
            return Task.FromResult(output);
        }
    }
}
