using System.Text;
using System.Text.Json;

namespace RJ.DomainTests;

// Deliberately synthetic: validates adapter/CLI contracts, never real-corpus quality.
internal sealed class LegalCorpusTestFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rj-legal-fixture-{Guid.NewGuid():N}");

    public string OabRoot => Path.Combine(_root, "oab-bench");
    public string RulingBrRoot => Path.Combine(_root, "rulingbr");

    public LegalCorpusTestFixture()
    {
        Directory.CreateDirectory(Path.Combine(OabRoot, "data", "oab_bench", "reference_answer"));
        File.WriteAllText(Path.Combine(OabRoot, "data", "oab_bench", "question.jsonl"),
            """{"question_id":"q-1","category":"civil","statement":"QUESTÃO\n1. Qual é a resposta?"}""", Encoding.UTF8);
        File.WriteAllText(Path.Combine(OabRoot, "data", "oab_bench", "reference_answer", "guidelines.jsonl"),
            """{"question_id":"q-1","choices":[{"turns":["Resposta esperada distinta da pergunta."]}]}""", Encoding.UTF8);
        File.WriteAllText(Path.Combine(OabRoot, "data", "judge_prompts.jsonl"),
            """{"name":"single-v1","type":"single","system_prompt":"x","prompt_template":"y","description":"z","category":"general","output_format":"[[rating]]"}""", Encoding.UTF8);
        Directory.CreateDirectory(RulingBrRoot);
        var rows = new[]
        {
            new { ementa = "Primeira fonte sintética.", acordao = "Primeira decisão sintética.", area = "civil", relator = "teste" },
            new { ementa = "Segunda fonte sintética.", acordao = "Segunda decisão sintética.", area = "civil", relator = "teste" }
        };
        File.WriteAllText(Path.Combine(RulingBrRoot, "sample-5.json"), JsonSerializer.Serialize(rows), Encoding.UTF8);
        File.WriteAllText(Path.Combine(RulingBrRoot, "rulingbr-v1.2.jsonl"),
            string.Join('\n', rows.Select(row => JsonSerializer.Serialize(row))), Encoding.UTF8);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
