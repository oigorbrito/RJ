using RJ.Application.Benchmarking;
using RJ.Application.Evaluation;
using RJ.Application.Generation;
using RJ.Application.Retrieval;

namespace RJ.BenchmarkCli;

public static class ApprovedGenerationBenchmarkCatalog
{
    public const string Version = "generation-benchmark-v1";

    public static GenerationBenchmarkCatalog Create()
    {
        return new GenerationBenchmarkCatalog(
            Version,
            [
                CreateClaimCase(
                    "case-claim-1",
                    "A tutela provisoria foi deferida.",
                    "A tutela provisoria foi deferida."),
                CreateAbstentionCase(
                    "case-abstain-1",
                    "Não há evidência suficiente para responder.")
            ]);
    }

    private static GenerationEvaluationCase CreateClaimCase(
        string id,
        string query,
        string excerpt)
    {
        var hash = new string('a', 64);
        var position = SourcePosition.Create(0, excerpt.Length, excerpt.Length);
        var citation = new GenerationCitation("doc-1", hash, 0, excerpt.Length);
        var context = new GenerationContext(
            id,
            query,
            1000,
            excerpt.Length,
            [new GenerationContextItem(id, "doc-1", "fixture.txt", hash, excerpt, position, 1.0f)]);

        return new GenerationEvaluationCase(
            id,
            context,
            [new ExpectedGenerationClaim(query, [citation])],
            false);
    }

    private static GenerationEvaluationCase CreateAbstentionCase(
        string id,
        string query)
    {
        const string excerpt = "Conteúdo sem resposta suficiente para a pergunta.";
        var hash = new string('b', 64);
        var position = SourcePosition.Create(0, excerpt.Length, excerpt.Length);
        var context = new GenerationContext(
            id,
            query,
            1000,
            excerpt.Length,
            [new GenerationContextItem(id, "doc-1", "fixture.txt", hash, excerpt, position, 1.0f)]);

        return new GenerationEvaluationCase(id, context, [], true);
    }
}
