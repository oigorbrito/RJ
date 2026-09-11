# Generation benchmark observation materialization

## Purpose

Wave J closes the reproducibility gap between an executed `GenerationBenchmarkReport` and the raw per-case artifacts consumed by the Wave I empirical-selection procedure.

The materializer does not select a model and does not reinterpret quality thresholds. It only performs a deterministic, provenance-preserving transformation of already observed benchmark results.

## Source artifact

The source is an immutable `GenerationBenchmarkReport` JSON file. Its exact bytes are identified by:

- `sourceArtifactReference`
- `sourceArtifactSha256`

Every emitted raw observation carries both values. `RJ.EmpiricalSelectionVerifier` reopens that source artifact and verifies its SHA-256 before accepting the observation.

## Observation semantics

For an evaluated generation case:

- execution status is `Pass` because the candidate produced an evaluable output;
- `claim_recall`, `citation_validity`, and `groundedness` are copied as measurements;
- a benchmark-quality result below `1.0` is **not** converted into a non-compensable execution gate.

For a case where the benchmark runner recorded execution failure and no evaluation:

- execution status is `Fail`;
- measurements are empty;
- the pre-registered `candidate_execution_failure` gate is emitted.

This distinction prevents a benchmark-specific quality threshold from being silently reinterpreted as a generic empirical hard gate.

## Raw artifact format

Wave J uses `rjudi-empirical-raw-observation-v2`.

In addition to case/treatment/status/measurements/gates/timestamp, v2 requires:

- `sourceArtifactReference`
- `sourceArtifactSha256`

The materializer serializes observations deterministically and emits the SHA-256 of the resulting bytes for direct inclusion in the Wave I selection manifest.

## Materialization policy

Metric IDs and the candidate-execution gate ID are supplied by an immutable policy artifact. The CLI verifies the policy SHA-256 before use. The policy must provide unique metric IDs for:

- claim recall;
- citation validity;
- groundedness;
- candidate execution failure gate.

No metric name is discovered or introduced after benchmark execution.

## CLI

`RJ.GenerationObservationMaterializer` takes:

1. generation benchmark report path;
2. expected report SHA-256;
3. treatment ID;
4. source report reference used by the empirical artifact root;
5. recorded-at timestamp;
6. materialization-policy path;
7. expected policy SHA-256;
8. output directory.

It verifies source and policy hashes before writing anything, rejects unsafe case/treatment filename tokens, writes one raw observation per case, then writes a treatment-specific index with the generated observation hashes.

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
6. creates and hashes a frozen self-test materialization policy;
7. runs `RJ.GenerationObservationMaterializer`;
8. requires exactly two raw observation v2 artifacts;
9. verifies each observation points to the exact source-report SHA;
10. requires the generated observation index.

This self-test proves the transformation mechanism only. It is not EVAL-010 evidence and cannot select or promote a retrieval or generation treatment.

## Status boundaries

`RJ-BLK-003` still blocks real treatment selection because the admitted 30–50 case EVAL-010 corpus and paired baseline/challenger executions are absent.

Wave J removes manual transcription from the executable path once those external observations exist.
