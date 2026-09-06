namespace RJ.Application.Rag;

public sealed class LocalRagIndex
{
    private readonly IReadOnlyList<LocalRagChunk> _chunks;

    public LocalRagIndex(IReadOnlyList<LocalRagChunk> chunks)
    {
        _chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
    }

    public IReadOnlyList<LocalRagChunk> Search(string caseId, string query, int limit)
    {
        if (string.IsNullOrWhiteSpace(caseId))
        {
            throw new ArgumentException("Case identifier cannot be empty.", nameof(caseId));
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query cannot be empty.", nameof(query));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var tokens = Tokenize(query).ToArray();
        var ranked = _chunks
            .Where(chunk => StringComparer.Ordinal.Equals(chunk.CaseId, caseId.Trim()))
            .Select(chunk => (Chunk: chunk, Score: Score(chunk, tokens)))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Chunk.Section, StringComparer.Ordinal)
            .ThenBy(item => item.Chunk.ChunkId, StringComparer.Ordinal)
            .Take(limit)
            .Select(item => item.Chunk)
            .ToArray();

        return ranked;
    }

    private static double Score(LocalRagChunk chunk, IReadOnlyCollection<string> tokens)
    {
        var content = chunk.Content;
        var metadata = string.Join(' ', chunk.Metadata.Values);
        var text = $"{content} {metadata}".ToLowerInvariant();
        double score = 0;
        foreach (var token in tokens)
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += token.Length >= 8 ? 3 : 1;
            }
        }

        if (chunk.Section == "steps" && tokens.Any(token => token.Contains("moviment") || token.Contains("audi")))
        {
            score += 6;
        }

        if (chunk.Section == "attachments" && tokens.Any(token => token.Contains("anex") || token.Contains("attachment")))
        {
            score += 6;
        }

        if (chunk.Section == "overview" && tokens.Any(token => token is "valor" or "juiz" or "status" or "fase" or "tribunal" or "cidade" or "estado" or "comarca"))
        {
            score += 8;
        }

        if (chunk.Section == "parties" && tokens.Any(token => token is "cpf" or "cnpj" or "oab" or "parte" or "autores" or "réu" or "reu" or "advogada" or "advogado"))
        {
            score += 8;
        }

        return score;
    }

    private static IEnumerable<string> Tokenize(string query)
    {
        var token = new System.Text.StringBuilder();
        foreach (var character in query)
        {
            if (char.IsLetterOrDigit(character))
            {
                token.Append(char.ToLowerInvariant(character));
            }
            else if (token.Length > 1)
            {
                yield return token.ToString();
                token.Clear();
            }
            else
            {
                token.Clear();
            }
        }

        if (token.Length > 1)
        {
            yield return token.ToString();
        }
    }
}
