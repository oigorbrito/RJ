# Retrieval benchmark and observation materialization

## Purpose

Wave K closes the reproducibility gap between an executed retrieval treatment and the raw per-case artifacts consumed by the Wave I empirical-selection procedure.

It does not select R0/R1/R2/R3. It defines how a retrieval treatment is identified, executed through the production retrieval boundary, measured per case, and deterministically materialized into evidence.

## Treatment identity

Benchmark execution requires `IIdentifiedLegalDocumentSearch` rather than an arbitrary strategy label. The implementation exposes an immutable `ImplementationId`, and the runner refuses execution when it differs from `RetrievalBenchmarkTreatmentMetadata.ImplementationId`.

The production PostgreSQL lexical implementation identifies itself as:

`postgres-ts-rank-cd-v1`

This identifies PostgreSQL full-text search using the existing `ts_rank_cd` implementation. It must not be described as BM25.

## Relevance rule

Retrieval relevance is based only on exact admitted evidence identity:

- `DocumentId`
- `ContentSha256`

Expected answer prose is not supplied to the retrieval runner and textual similarity is not used to decide relevance. This prevents oracle answer text from being injected into the retriever as a matching rule.

A returned document whose `CaseId` differs from the experimental case is classified as treatment execution failure rather than a quality observation.

## Experimental unit and measurements

The experimental unit remains one admitted legal process case. Each case may contain multiple pre-specified retrieval queries.

The report retains query-level first-relevant ranks and derives per-case:

- hit@1 rate;
- hit@3 rate;
- hit@5 rate;
- MRR;
- retrieval duration in milliseconds.

Duration is an observed measurement, not an acceptance threshold.

Treatment execution failure is kept separate from quality: a failed case has no quality measurements and materializes the pre-registered `candidate_execution_failure` non-compensable gate.

## Report integrity

`rjudi-retrieval-benchmark-report-v1` retains exact execution `GitCommit` and runtime and validates:

- unique cases and query IDs;
- positive relevant ranks;
- exact rank-to-hit-flag consistency;
- aggregate hit counts equal the retained query reports;
- MRR recomputed from retained query ranks;
- finite, non-negative duration;
- failed cases carry no quality measurements;
- successful cases carry no error message.

The empirical-selection verifier requires source report commit/runtime to match the frozen selection manifest.

## Production R0 benchmark runner

`RJ.RetrievalBenchmarkRunner` is the executable path for the currently implemented R0 baseline. It:

1. requires `RJ_POSTGRES_CONNECTION`;
2. verifies exact catalog and configuration SHA-256 values;
3. checks the current PostgreSQL schema;
4. constructs the real `PostgresLegalDocumentSearch`;
5. requires configured implementation ID to equal the runtime implementation ID;
6. resolves `git rev-parse HEAD` itself rather than accepting a caller-supplied commit;
7. records `RuntimeInformation.FrameworkDescription`;
8. executes the provider-neutral retrieval runner;
9. writes the typed retrieval benchmark report and its SHA-256.

Its configuration format is `rjudi-retrieval-benchmark-config-v1` and requires an implementation ID plus a search limit between 5 and 100.

R1/R2/R3 are not routed through this PostgreSQL-specific executable until actual candidate implementations exist.

## Raw observation materialization

`RetrievalEmpiricalObservationPolicy` freezes:

- treatment ID;
- implementation ID;
- configuration reference + SHA-256;
- metric IDs;
- execution-failure gate ID.

The policy must match both the retrieval report and the corresponding `EmpiricalTreatmentDefinition` used by the selection manifest.

`RJ.RetrievalObservationMaterializer` verifies report and policy hashes, then writes one `rjudi-empirical-raw-observation-v3` artifact per case plus an index. Each raw artifact retains source-report and policy references/hashes.

`RJ.EmpiricalSelectionVerifier` dispatches by empirical treatment kind. For retrieval observations it reopens the source report and policy, validates source report commit/runtime, configuration identity and hashes, re-materializes the case observation, and requires byte-for-byte equality with the stored raw artifact. The generation path now applies the same selection-treatment configuration and source-report commit/runtime binding.

## Canonical gate

```powershell
.\scripts\test-rjudi-wave-k.ps1
```

The Wave K self-test does not require PostgreSQL or EVAL-010. It:

1. builds the solution;
2. runs focused retrieval/generation/raw-observation regression tests;
3. runs `git diff --check`;
4. resolves and embeds the exact HEAD/runtime in a frozen synthetic retrieval report;
5. creates and hashes a synthetic retrieval configuration/report/policy;
6. runs `RJ.RetrievalObservationMaterializer`;
7. requires one raw v3 observation and one index;
8. checks report/policy provenance and selected measurements.

This proves the benchmark-report-to-observation machinery only. It is not evidence that R0 is superior and does not demonstrate R1, R2 or R3.

## Remaining external evidence

Real R0 execution still requires PostgreSQL and an admitted EVAL-010 retrieval catalog. Real R1/R2/R3 execution requires actual candidate implementations plus the same admitted paired corpus. Treatment selection remains blocked by `RJ-BLK-003` until those paired observations exist.
