using System.Security.Cryptography;
using RJ.Application.Ingestion;
using RJ.Domain.Documents;

namespace RJ.DomainTests;

public sealed class ResponseFixtureIngestionTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string FixturePath = Path.Combine(RepoRoot, "tests", "fixtures", "rj", "response_60031603620268160021_1.json");

    [Fact]
    public async Task Handler_preserves_real_fixture_raw_content_and_hash()
    {
        var rawContent = await File.ReadAllTextAsync(FixturePath);
        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawContent)));
        var writer = new CapturingWriter();
        var handler = new IngestLegalDocumentHandler(writer);

        await handler.HandleAsync(
            new IngestLegalDocumentCommand(
                "6003160-36.2026.8.16.0021",
                "response_60031603620268160021_1",
                "response_60031603620268160021_1.json",
                rawContent),
            CancellationToken.None);

        var stored = Assert.IsType<LegalDocument>(writer.Stored);
        Assert.Equal(rawContent, stored.RawContent);
        Assert.Equal(rawContent.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'), stored.Content);
        Assert.Equal(expectedHash, stored.ContentSha256);
    }

    [Fact]
    public async Task Handler_is_deterministic_for_the_same_real_fixture()
    {
        var rawContent = await File.ReadAllTextAsync(FixturePath);
        var firstWriter = new CapturingWriter();
        var secondWriter = new CapturingWriter();
        var firstHandler = new IngestLegalDocumentHandler(firstWriter);
        var secondHandler = new IngestLegalDocumentHandler(secondWriter);
        var command = new IngestLegalDocumentCommand(
            "6003160-36.2026.8.16.0021",
            "response_60031603620268160021_1",
            "response_60031603620268160021_1.json",
            rawContent);

        await firstHandler.HandleAsync(command, CancellationToken.None);
        await secondHandler.HandleAsync(command, CancellationToken.None);

        var first = Assert.IsType<LegalDocument>(firstWriter.Stored);
        var second = Assert.IsType<LegalDocument>(secondWriter.Stored);
        Assert.Equal(first.RawContent, second.RawContent);
        Assert.Equal(first.Content, second.Content);
        Assert.Equal(first.ContentSha256, second.ContentSha256);
    }

    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(current, "RJ.slnx")))
        {
            var parent = Directory.GetParent(current) ?? throw new InvalidOperationException("Repository root not found.");
            current = parent.FullName;
        }

        return current;
    }

    private sealed class CapturingWriter : ILegalDocumentWriter
    {
        public LegalDocument? Stored { get; private set; }

        public Task StoreAsync(LegalDocument document, CancellationToken cancellationToken)
        {
            Stored = document;
            return Task.CompletedTask;
        }
    }
}
