using RJ.Domain.Cases;

namespace RJ.Application.Sources;

public static class LegalCaseConsistencyEngine
{
    public static LegalCaseConsistencyReport Analyze(IReadOnlyList<LegalCase> cases)
    {
        ArgumentNullException.ThrowIfNull(cases);

        if (cases.Count == 0)
        {
            throw new ArgumentException("At least one legal case is required.", nameof(cases));
        }

        var cnj = cases[0].Cnj.Value;
        foreach (var legalCase in cases)
        {
            if (!StringComparer.Ordinal.Equals(cnj, legalCase.Cnj.Value))
            {
                throw new InvalidOperationException("Cannot compare legal cases with different CNJ values.");
            }
        }

        var checks = new[]
        {
            Check(cases, "name", legalCase => legalCase.Name),
            Check(cases, "court", legalCase => legalCase.Court),
            Check(cases, "phase", legalCase => legalCase.Phase),
            Check(cases, "status", legalCase => legalCase.Status),
            Check(cases, "secrecy_level", legalCase => legalCase.SecrecyLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Check(cases, "amount", legalCase => legalCase.Amount?.ToString("0.#############################", System.Globalization.CultureInfo.InvariantCulture) ?? "NAO OBSERVADO"),
            Check(cases, "parties", legalCase => Join(legalCase.Parties.Select(item => $"{item.Name}|{item.Side}|{item.PersonType}|{item.MainDocument ?? string.Empty}"))),
            Check(cases, "lawyers", legalCase => Join(legalCase.Lawyers.Select(item => $"{item.Name}|{item.Oab}"))),
            Check(cases, "classifications", legalCase => Join(legalCase.Classifications.Select(item => $"{item.Code}|{item.Name}"))),
            Check(cases, "subjects", legalCase => Join(legalCase.Subjects.Select(item => $"{item.Code}|{item.Name}"))),
            Check(cases, "steps", legalCase => Join(legalCase.Steps.Select(item => $"{item.Id}|{item.Date:O}|{item.Content}"))),
            Check(cases, "attachments", legalCase => Join(legalCase.Attachments.Select(item => $"{item.Id}|{item.Name}|{item.StepId}|{item.Extension}|{item.Status}|{item.Date:O}")))
        };

        return new LegalCaseConsistencyReport(
            cnj,
            checks.OrderBy(item => item.FieldPath, StringComparer.Ordinal).ToArray());
    }

    private static LegalCaseConsistencyFinding Check(
        IEnumerable<LegalCase> cases,
        string fieldPath,
        Func<LegalCase, string> readValue)
    {
        var observations = cases
            .Select(legalCase => new LegalCaseFieldObservation(
                fieldPath,
                SourceNameFor(legalCase, fieldPath),
                SourceReferenceFor(legalCase, fieldPath),
                ObservedPathFor(legalCase, fieldPath),
                readValue(legalCase)))
            .OrderBy(item => item.Value, StringComparer.Ordinal)
            .ThenBy(item => item.SourceName, StringComparer.Ordinal)
            .ThenBy(item => item.SourceReference, StringComparer.Ordinal)
            .ThenBy(item => item.ObservedPath, StringComparer.Ordinal)
            .ToArray();

        var distinctValues = observations
            .Select(item => item.Value)
            .Distinct(StringComparer.Ordinal)
            .Count();

        return new LegalCaseConsistencyFinding(
            fieldPath,
            distinctValues == 1 ? LegalCaseConsistencyStatus.Consistent : LegalCaseConsistencyStatus.Inconsistent,
            observations);
    }

    private static string Join(IEnumerable<string> values)
    {
        var joined = string.Join(";", values.OrderBy(item => item, StringComparer.Ordinal));
        return string.IsNullOrWhiteSpace(joined) ? "NAO OBSERVADO" : joined;
    }

    private static string SourceNameFor(LegalCase legalCase, string fieldPath) =>
        ProvenanceFor(legalCase, fieldPath)?.SourceName
        ?? legalCase.Provenance.OrderBy(item => item.SourceName, StringComparer.Ordinal).First().SourceName;

    private static string SourceReferenceFor(LegalCase legalCase, string fieldPath) =>
        ProvenanceFor(legalCase, fieldPath)?.SourceReference
        ?? legalCase.Provenance.OrderBy(item => item.SourceReference, StringComparer.Ordinal).First().SourceReference;

    private static string ObservedPathFor(LegalCase legalCase, string fieldPath) =>
        ProvenanceFor(legalCase, fieldPath)?.ObservedPath ?? "NAO OBSERVADO";

    private static LegalCaseFieldProvenance? ProvenanceFor(LegalCase legalCase, string fieldPath) =>
        legalCase.Provenance
            .Where(item => StringComparer.Ordinal.Equals(item.FieldPath, fieldPath))
            .OrderBy(item => item.SourceName, StringComparer.Ordinal)
            .ThenBy(item => item.SourceReference, StringComparer.Ordinal)
            .ThenBy(item => item.ObservedPath, StringComparer.Ordinal)
            .FirstOrDefault();
}

public sealed record LegalCaseConsistencyReport(
    string Cnj,
    IReadOnlyList<LegalCaseConsistencyFinding> Findings)
{
    public IReadOnlyList<LegalCaseConsistencyFinding> Inconsistencies =>
        Findings.Where(item => item.Status == LegalCaseConsistencyStatus.Inconsistent).ToArray();
}

public sealed record LegalCaseConsistencyFinding(
    string FieldPath,
    LegalCaseConsistencyStatus Status,
    IReadOnlyList<LegalCaseFieldObservation> Observations);

public sealed record LegalCaseFieldObservation(
    string FieldPath,
    string SourceName,
    string SourceReference,
    string ObservedPath,
    string Value);

public enum LegalCaseConsistencyStatus
{
    Consistent,
    Inconsistent
}
