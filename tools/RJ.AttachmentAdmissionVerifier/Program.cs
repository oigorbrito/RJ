using System.Text.Json;
using RJ.Application.Attachments;
using RJ.Application.Sources;

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: RJ.AttachmentAdmissionVerifier <manifest.json> <manifest-sha256> <artifact-root> <canonical-source.json>");
    return 2;
}

string manifestPath;
string expectedManifestSha256;
string artifactRoot;
string canonicalSourcePath;
try
{
    manifestPath = Path.GetFullPath(args[0]);
    expectedManifestSha256 = NormalizeSha256(args[1]);
    artifactRoot = Path.GetFullPath(args[2]);
    canonicalSourcePath = Path.GetFullPath(args[3]);
}
catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
{
    Console.Error.WriteLine($"Attachment verifier precondition failed: {exception.Message}");
    return 2;
}

if (!File.Exists(manifestPath) || !File.Exists(canonicalSourcePath) || !Directory.Exists(artifactRoot))
{
    Console.Error.WriteLine("Attachment verifier input path is missing.");
    return 2;
}

try
{
    var manifestBytes = await File.ReadAllBytesAsync(manifestPath);
    var observedManifestSha256 = AttachmentAdmissionManifest.ComputeSha256(manifestBytes);
    if (!StringComparer.Ordinal.Equals(observedManifestSha256, expectedManifestSha256))
    {
        throw new InvalidOperationException(
            $"Attachment manifest SHA-256 mismatch. Expected {expectedManifestSha256}, observed {observedManifestSha256}.");
    }

    var manifest = AttachmentAdmissionManifest.Parse(manifestBytes).Validate();
    var canonicalBytes = await File.ReadAllBytesAsync(canonicalSourcePath);
    var canonicalSha256 = AttachmentAdmissionManifest.ComputeSha256(canonicalBytes);
    if (!StringComparer.Ordinal.Equals(canonicalSha256, manifest.CanonicalSourceSha256.ToLowerInvariant()))
    {
        throw new InvalidOperationException(
            $"Canonical source SHA-256 mismatch. Expected {manifest.CanonicalSourceSha256.ToLowerInvariant()}, observed {canonicalSha256}.");
    }

    var source = new ProcessSourceDocument(
        JuditProcessSourceAdapter.JuditSourceSystem,
        "ATT-001 canonical source",
        manifest.CanonicalSourceReference,
        await File.ReadAllTextAsync(canonicalSourcePath),
        manifest.CanonicalSourceObservedAt);
    var legalCase = new JuditProcessSourceAdapter().Canonicalize(source);
    var service = new AttachmentAdmissionService(new RootedAttachmentReader(artifactRoot));
    var report = await service.AdmitAsync(legalCase, manifest.Binary, manifest.Extraction, CancellationToken.None);

    Console.WriteLine(JsonSerializer.Serialize(
        new AttachmentVerificationEnvelope(observedManifestSha256, canonicalSha256, report),
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));
    return report.Passed ? 0 : 3;
}
catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Attachment admission verification failed: {exception.Message}");
    return 3;
}

static string NormalizeSha256(string value)
{
    var normalized = value?.Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(normalized) || normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
    {
        throw new ArgumentException("manifest-sha256 must contain exactly 64 hexadecimal characters.", nameof(value));
    }

    return normalized;
}

sealed record AttachmentVerificationEnvelope(
    string ManifestSha256,
    string CanonicalSourceSha256,
    AttachmentAdmissionReport Admission);

sealed class RootedAttachmentReader(string root) : IAttachmentArtifactReader
{
    private readonly string _root = EnsureTrailingSeparator(Path.GetFullPath(root));
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public async Task<ReadOnlyMemory<byte>> ReadAsync(string artifactReference, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artifactReference))
        {
            throw new ArgumentException("Attachment artifact reference cannot be empty.", nameof(artifactReference));
        }

        var candidate = Path.GetFullPath(Path.Combine(_root, artifactReference));
        if (!candidate.StartsWith(_root, PathComparison))
        {
            throw new InvalidOperationException("Attachment artifact reference escapes the configured root.");
        }

        return await File.ReadAllBytesAsync(candidate, cancellationToken);
    }

    private static string EnsureTrailingSeparator(string value) =>
        value.EndsWith(Path.DirectorySeparatorChar) ? value : value + Path.DirectorySeparatorChar;
}
