using System.Security.Cryptography;
using System.Text.Json;

namespace RJ.DomainTests;

public sealed class Wave1CorpusContractTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string FixturePath = Path.Combine(RepoRoot, "tests", "fixtures", "rj", "response_60031603620268160021_1.json");

    [Fact]
    public void Real_fixture_exists_and_parses()
    {
        Assert.True(File.Exists(FixturePath));
        Assert.Equal("b5decf20e6bb330a7a58974711b7ec8e72215d74568e014e8cf1ced14ee916aa", ComputeSha256(FixturePath));

        using var doc = JsonDocument.Parse(File.ReadAllText(FixturePath));
        Assert.Equal(2, doc.RootElement.GetProperty("page_data").GetArrayLength());
        Assert.Contains(doc.RootElement.GetProperty("page_data").EnumerateArray(), item => item.GetProperty("response_type").GetString() == "summary");
    }

    [Fact]
    public void Contract_dataset_and_query_set_are_versioned_and_loadable()
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "docs", "onda1", "rj-corpus-contract.v2.json")));
        using var dataset = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "docs", "onda1", "rj-dataset-baseline.v2.json")));
        using var querySet = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "docs", "onda1", "rj-query-set.v2.json")));

        Assert.Equal("rj-corpus-contract-v2", contract.RootElement.GetProperty("schema_version").GetString());
        Assert.Equal("rj-juridical-response-60031603620268160021-2026-09-06", contract.RootElement.GetProperty("dataset_version").GetString());
        Assert.Equal("rj-juridical-response-60031603620268160021-2026-09-06", dataset.RootElement.GetProperty("dataset_version").GetString());
        Assert.Equal("rj-juridical-response-query-set-v2", querySet.RootElement.GetProperty("query_set_version").GetString());
        Assert.Contains(contract.RootElement.GetProperty("fields").EnumerateArray(), field => field.GetProperty("path").GetString() == "page_data[].response_data.parties[].lawyers[].documents[]");
        Assert.Contains(querySet.RootElement.GetProperty("queries").EnumerateArray(), query => query.GetProperty("query_id").GetString() == "q-024");
    }

    [Fact]
    public void Policy_marks_iaSummary_excluded_and_does_not_remove_summary()
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "docs", "onda1", "rj-corpus-contract.v2.json")));
        using var fixture = JsonDocument.Parse(File.ReadAllText(FixturePath));

        var excluded = contract.RootElement.GetProperty("rag_policy").GetProperty("excluded").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains("iaSummary", excluded);
        Assert.Contains("page_data[].response_type=summary", excluded);
        Assert.Contains(fixture.RootElement.GetProperty("page_data").EnumerateArray(), item => item.GetProperty("response_type").GetString() == "summary");
    }

    [Fact]
    public void Query_set_oracles_resolve_against_fixture()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(FixturePath));
        using var querySet = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "docs", "onda1", "rj-query-set.v2.json")));

        foreach (var query in querySet.RootElement.GetProperty("queries").EnumerateArray())
        {
            var oraclePath = query.GetProperty("oracle_path").GetString()!;
            Assert.True(ResolveOracle(fixture.RootElement, oraclePath), oraclePath);
        }
    }

    [Fact]
    public void Process_oracle_is_structured_versioned_and_bound_to_fixture()
    {
        using var oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "docs", "onda1", "rj-process-oracle.v1.json")));
        var root = oracle.RootElement;

        Assert.Equal("rj-process-oracle-v1", root.GetProperty("schema_version").GetString());
        Assert.Equal("rj-juridical-response-60031603620268160021-2026-09-06", root.GetProperty("dataset_version").GetString());
        Assert.Equal("tests/fixtures/rj/response_60031603620268160021_1.json", root.GetProperty("fixture").GetString());
        Assert.Equal(ComputeSha256(FixturePath), root.GetProperty("fixture_sha256").GetString());
        Assert.Equal("6003160-36.2026.8.16.0021", root.GetProperty("cnj").GetString());

        Assert.NotEmpty(root.GetProperty("facts").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("milestones").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("inconsistencies").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("abstentions").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("forbidden_claims").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("formatting_constraints").EnumerateArray());
    }

    [Fact]
    public void Process_oracle_evidence_paths_resolve_against_fixture()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(FixturePath));
        using var oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "docs", "onda1", "rj-process-oracle.v1.json")));

        foreach (var fact in oracle.RootElement.GetProperty("facts").EnumerateArray())
        {
            var oraclePath = fact.GetProperty("oracle_path").GetString()!;
            Assert.True(ResolveOracle(fixture.RootElement, oraclePath), oraclePath);
        }

        foreach (var milestone in oracle.RootElement.GetProperty("milestones").EnumerateArray())
        {
            var oraclePath = milestone.GetProperty("oracle_path").GetString()!;
            Assert.True(ResolveOracle(fixture.RootElement, oraclePath), oraclePath);
        }

        foreach (var inconsistency in oracle.RootElement.GetProperty("inconsistencies").EnumerateArray())
        {
            foreach (var observedPath in inconsistency.GetProperty("observed_paths").EnumerateArray())
            {
                var path = observedPath.GetString()!;
                Assert.True(ResolveOracle(fixture.RootElement, path), path);
            }
        }
    }

    [Fact]
    public void Rjudi_process_benchmark_protocol_is_versioned_and_bound_to_current_corpus()
    {
        var oraclePath = Path.Combine(RepoRoot, "docs", "onda1", "rj-process-oracle.v1.json");
        using var protocol = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "docs", "onda1", "rjudi-process-benchmark-protocol.v1.json")));
        var root = protocol.RootElement;
        var corpus = root.GetProperty("corpus");
        var treatment = root.GetProperty("treatment_identity");

        Assert.Equal("rjudi-process-benchmark-protocol-v1", root.GetProperty("protocol_version").GetString());
        Assert.Equal("response_60031603620268160021_1", corpus.GetProperty("case_id").GetString());
        Assert.Equal("6003160-36.2026.8.16.0021", corpus.GetProperty("cnj").GetString());
        Assert.Equal(ComputeSha256(FixturePath), corpus.GetProperty("fixture_sha256").GetString());
        Assert.Equal(ComputeSha256(oraclePath), corpus.GetProperty("oracle_sha256").GetString());

        Assert.Equal("rjudi-process-summary", treatment.GetProperty("prompt_id").GetString());
        Assert.Equal("rjudi-process-summary-v1", treatment.GetProperty("prompt_version").GetString());
        Assert.Equal(0, treatment.GetProperty("retrieval_calls").GetInt32());

        var paired = root.GetProperty("paired_comparison");
        Assert.True(paired.GetProperty("required").GetBoolean());
        Assert.True(paired.GetProperty("challenger_required_before_promotion").GetBoolean());

        var failureRetention = root.GetProperty("failure_retention");
        Assert.True(failureRetention.GetProperty("enabled").GetBoolean());
        Assert.True(failureRetention.GetProperty("retain_failed_cases").GetBoolean());

        var gates = root.GetProperty("non_compensable_gates").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains("exact_citations", gates);
        Assert.Contains("no_raw_pii", gates);
        Assert.Contains("no_oracle_leakage", gates);
        Assert.Contains("no_attachment_content_invention", gates);
        Assert.Contains("process_validator_pass", gates);
        Assert.Contains("retrieval_calls_zero_case_001", gates);
        Assert.Contains("all_13_steps_inline", gates);

        var decisionRules = root.GetProperty("decision_rules").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains("non_compensable_gates_must_pass", decisionRules);
        Assert.Contains("failed_case_cannot_be_averaged_away", decisionRules);
        Assert.Contains("candidate_promotion_requires_paired_artifact", decisionRules);
    }

    [Fact]
    public void Rjudi_m1_gate_is_local_core_only_and_bound_to_current_artifacts()
    {
        var oraclePath = Path.Combine(RepoRoot, "docs", "onda1", "rj-process-oracle.v1.json");
        var protocolPath = Path.Combine(RepoRoot, "docs", "onda1", "rjudi-process-benchmark-protocol.v1.json");
        using var gate = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "docs", "onda1", "rjudi-m1-gate.v1.json")));
        var root = gate.RootElement;

        Assert.Equal("rjudi-m1-gate-v1", root.GetProperty("gate_version").GetString());
        Assert.Equal("NOT_TESTED", root.GetProperty("status").GetString());
        Assert.Equal("scripts/test-rjudi-m1.ps1", root.GetProperty("canonical_script").GetString());
        Assert.True(root.GetProperty("preserves_existing_local_rag_mvp").GetBoolean());
        Assert.Equal("c627a1bcdc87ff9b0bbd5ccc0b7d108daa5e324d", root.GetProperty("base_commit").GetString());
        Assert.Equal("response_60031603620268160021_1", root.GetProperty("case_id").GetString());
        Assert.Equal("6003160-36.2026.8.16.0021", root.GetProperty("cnj").GetString());
        Assert.Equal(ComputeSha256(FixturePath), root.GetProperty("fixture_sha256").GetString());
        Assert.Equal(ComputeSha256(oraclePath), root.GetProperty("process_oracle_sha256").GetString());
        Assert.Equal(ComputeSha256(protocolPath), root.GetProperty("benchmark_protocol_sha256").GetString());

        var requiredEvidence = root.GetProperty("required_evidence").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains("retrieval_calls_zero_for_case_001", requiredEvidence);
        Assert.Contains("legal_case_cnj_ascii_digits_only", requiredEvidence);
        Assert.Contains("legal_case_amount_non_negative", requiredEvidence);
        Assert.Contains("legal_case_attachments_reference_observed_steps", requiredEvidence);
        Assert.Contains("process_validator_pass", requiredEvidence);
        Assert.Contains("process_validator_rejects_abstention_with_available_evidence", requiredEvidence);
        Assert.Contains("corrective_retry_bounded", requiredEvidence);
        Assert.Contains("evidence_source_authorization", requiredEvidence);
        Assert.Contains("job_tracking_history", requiredEvidence);
        Assert.Contains("read_only_refresh_plan_api", requiredEvidence);
        Assert.Contains("invalid_job_id_requests_sanitized", requiredEvidence);
        Assert.Contains("request_attachment_content_admission", requiredEvidence);
        Assert.Contains("sanitized_request_parse_errors", requiredEvidence);
        Assert.Contains("process_validator_rejects_uncited_factual_claims", requiredEvidence);
        Assert.Contains("process_validator_rejects_missing_required_sections", requiredEvidence);
        Assert.Contains("process_validator_requires_court_and_amount_coverage", requiredEvidence);
        Assert.Contains("process_validator_requires_lawyer_coverage", requiredEvidence);
        Assert.Contains("canonical_secrecy_level_preserved", requiredEvidence);
        Assert.Contains("deterministic_secrecy_level_inconsistencies", requiredEvidence);
        Assert.Contains("process_validator_requires_secrecy_level_coverage", requiredEvidence);
        Assert.Contains("malformed_raw_json_errors_sanitized", requiredEvidence);
        Assert.Contains("malformed_process_source_schema_errors_sanitized", requiredEvidence);
        Assert.Contains("missing_lawsuit_process_source_page_sanitized", requiredEvidence);
        Assert.Contains("missing_required_process_source_fields_sanitized", requiredEvidence);
        Assert.Contains("non_array_process_source_pages_sanitized", requiredEvidence);
        Assert.Contains("non_object_process_source_page_items_sanitized", requiredEvidence);
        Assert.Contains("non_object_process_source_response_sanitized", requiredEvidence);
        Assert.Contains("non_array_required_process_source_collections_sanitized", requiredEvidence);
        Assert.Contains("invalid_process_source_amount_type_sanitized", requiredEvidence);
        Assert.Contains("invalid_process_source_secrecy_level_type_sanitized", requiredEvidence);
        Assert.Contains("invalid_process_source_step_date_sanitized", requiredEvidence);
        Assert.Contains("invalid_process_source_attachment_date_sanitized", requiredEvidence);
        Assert.Contains("unknown_source_system_errors_sanitized", requiredEvidence);
        Assert.Contains("null_request_payloads_sanitized", requiredEvidence);
        Assert.Contains("bounded_process_summary_payloads", requiredEvidence);
        Assert.Contains("hashed_caller_observability", requiredEvidence);
        Assert.Contains("refresh_plan_telemetry", requiredEvidence);
        Assert.Contains("duration_telemetry", requiredEvidence);
        Assert.Contains("validation_failure_telemetry", requiredEvidence);
        Assert.Contains("non_success_telemetry_sanitized", requiredEvidence);
        Assert.Contains("terminal_validation_observability_attributes", requiredEvidence);
        Assert.Contains("emitted_observability_events_cataloged", requiredEvidence);
        Assert.Contains("sanitized_refresh_reason_telemetry", requiredEvidence);
        Assert.Contains("null_refresh_plan_request_sanitized", requiredEvidence);
        Assert.Contains("freshness_telemetry", requiredEvidence);
        Assert.Contains("unknown_attachment_content_conflict_sanitized", requiredEvidence);
        Assert.Contains("unexpected_conflict_messages_sanitized", requiredEvidence);
        Assert.Contains("failed_summary_not_returned_as_validated", requiredEvidence);
        Assert.Contains("failed_validation_response_sanitized", requiredEvidence);
        Assert.Contains("public_validation_errors_are_stable_codes", requiredEvidence);
        Assert.Contains("job_polling_failed_validation_response_sanitized", requiredEvidence);
        Assert.Contains("job_polling_matches_submission_envelope", requiredEvidence);
        Assert.Contains("refresh_plan_request_validated_before_missing_job", requiredEvidence);
        Assert.Contains("refresh_plan_service_validates_current_state_before_missing_job", requiredEvidence);
        Assert.Contains("refresh_plan_current_version_validated_before_missing_job", requiredEvidence);
        Assert.Contains("refresh_plan_snapshot_hash_format_validated", requiredEvidence);
        Assert.Contains("refresh_plan_snapshot_hash_normalized", requiredEvidence);
        Assert.Contains("refresh_plan_catalog_matches_emitted_attributes", requiredEvidence);
        Assert.Contains("emitted_telemetry_attributes_declared_in_catalog", requiredEvidence);
        Assert.Contains("end_to_end_duration_telemetry", requiredEvidence);
        Assert.Contains("distinct_idempotency_keys_do_not_collide", requiredEvidence);
        Assert.Contains("authorization_precedes_idempotent_replay", requiredEvidence);
        Assert.Contains("idempotency_replay_scoped_to_caller_identity", requiredEvidence);
        Assert.Contains("concurrent_idempotency_conflict_rechecked_after_generation", requiredEvidence);

        var notClaimed = root.GetProperty("not_claimed").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains("FULL_RJUDI_MVP", notClaimed);
        Assert.Contains("PRODUCTION_READY", notClaimed);
        Assert.Contains("30_50_process_corpus", notClaimed);
        Assert.Contains("generation_provider_selection", notClaimed);

        var blockers = root.GetProperty("blockers").EnumerateArray().Select(item => item.GetProperty("blocker_id").GetString()).ToArray();
        Assert.Contains("BLK-CORPUS-REAL-001", blockers);
        Assert.Contains("BLK-POSTGRES-001", blockers);

        var blockerRegister = File.ReadAllText(Path.Combine(RepoRoot, "docs", "engineering", "blockers.md"));
        Assert.Contains("BLK-POSTGRES-001", blockerRegister);
        Assert.Contains("RJ_POSTGRES_CONNECTION", blockerRegister);
    }

    [Fact]
    public void Rjudi_empirical_selection_gate_blocks_provider_selection_until_real_corpus_exists()
    {
        using var gate = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "docs", "onda1", "rjudi-empirical-selection-gate.v1.json")));
        var blockerRegister = File.ReadAllText(Path.Combine(RepoRoot, "docs", "engineering", "blockers.md"));
        var root = gate.RootElement;

        Assert.Equal("rjudi-empirical-selection-gate-v1", root.GetProperty("gate_version").GetString());
        Assert.Equal("PROCEDURE_IMPLEMENTED_SELECTION_BLOCKED", root.GetProperty("status").GetString());
        Assert.Contains(
            root.GetProperty("blocked_by").EnumerateArray(),
            item => item.GetProperty("blocker_id").GetString() == "RJ-BLK-003"
                && item.GetProperty("source").GetString() == "docs/engineering/blockers.md");

        var required = root.GetProperty("required_before_selection").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains("30_50_admitted_legal_process_corpus", required);
        Assert.Contains("reviewed_oracle_artifacts", required);
        Assert.Contains("corpus_manifest_sha256_verified", required);
        Assert.Contains("exact_corpus_case_set_has_one_baseline_and_one_challenger_observation_per_case", required);
        Assert.Contains("failure_retention_artifacts", required);
        Assert.Contains("non_compensable_gate_results_by_case", required);
        Assert.Contains("retrieval_latency_cost_privacy_measurements_when_retrieval_treatments_are_compared", required);
        Assert.Contains("generation_latency_cost_privacy_measurements_when_generation_treatments_are_compared", required);

        var forbidden = root.GetProperty("forbidden_conclusions").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains("retrieval_provider_selected_without_complete_paired_manifest", forbidden);
        Assert.Contains("generation_provider_selected_without_complete_paired_manifest", forbidden);
        Assert.Contains("vector_search_required", forbidden);
        Assert.Contains("hybrid_reranker_promoted", forbidden);
        Assert.Contains("openai_promoted_to_production", forbidden);
        Assert.Contains("production_ready", forbidden);

        var allowed = root.GetProperty("currently_allowed").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains("local_deterministic_core_validation", allowed);
        Assert.Contains("benchmark_harness_validation", allowed);
        Assert.Contains("single_case_fixture_validation", allowed);
        Assert.Contains("self_test_or_deterministic_fake_execution", allowed);

        var implemented = root.GetProperty("implemented_procedure").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains("external_empirical_selection_verifier", implemented);
        Assert.Contains("runtime_verified", implemented);
        Assert.Contains("RJ-BLK-003", blockerRegister);
        Assert.Contains("authorized 30–50 case paired execution", blockerRegister);
    }

    private static bool ResolveOracle(JsonElement root, string oraclePath)
    {
        return oraclePath switch
        {
            "page_data[0].response_data.code" => Get(root, "page_data", 0, "response_data", "code"),
            "page_data[0].response_data.tribunal_acronym" => Get(root, "page_data", 0, "response_data", "tribunal_acronym"),
            "page_data[0].response_data.county" => Get(root, "page_data", 0, "response_data", "county"),
            "page_data[0].response_data.city" => Get(root, "page_data", 0, "response_data", "city"),
            "page_data[0].response_data.state" => Get(root, "page_data", 0, "response_data", "state"),
            "page_data[0].response_data.amount" => Get(root, "page_data", 0, "response_data", "amount"),
            "page_data[0].response_data.judge" => Get(root, "page_data", 0, "response_data", "judge"),
            "page_data[0].response_data.status" => Get(root, "page_data", 0, "response_data", "status"),
            "page_data[0].response_data.phase" => Get(root, "page_data", 0, "response_data", "phase"),
            "page_data[0].response_data.parties[].name" => Get(root, "page_data", 0, "response_data", "parties", 0, "name"),
            "page_data[0].response_data.parties[3].name" => Get(root, "page_data", 0, "response_data", "parties", 3, "name"),
            "page_data[0].response_data.parties[0].lawyers[0].name" => Get(root, "page_data", 0, "response_data", "parties", 0, "lawyers", 0, "name"),
            "page_data[0].response_data.parties[0].lawyers[0].documents[0].document" => Get(root, "page_data", 0, "response_data", "parties", 0, "lawyers", 0, "documents", 0, "document"),
            "page_data[0].response_data.parties[0].main_document" => Get(root, "page_data", 0, "response_data", "parties", 0, "main_document"),
            "page_data[0].response_data.parties[3]" => Get(root, "page_data", 0, "response_data", "parties", 3),
            "page_data[0].response_data.parties[3].lawyers" => Get(root, "page_data", 0, "response_data", "parties", 3, "lawyers"),
            "page_data[0].response_data.subjects[].name" => Get(root, "page_data", 0, "response_data", "subjects", 0, "name"),
            "page_data[0].response_data.subjects" => Get(root, "page_data", 0, "response_data", "subjects"),
            "page_data[0].response_data.classifications[].name" => Get(root, "page_data", 0, "response_data", "classifications", 0, "name"),
            "page_data[0].response_data.area" => Get(root, "page_data", 0, "response_data", "area"),
            "page_data[0].response_data.last_step.content" => Get(root, "page_data", 0, "response_data", "last_step", "content"),
            "page_data[0].response_data.last_step.step_date" => Get(root, "page_data", 0, "response_data", "last_step", "step_date"),
            "page_data[0].response_data.last_step.steps_count" => Get(root, "page_data", 0, "response_data", "last_step", "steps_count"),
            "page_data[0].response_data.steps[3].content" => Get(root, "page_data", 0, "response_data", "steps", 3, "content"),
            "page_data[0].response_data.steps[9].content" => Get(root, "page_data", 0, "response_data", "steps", 9, "content"),
            "page_data[0].response_data.steps[12].content" => Get(root, "page_data", 0, "response_data", "steps", 12, "content"),
            "page_data[0].response_data.attachments.length" => root.GetProperty("page_data").EnumerateArray().First().GetProperty("response_data").GetProperty("attachments").GetArrayLength() == 2,
            "page_data[0].response_data.attachments[0].attachment_id" => Get(root, "page_data", 0, "response_data", "attachments", 0, "attachment_id"),
            "page_data[0].response_data.attachments[0].extension" => Get(root, "page_data", 0, "response_data", "attachments", 0, "extension"),
            "page_data[0].response_data.crawler.source_name" => Get(root, "page_data", 0, "response_data", "crawler", "source_name"),
            "page_data[0].response_data.tags.labor" => Get(root, "page_data", 0, "response_data", "tags", "labor"),
            _ => false
        };
    }

    private static bool Get(JsonElement root, params object[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (segment is int index)
            {
                current = current.EnumerateArray().ElementAt(index);
            }
            else
            {
                current = current.GetProperty((string)segment);
            }
        }

        return true;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
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
