using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RJ.BenchmarkCli;

public static class BenchmarkRunManifestVerifier
{
    public static BenchmarkRunManifestVerificationResult Verify(string manifestPath)
    {
        ArgumentNullException.ThrowIfNull(manifestPath);

        try
        {
            var manifestJson = File.ReadAllText(manifestPath, Encoding.UTF8);
            var manifest = BenchmarkRunManifestJson.Deserialize(manifestJson);
            if (manifest is null)
            {
                return BenchmarkRunManifestVerificationResult.Fail("manifest_deserialization_failed");
            }

            var manifestError = ValidateManifest(manifest);
            if (manifestError is not null)
            {
                return BenchmarkRunManifestVerificationResult.Fail(manifestError);
            }

            if (!TryResolveReportPath(manifestPath, manifest.OutputPath, out var reportPath))
            {
                return BenchmarkRunManifestVerificationResult.Fail("report_path_escape");
            }

            if (!File.Exists(reportPath))
            {
                return BenchmarkRunManifestVerificationResult.Fail("report_missing");
            }

            var reportBytes = File.ReadAllBytes(reportPath);
            var reportSha256 = ComputeSha256(reportBytes);
            if (!StringComparer.Ordinal.Equals(reportSha256, manifest.ReportSha256))
            {
                return BenchmarkRunManifestVerificationResult.Fail("report_checksum_mismatch");
            }

            return BenchmarkRunManifestVerificationResult.Pass(reportPath);
        }
        catch (JsonException)
        {
            return BenchmarkRunManifestVerificationResult.Fail("manifest_json_invalid");
        }
        catch (IOException)
        {
            return BenchmarkRunManifestVerificationResult.Fail("manifest_read_failed");
        }
        catch (UnauthorizedAccessException)
        {
            return BenchmarkRunManifestVerificationResult.Fail("manifest_read_failed");
        }
    }

    private static string? ValidateManifest(BenchmarkRunManifest manifest)
    {
        if (!StringComparer.Ordinal.Equals(manifest.ManifestVersion, BenchmarkRunManifestJson.CurrentVersion))
        {
            return "manifest_version_mismatch";
        }

        if (string.IsNullOrWhiteSpace(manifest.GitCommit)
            || string.IsNullOrWhiteSpace(manifest.Runtime)
            || string.IsNullOrWhiteSpace(manifest.CatalogVersion)
            || string.IsNullOrWhiteSpace(manifest.ModelId)
            || string.IsNullOrWhiteSpace(manifest.ModelConfiguration)
            || string.IsNullOrWhiteSpace(manifest.Seed)
            || string.IsNullOrWhiteSpace(manifest.Command)
            || string.IsNullOrWhiteSpace(manifest.OutputPath)
            || string.IsNullOrWhiteSpace(manifest.ReportSha256))
        {
            return "manifest_missing_required_field";
        }

        if (manifest.ReportSha256.Length != 64 || manifest.ReportSha256.Any(character => !Uri.IsHexDigit(character)))
        {
            return "report_checksum_invalid";
        }

        return null;
    }

    private static bool TryResolveReportPath(string manifestPath, string outputPath, out string reportPath)
    {
        if (Path.IsPathRooted(outputPath))
        {
            reportPath = outputPath;
            return true;
        }

        var manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? ".";
        var baseDirectory = Path.GetFullPath(manifestDirectory);
        var resolved = Path.GetFullPath(Path.Combine(baseDirectory, outputPath));
        var relativePath = Path.GetRelativePath(baseDirectory, resolved);

        if (Path.IsPathRooted(relativePath)
            || relativePath == "."
            || relativePath == ".."
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            reportPath = string.Empty;
            return false;
        }

        reportPath = resolved;
        return true;
    }

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
