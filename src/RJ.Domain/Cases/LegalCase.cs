namespace RJ.Domain.Cases;

public sealed class LegalCase
{
    public LegalCase(
        LegalCaseId id,
        LegalCaseCnj cnj,
        string name,
        string court,
        string phase,
        string status,
        decimal? amount,
        IReadOnlyList<LegalCaseParty> parties,
        IReadOnlyList<LegalCaseLawyer> lawyers,
        IReadOnlyList<LegalCaseClassification> classifications,
        IReadOnlyList<LegalCaseSubject> subjects,
        IReadOnlyList<LegalCaseStep> steps,
        IReadOnlyList<LegalCaseAttachment> attachments,
        IReadOnlyList<LegalCaseFieldProvenance> provenance)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(cnj);

        Id = id;
        Cnj = cnj;
        Name = Required(name, nameof(name));
        Court = Required(court, nameof(court));
        Phase = Required(phase, nameof(phase));
        Status = Required(status, nameof(status));
        Amount = amount;
        Parties = RequireItems(parties, nameof(parties));
        Lawyers = RequireList(lawyers, nameof(lawyers));
        Classifications = RequireItems(classifications, nameof(classifications));
        Subjects = RequireItems(subjects, nameof(subjects));
        Steps = RequireItems(steps, nameof(steps));
        Attachments = RequireList(attachments, nameof(attachments));
        Provenance = RequireItems(provenance, nameof(provenance));
    }

    public LegalCaseId Id { get; }

    public LegalCaseCnj Cnj { get; }

    public string Name { get; }

    public string Court { get; }

    public string Phase { get; }

    public string Status { get; }

    public decimal? Amount { get; }

    public IReadOnlyList<LegalCaseParty> Parties { get; }

    public IReadOnlyList<LegalCaseLawyer> Lawyers { get; }

    public IReadOnlyList<LegalCaseClassification> Classifications { get; }

    public IReadOnlyList<LegalCaseSubject> Subjects { get; }

    public IReadOnlyList<LegalCaseStep> Steps { get; }

    public IReadOnlyList<LegalCaseAttachment> Attachments { get; }

    public IReadOnlyList<LegalCaseFieldProvenance> Provenance { get; }

    internal static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }

        return value.Trim();
    }

    private static T[] RequireItems<T>(IReadOnlyList<T> items, string parameterName)
    {
        var list = RequireList(items, parameterName);
        if (list.Length == 0)
        {
            throw new ArgumentException("Collection cannot be empty.", parameterName);
        }

        return list;
    }

    private static T[] RequireList<T>(IReadOnlyList<T> items, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(items, parameterName);
        return items.ToArray();
    }
}

public sealed record LegalCaseParty(string Name, string Side, string PersonType, string? MainDocument)
{
    public string Name { get; } = LegalCase.Required(Name, nameof(Name));

    public string Side { get; } = LegalCase.Required(Side, nameof(Side));

    public string PersonType { get; } = LegalCase.Required(PersonType, nameof(PersonType));

    public string? MainDocument { get; } = string.IsNullOrWhiteSpace(MainDocument) ? null : MainDocument.Trim();
}

public sealed record LegalCaseLawyer(string Name, string Oab)
{
    public string Name { get; } = LegalCase.Required(Name, nameof(Name));

    public string Oab { get; } = LegalCase.Required(Oab, nameof(Oab));
}

public sealed record LegalCaseClassification(string Code, string Name)
{
    public string Code { get; } = LegalCase.Required(Code, nameof(Code));

    public string Name { get; } = LegalCase.Required(Name, nameof(Name));
}

public sealed record LegalCaseSubject(string Code, string Name)
{
    public string Code { get; } = LegalCase.Required(Code, nameof(Code));

    public string Name { get; } = LegalCase.Required(Name, nameof(Name));
}

public sealed record LegalCaseStep(string Id, DateTimeOffset Date, string Content, string SourceName)
{
    public string Id { get; } = LegalCase.Required(Id, nameof(Id));

    public string Content { get; } = LegalCase.Required(Content, nameof(Content));

    public string SourceName { get; } = LegalCase.Required(SourceName, nameof(SourceName));
}

public sealed record LegalCaseAttachment(string Id, string Name, string StepId, string Extension, string Status, DateTimeOffset Date)
{
    public string Id { get; } = LegalCase.Required(Id, nameof(Id));

    public string Name { get; } = LegalCase.Required(Name, nameof(Name));

    public string StepId { get; } = LegalCase.Required(StepId, nameof(StepId));

    public string Extension { get; } = LegalCase.Required(Extension, nameof(Extension));

    public string Status { get; } = LegalCase.Required(Status, nameof(Status));
}

public sealed record LegalCaseFieldProvenance(
    string FieldPath,
    string SourceName,
    string SourceReference,
    string SourceSha256,
    string ObservedPath,
    DateTimeOffset ObservedAt)
{
    public string FieldPath { get; } = LegalCase.Required(FieldPath, nameof(FieldPath));

    public string SourceName { get; } = LegalCase.Required(SourceName, nameof(SourceName));

    public string SourceReference { get; } = LegalCase.Required(SourceReference, nameof(SourceReference));

    public string SourceSha256 { get; } = RequireSha256(SourceSha256, nameof(SourceSha256));

    public string ObservedPath { get; } = LegalCase.Required(ObservedPath, nameof(ObservedPath));

    private static string RequireSha256(string value, string parameterName)
    {
        var normalized = LegalCase.Required(value, parameterName).ToLowerInvariant();
        if (normalized.Length != 64)
        {
            throw new ArgumentException("SHA-256 must contain exactly 64 hexadecimal characters.", parameterName);
        }

        foreach (var character in normalized)
        {
            if (!Uri.IsHexDigit(character))
            {
                throw new ArgumentException("SHA-256 must contain exactly 64 hexadecimal characters.", parameterName);
            }
        }

        return normalized;
    }
}
