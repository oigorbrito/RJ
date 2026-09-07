using System.Runtime.CompilerServices;
using System.Text;

namespace RJ.DomainTests;

internal static class LegacyBenchmarkCorpusFixtureInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(AppContext.BaseDirectory, @"C:\Projetos\RJ", "oab-bench");
        var referenceAnswer = Path.Combine(root, "data", "oab_bench", "reference_answer");
        Directory.CreateDirectory(referenceAnswer);
        File.WriteAllText(
            Path.Combine(root, "data", "oab_bench", "question.jsonl"),
            "{\"question_id\":\"q-1\",\"category\":\"cat-1\",\"statement\":\"QUESTÃO\\n1. Qual é a resposta?\"}" + Environment.NewLine,
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(referenceAnswer, "guidelines.jsonl"),
            "{\"question_id\":\"q-1\",\"choices\":[{\"turns\":[\"Resposta deliberadamente divergente para preservar o gate-failure esperado.\"]}]}" + Environment.NewLine,
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(root, "data", "judge_prompts.jsonl"),
            "{\"name\":\"single-v1\",\"type\":\"single\",\"system_prompt\":\"x\",\"prompt_template\":\"y\",\"description\":\"z\",\"category\":\"general\",\"output_format\":\"[[rating]]\"}" + Environment.NewLine,
            Encoding.UTF8);
    }
}
