using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using RJ.Api;
using RJ.Application.Generation;
using RJ.Application.Operations;
using RJ.Application.Sources;

namespace RJ.ApiTests;

public sealed class ProcessSummaryEndpointTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string FixturePath = Path.Combine(RepoRoot, "tests", "fixtures", "rj", "response_60031603620268160021_1.json");
    private static readonly DateTimeOffset SubmittedAt =
        DateTimeOffset.Parse("2026-09-07T20:00:00.000Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Submit_returns_accepted_job_bound_to_snapshot_and_validated_summary()
    {
        var service = CreateService();
        var request = Request("idem-1", File.ReadAllText(FixturePath));

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status202Accepted, ((IStatusCodeHttpResult)result).StatusCode);
        var response = Assert.IsType<ProcessSummaryJobResponse>(((IValueHttpResult)result).Value);
        Assert.Equal("response_60031603620268160021_1", response.CaseId);
        Assert.Equal("6003160-36.2026.8.16.0021", response.Cnj);
        Assert.True(response.IsValid);
        Assert.Equal(64, response.SnapshotSha256.Length);
        Assert.Equal(ProcessSummaryPrompt.PromptVersion, response.SummaryVersion);
        Assert.Equal(0, response.RetrievalCalls);
        Assert.Equal(1, response.Attempts);
        Assert.False(response.Retried);
        Assert.Equal(SubmittedAt, response.CreatedAt);
        Assert.Equal(SubmittedAt, response.ValidatedAt);
        Assert.Collection(
            response.History,
            submitted =>
            {
                Assert.Equal("Submitted", submitted.Status);
                Assert.Equal(SubmittedAt, submitted.ObservedAt);
            },
            validated =>
            {
                Assert.Equal("Validated", validated.Status);
                Assert.Equal(SubmittedAt, validated.ObservedAt);
            });

        var summaryResult = ProcessSummaryEndpoint.GetValidatedSummary(response.JobId, service);
        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)summaryResult).StatusCode);
        var summary = Assert.IsType<GenerationModelOutput>(((IValueHttpResult)summaryResult).Value);
        Assert.False(summary.Abstained);
    }

    [Fact]
    public async Task Submit_is_idempotent_for_same_key_and_same_snapshot()
    {
        var service = CreateService();
        var raw = File.ReadAllText(FixturePath);
        var first = await ProcessSummaryEndpoint.SubmitAsync(Request("idem-2", raw), service, CancellationToken.None);
        var firstResponse = Assert.IsType<ProcessSummaryJobResponse>(((IValueHttpResult)first).Value);

        var second = await ProcessSummaryEndpoint.SubmitAsync(Request("idem-2", raw), service, CancellationToken.None);
        var secondResponse = Assert.IsType<ProcessSummaryJobResponse>(((IValueHttpResult)second).Value);

        Assert.Equal(StatusCodes.Status202Accepted, ((IStatusCodeHttpResult)second).StatusCode);
        Assert.Equal(firstResponse.JobId, secondResponse.JobId);
        Assert.Equal(firstResponse.SnapshotSha256, secondResponse.SnapshotSha256);
    }

    [Fact]
    public async Task Submit_rejects_same_idempotency_key_for_different_snapshot()
    {
        var service = CreateService();
        var raw = File.ReadAllText(FixturePath);
        await ProcessSummaryEndpoint.SubmitAsync(Request("idem-3", raw), service, CancellationToken.None);

        var changedSnapshot = raw.Replace("\"cached\":false", "\"cached\":true", StringComparison.Ordinal);
        var result = await ProcessSummaryEndpoint.SubmitAsync(Request("idem-3", changedSnapshot), service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Idempotency key is already bound to a different process snapshot.", error.Error);
    }

    [Fact]
    public void Get_job_returns_not_found_for_unknown_job()
    {
        var result = ProcessSummaryEndpoint.GetJob("missing", CreateService());

        Assert.Equal(StatusCodes.Status404NotFound, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task Submit_returns_forbidden_for_unauthorized_case()
    {
        var service = CreateService();
        var request = Request("idem-4", File.ReadAllText(FixturePath), ["other-case"]);

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.IsType<ForbidHttpResult>(result);
    }

    [Fact]
    public async Task Submit_returns_forbidden_for_unauthorized_evidence_source()
    {
        var service = CreateService();
        var request = Request(
            "idem-5",
            File.ReadAllText(FixturePath),
            authorizedEvidenceSourceNames: ["DataJud"]);

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.IsType<ForbidHttpResult>(result);
    }

    [Fact]
    public async Task Refresh_plan_returns_read_only_scheduler_decision_for_job_snapshot()
    {
        var service = CreateService();
        var submit = await ProcessSummaryEndpoint.SubmitAsync(
            Request("idem-6", File.ReadAllText(FixturePath)),
            service,
            CancellationToken.None);
        var job = Assert.IsType<ProcessSummaryJobResponse>(((IValueHttpResult)submit).Value);

        var result = ProcessSummaryEndpoint.GetRefreshPlan(
            job.JobId,
            new ProcessSummaryRefreshPlanRequest(new string('c', 64), job.SummaryVersion),
            service);

        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        var response = Assert.IsType<ProcessSummaryRefreshPlanResponse>(((IValueHttpResult)result).Value);
        Assert.Equal("Refresh", response.Action);
        Assert.True(response.RequiresScheduler);
        Assert.Equal("Snapshot hash changed.", response.Reason);
    }

    [Fact]
    public async Task Submit_accepts_observed_attachment_content_without_turning_it_into_unverified_claims()
    {
        var service = CreateService();
        var request = Request(
            "idem-7",
            File.ReadAllText(FixturePath),
            attachmentContents:
            [
                new ProcessAttachmentContentRequest(
                    "response_60031603620268160021_1",
                    "411788364428621657023616086781",
                    "attachment-extractor",
                    "attachments/411788364428621657023616086781.html",
                    "ATO ORDINATORIO OBSERVADO",
                    "2026-09-07T20:00:00.000Z")
            ]);

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status202Accepted, ((IStatusCodeHttpResult)result).StatusCode);
        var response = Assert.IsType<ProcessSummaryJobResponse>(((IValueHttpResult)result).Value);
        Assert.True(response.IsValid);

        var summaryResult = ProcessSummaryEndpoint.GetValidatedSummary(response.JobId, service);
        var summary = Assert.IsType<GenerationModelOutput>(((IValueHttpResult)summaryResult).Value);
        Assert.DoesNotContain(summary.Claims, claim => claim.Text.Contains("ATO ORDINATORIO OBSERVADO", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Submit_sanitizes_invalid_date_parse_errors()
    {
        const string sensitiveDate = "not-a-date with cpf 02727135971 and path C:/secret/source.json";
        var service = CreateService();
        var request = Request("idem-8", File.ReadAllText(FixturePath)) with { ObservedAt = sensitiveDate };

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary request.", error.Error);
        Assert.DoesNotContain("02727135971", error.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("C:/secret/source.json", error.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submit_rejects_oversized_raw_content_before_canonicalization()
    {
        var service = CreateService();
        var request = Request("idem-9", new string('x', IngestionLimits.MaxRawContentBytes + 1));

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task Submit_rejects_oversized_attachment_content()
    {
        var service = CreateService();
        var request = Request(
            "idem-10",
            File.ReadAllText(FixturePath),
            attachmentContents:
            [
                new ProcessAttachmentContentRequest(
                    "response_60031603620268160021_1",
                    "411788364428621657023616086781",
                    "attachment-extractor",
                    "attachments/411788364428621657023616086781.html",
                    new string('x', IngestionLimits.MaxProcessAttachmentTextBytes + 1),
                    "2026-09-07T20:00:00.000Z")
            ]);

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task Submit_rejects_too_many_attachment_content_items()
    {
        var service = CreateService();
        var attachmentContents = Enumerable.Range(0, IngestionLimits.MaxProcessAttachmentContentItems + 1)
            .Select(index => new ProcessAttachmentContentRequest(
                "response_60031603620268160021_1",
                $"attachment-{index}",
                "attachment-extractor",
                $"attachments/{index}.html",
                "observed text",
                "2026-09-07T20:00:00.000Z"))
            .ToArray();
        var request = Request(
            "idem-11",
            File.ReadAllText(FixturePath),
            attachmentContents: attachmentContents);

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public void Refresh_plan_sanitizes_invalid_request_errors()
    {
        var service = CreateService();

        var result = ProcessSummaryEndpoint.GetRefreshPlan(
            "missing",
            new ProcessSummaryRefreshPlanRequest(" ", "version with cpf 02727135971"),
            service);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary refresh plan request.", error.Error);
        Assert.DoesNotContain("02727135971", error.Error, StringComparison.Ordinal);
    }

    private static ProcessSummaryRequest Request(
        string idempotencyKey,
        string rawContent,
        IReadOnlyList<string>? authorizedCaseIds = null,
        IReadOnlyList<string>? authorizedEvidenceSourceNames = null,
        IReadOnlyList<ProcessAttachmentContentRequest>? attachmentContents = null) =>
        new(
            idempotencyKey,
            JuditProcessSourceAdapter.JuditSourceSystem,
            "Judit",
            "tests/fixtures/rj/response_60031603620268160021_1.json",
            rawContent,
            "2026-09-02T18:51:04.800Z",
            "Resuma o processo.",
            "tenant-1",
            "subject-1",
            authorizedCaseIds ?? ["response_60031603620268160021_1"],
            false,
            authorizedEvidenceSourceNames,
            attachmentContents);

    private static ProcessSummaryJobService CreateService()
    {
        var canonicalization = new ProcessSourceCanonicalizationService(new[] { new JuditProcessSourceAdapter() });
        var composer = new ProcessGenerationContextComposer(new GenerationContextBuilder());
        var generation = new GenerationService(new DeterministicProcessSummaryModel());
        return new ProcessSummaryJobService(
            canonicalization,
            composer,
            generation,
            new FixedClock(SubmittedAt),
            new NoopProcessSummaryTelemetry(),
            new EmptyProcessAttachmentContentStore());
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IProcessSummaryClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
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
