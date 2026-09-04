namespace RJ.Application.Benchmarking;

public sealed record CorpusAdmissionReport(
    string CatalogVersion,
    string CatalogSha256,
    int TotalCases,
    int TotalEvidenceItems,
    int VerifiedEvidenceItems,
    int VerifiedOracles,
    bool Passed,
    IReadOnlyList<CorpusAdmissionCaseReport> Cases);

public sealed record CorpusAdmissionCaseReport(
    string CaseId,
    bool OracleVerified,
    int EvidenceItems,
    int VerifiedEvidenceItems,
    bool Passed,
    IReadOnlyList<CorpusAdmissionFailure> Failures);

public sealed record CorpusAdmissionFailure(
    string ArtifactReference,
    string Gate,
    string Message);
