using System.Text.RegularExpressions;
using RJ.Application.Sources;
using RJ.Domain.Cases;

namespace RJ.Application.Generation;

public static partial class ProcessSummaryValidator
{
    public static ProcessSummaryValidationResult Validate(LegalCase legalCase, GenerationModelOutput output)
    {
        return Validate(legalCase, output, null);
    }

    public static ProcessSummaryValidationResult Validate(
        LegalCase legalCase,
        GenerationModelOutput output,
        LegalCaseConsistencyReport? consistencyReport)
    {
        ArgumentNullException.ThrowIfNull(legalCase);
        ArgumentNullException.ThrowIfNull(output);

        var errors = new List<string>();

        if (output.Abstained)
        {
            return new ProcessSummaryValidationResult(errors.Count == 0, errors);
        }

        RequireCanonicalCoverage(legalCase, output, consistencyReport, errors);

        foreach (var claim in output.Claims)
        {
            if (!claim.Text.Contains(legalCase.Cnj.Value, StringComparison.Ordinal)
                && CnjPattern().IsMatch(claim.Text))
            {
                errors.Add("Summary contains a CNJ different from the canonical case CNJ.");
            }

            if (RawCpfPattern().IsMatch(claim.Text) || RawCnpjPattern().IsMatch(claim.Text))
            {
                errors.Add("Summary contains an unmasked CPF or CNPJ.");
            }

            if (ContainsPrognosis(claim.Text))
            {
                errors.Add("Summary contains legal prognosis not supported by deterministic process evidence.");
            }

            if (InventsAttachmentContent(claim.Text))
            {
                errors.Add("Summary claims attachment content even though attachment content is not observed.");
            }
        }

        return new ProcessSummaryValidationResult(errors.Count == 0, errors.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static void RequireCanonicalCoverage(
        LegalCase legalCase,
        GenerationModelOutput output,
        LegalCaseConsistencyReport? consistencyReport,
        List<string> errors)
    {
        if (!ContainsClaim(output, legalCase.Cnj.Value))
        {
            errors.Add("Summary does not mention the canonical CNJ.");
        }

        if (!ContainsClaim(output, legalCase.Name))
        {
            errors.Add("Summary does not mention the canonical case name.");
        }

        if (!ContainsClaim(output, legalCase.Phase))
        {
            errors.Add("Summary does not mention the canonical phase.");
        }

        if (!ContainsClaim(output, legalCase.Status))
        {
            errors.Add("Summary does not mention the canonical status.");
        }

        foreach (var party in legalCase.Parties)
        {
            if (!ContainsClaim(output, party.Name))
            {
                errors.Add("Summary does not mention every canonical party name.");
                break;
            }
        }

        var expectedMovements = Math.Min(legalCase.Steps.Count, ProcessGenerationContextComposer.MaxInlineSteps);
        var observedMovements = output.Claims.Count(claim => claim.Text.StartsWith("Movimentacao:", StringComparison.Ordinal));
        if (observedMovements != expectedMovements)
        {
            errors.Add("Summary movement claim count does not match the canonical inline movement count.");
        }

        foreach (var step in legalCase.Steps.Take(ProcessGenerationContextComposer.MaxInlineSteps))
        {
            var date = ProcessNormalization.FormatSaoPauloDateTime(step.Date);
            if (!ContainsClaim(output, date))
            {
                errors.Add("Summary does not mention every canonical movement date.");
                break;
            }
        }

        if (consistencyReport is not null && consistencyReport.Inconsistencies.Count > 0)
        {
            foreach (var inconsistency in consistencyReport.Inconsistencies)
            {
                if (!output.Claims.Any(claim =>
                    claim.Text.StartsWith("Inconsistencia:", StringComparison.Ordinal)
                    && claim.Text.Contains(inconsistency.FieldPath, StringComparison.Ordinal)))
                {
                    errors.Add("Summary does not mention every deterministic point of attention.");
                    break;
                }
            }
        }
    }

    private static bool ContainsClaim(GenerationModelOutput output, string value) =>
        output.Claims.Any(claim => claim.Text.Contains(value, StringComparison.Ordinal));

    private static bool ContainsPrognosis(string value) =>
        value.Contains("provavelmente", StringComparison.OrdinalIgnoreCase)
        || value.Contains("chance de êxito", StringComparison.OrdinalIgnoreCase)
        || value.Contains("chance de exito", StringComparison.OrdinalIgnoreCase)
        || value.Contains("deve ganhar", StringComparison.OrdinalIgnoreCase)
        || value.Contains("deve perder", StringComparison.OrdinalIgnoreCase);

    private static bool InventsAttachmentContent(string value) =>
        value.Contains("anexo", StringComparison.OrdinalIgnoreCase)
        && !value.Contains("conteudo: nao observado", StringComparison.OrdinalIgnoreCase)
        && !value.Contains("conteudo de anexo nao utilizado", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\d{7}-\d{2}\.\d{4}\.\d\.\d{2}\.\d{4}")]
    private static partial Regex CnjPattern();

    [GeneratedRegex(@"(?<![\d*])\d{3}\.\d{3}\.\d{3}-\d{2}(?![\d*])|(?<![\d*])\d{11}(?![\d*])")]
    private static partial Regex RawCpfPattern();

    [GeneratedRegex(@"(?<![\d*])\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}(?![\d*])|(?<![\d*])\d{14}(?![\d*])")]
    private static partial Regex RawCnpjPattern();
}

public sealed record ProcessSummaryValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors);
