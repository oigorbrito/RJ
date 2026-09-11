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

Expected answer prose is not supplied to the retrieval runner and textual similarity is not used to decide relevance. This prevents the oracle answer text from being injected into the retriever as a matching rule.

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

`rjudi-retrieval-benchmark-report-v1` validates:

- unique cases and query IDs;
- positive relevant ranks;
- exact rank-to-hit-flag consistency;
- aggregate hit counts equal the retained query reports;
- MRR recomputed from retained query ranks;
- finite, non-negative duration;
- failed cases carry no quality measurements;
- successful cases carry no error message.

## Raw observation materialization

`RetrievalEmpiricalObservationPolicy` freezes:

- treatment ID;
- implementation ID;
- configuration reference + SHA-256;
- metric IDs;
- execution-failure gate ID.

The policy must match the treatment identity/configuration embedded in the retrieval report.

`RJ.RetrievalObservationMaterializer` verifies report and policy hashes, then writes one `rjudi-empirical-raw-observation-v3` artifact per case plus an index. Each raw artifact retains source-report and policy references/hashes.

`RJ.EmpiricalSelectionVerifier` dispatches by empirical treatment kind. For retrieval observations it reopens the source report and policy, validates their hashes and identity binding, re-materializes the case observation, and requires byte-for-byte equality with the stored raw artifact.

## Canonical gate

```powershell
.\scripts\test-rjudi-wave-k.ps1
```

The Wave K self-test does not require PostgreSQL or EVAL-010. It:

1. builds the solution;
2. runs focused retrieval/generation/raw-observation regression tests;
3. runs `git diff --check`;
4. creates a frozen synthetic retrieval configuration/report/policy;
5. hashes each artifact;
6. runs `RJ.RetrievalObservationMaterializer`;
7. requires one raw v3 observation and one index;
8. checks report/policy provenance and selected measurements.

This proves the benchmark-report-to-observation machinery only. It is not evidence that R0 is superior and does not demonstrate R1, R2 or R3.

## Remaining external evidence

Real R0 execution still requires PostgreSQL and an admitted EVAL-010 retrieval catalog. Real R1/R2/R3 execution requires actual candidate implementations plus the same admitted paired corpus. Treatment selection remains blocked by `RJ-BLK-003` until those paired observations exist.
