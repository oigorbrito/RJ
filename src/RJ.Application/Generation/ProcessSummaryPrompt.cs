namespace RJ.Application.Generation;

public static class ProcessSummaryPrompt
{
    public const string PromptId = "rjudi-process-summary";
    public const string PromptVersion = "rjudi-process-summary-v1";

    public static string BuildQuery(string userInstruction)
    {
        if (string.IsNullOrWhiteSpace(userInstruction))
        {
            throw new ArgumentException("Prompt instruction cannot be empty.", nameof(userInstruction));
        }

        return string.Join(
            Environment.NewLine,
            $"PromptId: {PromptId}",
            $"PromptVersion: {PromptVersion}",
            "Sections: resumo, partes, classe, assuntos, movimentacoes, pontos_de_atencao",
            "Rules: cite every factual claim; cite source inconsistencies as pontos_de_atencao; do not use oracle files; do not invent attachment content; abstain when evidence is absent.",
            $"UserInstruction: {userInstruction.Trim()}");
    }
}
