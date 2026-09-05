namespace RJ.BenchmarkCli;

public sealed record BenchmarkRunManifestVerificationResult(
    bool Passed,
    string? ReportPath,
    string? ErrorCode)
{
    public static BenchmarkRunManifestVerificationResult Pass(string reportPath) =>
        new(true, reportPath, null);

    public static BenchmarkRunManifestVerificationResult Fail(string errorCode) =>
        new(false, null, errorCode);
}
