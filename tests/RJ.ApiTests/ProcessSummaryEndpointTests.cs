using System.Globalization;
using System.Text.Json;
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
    public async Task Get_validated_summary_returns_not_found_for_failed_validation_job()
    {
        var service = CreateService(new IncompleteProcessSummaryModel());
        var submit = await ProcessSummaryEndpoint.SubmitAsync(
            Request("idem-failed-validation", File.ReadAllText(FixturePath)),
            service,
            CancellationToken.None);
        var job = Assert.IsType<ProcessSummaryJobResponse>(((IValueHttpResult)submit).Value);

        Assert.Equal(StatusCodes.Status202Accepted, ((IStatusCodeHttpResult)submit).StatusCode);
        Assert.Equal("Failed", job.Status);
        Assert.False(job.IsValid);

        var result = ProcessSummaryEndpoint.GetValidatedSummary(job.JobId, service);

        Assert.Equal(StatusCodes.Status404NotFound, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task Submit_sanitizes_failed_validation_response()
    {
        const string rawCpf = "027.271.359-71";
        var service = CreateService(new RawPiiProcessSummaryModel(rawCpf));

        var result = await ProcessSummaryEndpoint.SubmitAsync(
            Request("idem-failed-validation-pii", File.ReadAllText(FixturePath)),
            service,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status202Accepted, ((IStatusCodeHttpResult)result).StatusCode);
        var response = Assert.IsType<ProcessSummaryJobResponse>(((IValueHttpResult)result).Value);
        Assert.Equal("Failed", response.Status);
        Assert.False(response.IsValid);
        Assert.Equal(["process_summary_validation_failed"], response.Errors);
        Assert.DoesNotContain("Summary contains an unmasked CPF or CNPJ.", response.Errors);
        Assert.DoesNotContain(rawCpf, JsonSerializer.Serialize(response), StringComparison.Ordinal);
        Assert.DoesNotContain("02727135971", JsonSerializer.Serialize(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_job_sanitizes_failed_validation_response()
    {
        const string rawCpf = "027.271.359-71";
        var service = CreateService(new RawPiiProcessSummaryModel(rawCpf));
        var submit = await ProcessSummaryEndpoint.SubmitAsync(
            Request("idem-failed-validation-pii-get", File.ReadAllText(FixturePath)),
            service,
            CancellationToken.None);
        var submitted = Assert.IsType<ProcessSummaryJobResponse>(((IValueHttpResult)submit).Value);

        var result = ProcessSummaryEndpoint.GetJob(submitted.JobId, service);

        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        var response = Assert.IsType<ProcessSummaryJobResponse>(((IValueHttpResult)result).Value);
        Assert.Equal(submitted.JobId, response.JobId);
        Assert.Equal(submitted.SnapshotSha256, response.SnapshotSha256);
        Assert.Equal(submitted.SummaryVersion, response.SummaryVersion);
        Assert.Equal("Failed", response.Status);
        Assert.False(response.IsValid);
        Assert.Equal(["process_summary_validation_failed"], response.Errors);
        Assert.Equal(submitted.Errors, response.Errors);
        Assert.DoesNotContain(rawCpf, JsonSerializer.Serialize(response), StringComparison.Ordinal);
        Assert.DoesNotContain("02727135971", JsonSerializer.Serialize(response), StringComparison.Ordinal);
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
    public void Get_job_sanitizes_invalid_job_id()
    {
        var result = ProcessSummaryEndpoint.GetJob(" ", CreateService());

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary job request.", error.Error);
        Assert.DoesNotContain("Value cannot be empty", error.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Get_validated_summary_sanitizes_invalid_job_id()
    {
        var result = ProcessSummaryEndpoint.GetValidatedSummary(" ", CreateService());

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary job request.", error.Error);
        Assert.DoesNotContain("Value cannot be empty", error.Error, StringComparison.OrdinalIgnoreCase);
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
    public async Task Submit_rejects_unknown_request_attachment_content_with_fixed_conflict()
    {
        const string sensitiveAttachmentText = "ATO ORDINATORIO OBSERVADO com cpf 02727135971";
        var service = CreateService();
        var request = Request(
            "idem-8",
            File.ReadAllText(FixturePath),
            attachmentContents:
            [
                new ProcessAttachmentContentRequest(
                    "response_60031603620268160021_1",
                    "unknown-attachment",
                    "attachment-extractor",
                    "attachments/unknown.html",
                    sensitiveAttachmentText,
                    "2026-09-07T20:00:00.000Z")
            ]);

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Attachment content must reference observed attachment metadata.", error.Error);
        Assert.DoesNotContain("02727135971", error.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveAttachmentText, error.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submit_sanitizes_invalid_date_parse_errors()
    {
        const string sensitiveDate = "not-a-date with cpf 02727135971 and path C:/secret/source.json";
        var service = CreateService();
        var request = Request("idem-9", File.ReadAllText(FixturePath)) with { ObservedAt = sensitiveDate };

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary request.", error.Error);
        Assert.DoesNotContain("02727135971", error.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("C:/secret/source.json", error.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submit_sanitizes_null_raw_content_errors()
    {
        var service = CreateService();
        var request = Request("idem-10", null!);

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary request.", error.Error);
        Assert.DoesNotContain("Value cannot be null", error.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submit_sanitizes_malformed_raw_json_errors()
    {
        const string sensitiveRawContent = "{ \"cpf\": \"02727135971\", ";
        var service = CreateService();
        var request = Request("idem-malformed-json", sensitiveRawContent);

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary request.", error.Error);
        Assert.DoesNotContain("02727135971", error.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("cpf", error.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submit_sanitizes_malformed_process_source_schema_errors()
    {
        const string sensitiveRawContent = "{ \"cpf\": \"02727135971\" }";
        var service = CreateService();
        var request = Request("idem-malformed-schema", sensitiveRawContent);

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary request.", error.Error);
        Assert.DoesNotContain("02727135971", error.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("page_data", error.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submit_sanitizes_missing_required_process_source_fields()
    {
        const string sensitiveRawContent = """
            {
              "page_data": [
                {
                  "response_type": "lawsuit",
                  "response_data": {
                    "name": "case with cpf 02727135971"
                  }
                }
              ]
            }
            """;
        var service = CreateService();
        var request = Request("idem-missing-required-source-field", sensitiveRawContent);

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary request.", error.Error);
        Assert.DoesNotContain("02727135971", error.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("code", error.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submit_sanitizes_missing_lawsuit_process_source_page()
    {
        const string sensitiveRawContent = """
            {
              "page_data": [
                {
                  "response_type": "summary",
                  "response_data": {
                    "iaSummary": "resumo com cpf 02727135971"
                  }
                }
              ]
            }
            """;
        var service = CreateService();
        var request = Request("idem-missing-lawsuit-source-page", sensitiveRawContent);

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary request.", error.Error);
        Assert.DoesNotContain("02727135971", error.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("iaSummary", error.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submit_sanitizes_non_array_process_source_pages()
    {
        const string sensitiveRawContent = """
            {
              "page_data": {
                "cpf": "02727135971"
              }
            }
            """;
        var service = CreateService();
        var request = Request("idem-non-array-source-pages", sensitiveRawContent);

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary request.", error.Error);
        Assert.DoesNotContain("02727135971", error.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("page_data", error.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submit_sanitizes_non_object_process_source_page_items()
    {
        const string sensitiveRawContent = """
            {
              "page_data": [
                "cpf 02727135971"
              ]
            }
            """;
        var service = CreateService();
        var request = Request("idem-non-object-source-page-item", sensitiveRawContent);

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary request.", error.Error);
        Assert.DoesNotContain("02727135971", error.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("response_type", error.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submit_sanitizes_non_object_process_source_response()
    {
        const string sensitiveRawContent = """
            {
              "page_data": [
                {
                  "response_type": "lawsuit",
                  "response_data": "cpf 02727135971"
                }
              ]
            }
            """;
        var service = CreateService();
        var request = Request("idem-non-object-source-response", sensitiveRawContent);

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary request.", error.Error);
        Assert.DoesNotContain("02727135971", error.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("response_data", error.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submit_sanitizes_non_array_required_process_source_collections()
    {
        var sensitiveRawContent = File.ReadAllText(FixturePath)
            .Replace("\"parties\":[", "\"parties\":{\"cpf\":\"02727135971\",\"items\":[", StringComparison.Ordinal)
            .Replace("\"courts\":[", "]} ,\"courts\":[", StringComparison.Ordinal);
        var service = CreateService();
        var request = Request("idem-non-array-source-collection", sensitiveRawContent);

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary request.", error.Error);
        Assert.DoesNotContain("02727135971", error.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("parties", error.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submit_sanitizes_invalid_process_source_amount_type()
    {
        var sensitiveRawContent = File.ReadAllText(FixturePath)
            .Replace("\"amount\":30000", "\"amount\":\"30000 with cpf 02727135971\"", StringComparison.Ordinal);
        var service = CreateService();
        var request = Request("idem-invalid-source-amount-type", sensitiveRawContent);

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary request.", error.Error);
        Assert.DoesNotContain("02727135971", error.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("amount", error.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submit_sanitizes_invalid_process_source_secrecy_level_type()
    {
        var sensitiveRawContent = File.ReadAllText(FixturePath)
            .Replace("\"secrecy_level\":0", "\"secrecy_level\":\"0 with cpf 02727135971\"", StringComparison.Ordinal);
        var service = CreateService();
        var request = Request("idem-invalid-source-secrecy-level-type", sensitiveRawContent);

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary request.", error.Error);
        Assert.DoesNotContain("02727135971", error.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("secrecy_level", error.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submit_sanitizes_negative_process_source_secrecy_level()
    {
        var sensitiveRawContent = File.ReadAllText(FixturePath)
            .Replace("\"secrecy_level\":0", "\"secrecy_level\":-1", StringComparison.Ordinal);
        var service = CreateService();
        var request = Request("idem-negative-source-secrecy-level", sensitiveRawContent);

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary request.", error.Error);
        Assert.DoesNotContain("Legal case secrecy level cannot be negative", error.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secrecy", error.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submit_sanitizes_invalid_process_source_step_date()
    {
        var sensitiveRawContent = File.ReadAllText(FixturePath)
            .Replace("\"step_date\":\"2026-09-02T16:09:16.000Z\"", "\"step_date\":\"not-a-date with cpf 02727135971\"", StringComparison.Ordinal);
        var service = CreateService();
        var request = Request("idem-invalid-source-step-date", sensitiveRawContent);

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary request.", error.Error);
        Assert.DoesNotContain("02727135971", error.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("step_date", error.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submit_sanitizes_invalid_process_source_attachment_date()
    {
        var sensitiveRawContent = File.ReadAllText(FixturePath)
            .Replace("\"attachment_date\":\"2026-09-02T15:56:04.000Z\"", "\"attachment_date\":\"not-a-date with cpf 02727135971\"", StringComparison.Ordinal);
        var service = CreateService();
        var request = Request("idem-invalid-source-attachment-date", sensitiveRawContent);

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary request.", error.Error);
        Assert.DoesNotContain("02727135971", error.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("attachment_date", error.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submit_sanitizes_null_request_errors()
    {
        var result = await ProcessSummaryEndpoint.SubmitAsync(null!, CreateService(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary request.", error.Error);
        Assert.DoesNotContain("Value cannot be null", error.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submit_sanitizes_null_attachment_content_items()
    {
        var request = Request(
            "idem-11",
            File.ReadAllText(FixturePath),
            attachmentContents: [null!]);

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, CreateService(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary request.", error.Error);
    }

    [Fact]
    public async Task Submit_sanitizes_null_attachment_text_errors()
    {
        var service = CreateService();
        var request = Request(
            "idem-12",
            File.ReadAllText(FixturePath),
            attachmentContents:
            [
                new ProcessAttachmentContentRequest(
                    "response_60031603620268160021_1",
                    "411788364428621657023616086781",
                    "attachment-extractor",
                    "attachments/411788364428621657023616086781.html",
                    null!,
                    "2026-09-07T20:00:00.000Z")
            ]);

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary request.", error.Error);
        Assert.DoesNotContain("ExtractedText", error.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submit_sanitizes_unexpected_conflict_messages()
    {
        const string sensitiveSourceSystem = "unknown-source-with-cpf-02727135971";
        var service = CreateService();
        var request = Request("idem-13", File.ReadAllText(FixturePath)) with { SourceSystem = sensitiveSourceSystem };

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Process summary request could not be completed.", error.Error);
        Assert.DoesNotContain(sensitiveSourceSystem, error.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("02727135971", error.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submit_rejects_oversized_raw_content_before_canonicalization()
    {
        var service = CreateService();
        var request = Request("idem-14", new string('x', IngestionLimits.MaxRawContentBytes + 1));

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task Submit_rejects_oversized_attachment_content()
    {
        var service = CreateService();
        var request = Request(
            "idem-15",
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
            "idem-16",
            File.ReadAllText(FixturePath),
            attachmentContents: attachmentContents);

        var result = await ProcessSummaryEndpoint.SubmitAsync(request, service, CancellationToken.None);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task Refresh_plan_sanitizes_invalid_request_errors()
    {
        var service = CreateService();
        var submit = await ProcessSummaryEndpoint.SubmitAsync(
            Request("idem-17", File.ReadAllText(FixturePath)),
            service,
            CancellationToken.None);
        var job = Assert.IsType<ProcessSummaryJobResponse>(((IValueHttpResult)submit).Value);

        var result = ProcessSummaryEndpoint.GetRefreshPlan(
            job.JobId,
            new ProcessSummaryRefreshPlanRequest(" ", "version with cpf 02727135971"),
            service);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary refresh plan request.", error.Error);
        Assert.DoesNotContain("02727135971", error.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Refresh_plan_sanitizes_invalid_request_before_missing_job()
    {
        var result = ProcessSummaryEndpoint.GetRefreshPlan(
            "missing",
            new ProcessSummaryRefreshPlanRequest(" ", "version with cpf 02727135971"),
            CreateService());

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary refresh plan request.", error.Error);
        Assert.DoesNotContain("02727135971", error.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Refresh_plan_sanitizes_invalid_version_before_missing_job()
    {
        var result = ProcessSummaryEndpoint.GetRefreshPlan(
            "missing",
            new ProcessSummaryRefreshPlanRequest(new string('a', 64), " "),
            CreateService());

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary refresh plan request.", error.Error);
    }

    [Fact]
    public void Refresh_plan_sanitizes_malformed_snapshot_hash_before_missing_job()
    {
        var result = ProcessSummaryEndpoint.GetRefreshPlan(
            "missing",
            new ProcessSummaryRefreshPlanRequest("not-a-sha256-with-cpf-02727135971", ProcessSummaryPrompt.PromptVersion),
            CreateService());

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary refresh plan request.", error.Error);
        Assert.DoesNotContain("02727135971", error.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Refresh_plan_sanitizes_invalid_job_id()
    {
        var result = ProcessSummaryEndpoint.GetRefreshPlan(
            " ",
            new ProcessSummaryRefreshPlanRequest(new string('a', 64), ProcessSummaryPrompt.PromptVersion),
            CreateService());

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary refresh plan request.", error.Error);
        Assert.DoesNotContain("Value cannot be empty", error.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_plan_sanitizes_null_request_errors()
    {
        var service = CreateService();
        var submit = await ProcessSummaryEndpoint.SubmitAsync(
            Request("idem-18", File.ReadAllText(FixturePath)),
            service,
            CancellationToken.None);
        var job = Assert.IsType<ProcessSummaryJobResponse>(((IValueHttpResult)submit).Value);

        var result = ProcessSummaryEndpoint.GetRefreshPlan(job.JobId, null!, service);

        Assert.Equal(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)result).StatusCode);
        var error = Assert.IsType<ProcessSummaryError>(((IValueHttpResult)result).Value);
        Assert.Equal("Invalid process summary refresh plan request.", error.Error);
        Assert.DoesNotContain("Value cannot be null", error.Error, StringComparison.OrdinalIgnoreCase);
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

    private static ProcessSummaryJobService CreateService(IGenerationModel? model = null)
    {
        var canonicalization = new ProcessSourceCanonicalizationService(new[] { new JuditProcessSourceAdapter() });
        var composer = new ProcessGenerationContextComposer(new GenerationContextBuilder());
        var generation = new GenerationService(model ?? new DeterministicProcessSummaryModel());
        return new ProcessSummaryJobService(
            canonicalization,
            composer,
            generation,
            new FixedClock(SubmittedAt),
            new NoopProcessSummaryTelemetry(),
            new EmptyProcessAttachmentContentStore());
    }

    private sealed class IncompleteProcessSummaryModel : IGenerationModel
    {
        public Task<GenerationModelOutput> GenerateAsync(
            GenerationContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = context.Items.First(item => item.Excerpt.StartsWith("CNJ:", StringComparison.Ordinal));
            return Task.FromResult(new GenerationModelOutput(
                false,
                null,
                [
                    new GenerationClaim(
                        item.Excerpt,
                        [new GenerationCitation(item.DocumentId, item.ContentSha256, item.Position.StartOffset, item.Position.Length)])
                ]));
        }
    }

    private sealed class RawPiiProcessSummaryModel(string rawCpf) : IGenerationModel
    {
        public Task<GenerationModelOutput> GenerateAsync(
            GenerationContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = context.Items.First(item => item.Excerpt.StartsWith("CNJ:", StringComparison.Ordinal));
            return Task.FromResult(new GenerationModelOutput(
                false,
                null,
                [
                    new GenerationClaim(
                        $"{item.Excerpt}; CPF observado {rawCpf}",
                        [new GenerationCitation(item.DocumentId, item.ContentSha256, item.Position.StartOffset, item.Position.Length)])
                ]));
        }
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
