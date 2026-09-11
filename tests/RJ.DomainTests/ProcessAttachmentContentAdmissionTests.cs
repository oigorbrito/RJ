using System.Globalization;
using RJ.Application.Sources;

namespace RJ.DomainTests;

public sealed class ProcessAttachmentContentAdmissionTests
{
    private static readonly string[] ExpectedExtractorOrder = ["extractor-a", "extractor-b"];

    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string FixturePath = Path.Combine(RepoRoot, "tests", "fixtures", "rj", "response_60031603620268160021_1.json");
    private static readonly DateTimeOffset ObservedAt =
        DateTimeOffset.Parse("2026-09-02T18:51:04.800Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Admit_accepts_only_content_bound_to_observed_attachment_metadata()
    {
        var legalCase = LoadCanonicalCase();
        var content = new ProcessAttachmentContent(
            legalCase.Id.Value,
            legalCase.Attachments[0].Id,
            "html-extractor",
            "attachments/411788364428621657023616086781.html",
            "Texto extraido observado.",
            ObservedAt);

        var admitted = ProcessAttachmentContentAdmission.Admit(legalCase, [content]);

        var item = Assert.Single(admitted);
        Assert.Equal(content.AttachmentId, item.AttachmentId);
        Assert.Equal(content.ContentSha256, item.ContentSha256);
        Assert.Equal($"attachments[{content.AttachmentId}].content", item.ToProvenance().FieldPath);
    }

    [Fact]
    public void Admit_rejects_content_for_other_case_or_unknown_attachment()
    {
        var legalCase = LoadCanonicalCase();
        var otherCase = new ProcessAttachmentContent(
            "other-case",
            legalCase.Attachments[0].Id,
            "html-extractor",
            "attachments/other.html",
            "Texto observado.",
            ObservedAt);
        var unknownAttachment = new ProcessAttachmentContent(
            legalCase.Id.Value,
            "unknown-attachment",
            "html-extractor",
            "attachments/unknown.html",
            "Texto observado.",
            ObservedAt);

        Assert.Throws<InvalidOperationException>(() => ProcessAttachmentContentAdmission.Admit(legalCase, [otherCase]));
        Assert.Throws<InvalidOperationException>(() => ProcessAttachmentContentAdmission.Admit(legalCase, [unknownAttachment]));
    }

    [Fact]
    public void Admit_orders_content_deterministically()
    {
        var legalCase = LoadCanonicalCase();
        var first = new ProcessAttachmentContent(
            legalCase.Id.Value,
            legalCase.Attachments[0].Id,
            "extractor-b",
            "b.html",
            "Texto B.",
            ObservedAt);
        var second = new ProcessAttachmentContent(
            legalCase.Id.Value,
            legalCase.Attachments[0].Id,
            "extractor-a",
            "a.html",
            "Texto A.",
            ObservedAt);

        var admitted = ProcessAttachmentContentAdmission.Admit(legalCase, [first, second]);

        Assert.Equal(ExpectedExtractorOrder, admitted.Select(item => item.SourceName));
    }

    [Fact]
    public void Attachment_content_rejects_missing_observed_instant()
    {
        Assert.Throws<ArgumentException>(() => new ProcessAttachmentContent(
            "case-001",
            "attachment-001",
            "html-extractor",
            "attachments/attachment-001.html",
            "Texto observado.",
            default));
    }

    [Fact]
    public void In_memory_store_rejects_null_attachment_content_items()
    {
        Assert.Throws<ArgumentException>(() => new InMemoryProcessAttachmentContentStore([null!]));
    }

    private static RJ.Domain.Cases.LegalCase LoadCanonicalCase()
    {
        var rawContent = File.ReadAllText(FixturePath);
        var source = new ProcessSourceDocument(
            JuditProcessSourceAdapter.JuditSourceSystem,
            "Judit",
            "tests/fixtures/rj/response_60031603620268160021_1.json",
            rawContent,
            ObservedAt);

        return new JuditProcessSourceAdapter().Canonicalize(source);
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
}
