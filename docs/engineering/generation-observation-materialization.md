# Generation benchmark observation materialization

## Purpose

Wave J closes the reproducibility gap between an executed `GenerationBenchmarkReport` and the raw per-case artifacts consumed by the Wave I empirical-selection procedure.

The materializer does not select a model and does not reinterpret quality thresholds. It performs a deterministic, provenance-preserving transformation of already observed benchmark results.

## Provenance chain

Every admitted generation observation must preserve and verify this chain:

`treatment id -> materialization policy -> model id/configuration -> generation benchmark report -> case observation`

The immutable source report is identified by `sourceArtifactReference` and `sourceArtifactSha256`.

The immutable materialization policy is identified by `materializationPolicyReference` and `materializationPolicySha256`.

The policy itself freezes the treatment ID, benchmark `ModelId`, benchmark `ModelConfiguration`, metric IDs and candidate-execution hard-gate ID. The materializer refuses a report whose model/configuration differs from the policy.

`RJ.EmpiricalSelectionVerifier` verifies both source and policy hashes, reparses them, validates the model/configuration binding, then re-materializes the observation and requires byte-for-byte equality with the stored raw artifact. This prevents manual transcription or relabeling after benchmark execution.

## Observation semantics

For an evaluated generation case:

- execution status is `Pass` because the candidate produced an evaluable output;
- `claim_recall`, `citation_validity`, and `groundedness` are copied as measurements;
- a benchmark-quality result below `1.0` is not converted into a non-compensable execution gate.

For a case where the benchmark runner recorded execution failure and no evaluation:

- execution status is `Fail`;
- measurements are empty;
- the pre-registered `candidate_execution_failure` gate is emitted.

The materializer also rejects contradictory report evidence, including mismatched case/evaluation IDs, inconsistent aggregate counts, an evaluated case carrying execution-error evidence, or a case marked passed differently from its `GenerationEvaluationResult`.

## Raw artifact format

Wave J uses `rjudi-empirical-raw-observation-v3`.

In addition to case/treatment/status/measurements/gates/timestamp, v3 requires:

- `sourceArtifactReference`
- `sourceArtifactSha256`
- `materializationPolicyReference`
- `materializationPolicySha256`

The materializer serializes observations deterministically and emits the SHA-256 of the exact resulting bytes for direct inclusion in the Wave I selection manifest.

## Materialization policy

The frozen policy contains:

- treatment ID;
- expected benchmark model ID;
- expected benchmark model configuration;
- claim-recall metric ID;
- citation-validity metric ID;
- groundedness metric ID;
- candidate-execution-failure gate ID.

Metric IDs must be unique. No metric name is discovered or introduced after benchmark execution.

## CLI

`RJ.GenerationObservationMaterializer` takes:

1. generation benchmark report path;
2. expected report SHA-256;
3. source report reference used by the empirical artifact root;
4. recorded-at timestamp;
5. materialization-policy path;
6. expected policy SHA-256;
7. policy reference used by the empirical artifact root;
8. output directory.

It verifies source and policy hashes before writing anything, validates treatment/model/configuration binding, rejects unsafe case/treatment filename tokens, writes one raw observation per case, then writes a treatment-specific index with the generated observation hashes and provenance references.

Exit codes:

- `0`: materialization completed;
- `2`: missing/precondition input;
- `3`: invalid/hash/contract/I/O failure.

## Canonical executable gate

```powershell
.\scripts\test-rjudi-wave-j.ps1
```

The gate requires no external corpus or provider. It:

1. builds `RJ.slnx`;
2. runs focused materializer/raw-observation/benchmark-runner tests;
3. runs `git diff --check`;
4. executes `harness-selftest-v1` through the existing generation benchmark CLI;
5. hashes the generated report;
6. creates and hashes a self-test treatment/materialization policy bound to `harness-selftest-v1` + `deterministic-selftest`;
7. runs `RJ.GenerationObservationMaterializer`;
8. requires exactly two raw observation v3 artifacts;
9. verifies source-report and materialization-policy references/hashes on each artifact;
10. requires the generated observation index.

This self-test proves the transformation mechanism only. It is not EVAL-010 evidence and cannot select or promote a retrieval or generation treatment.

## Status boundaries

`RJ-BLK-003` still blocks real treatment selection because the admitted 30–50 case EVAL-010 corpus and paired baseline/challenger executions are absent.

Wave J removes manual transcription and treatment relabeling from the executable path once those external observations exist.
