# Reproducible generation benchmark

## Scope

This phase defines a deterministic batch runner for generation candidates. It does not select, integrate, or promote any external provider.

## Versioned fixture catalog

`GenerationBenchmarkCatalog` carries an explicit `Version` and a non-empty collection of `GenerationEvaluationCase` fixtures.

Catalog case identifiers must be non-empty and unique. The runner rejects duplicate ids and rejects metadata whose `CatalogVersion` differs from the supplied catalog version.

The catalog version must change when oracle semantics, fixture content, source citations, abstention expectations, or acceptance interpretation changes materially.

## Reproducibility metadata

Every run requires `GenerationBenchmarkMetadata` containing:

- `GitCommit`;
- `Runtime`;
- `CatalogVersion`;
- `ModelId`;
- `ModelConfiguration`;
- `Seed`.

All fields are required. `ModelConfiguration` must contain only reproducibility-relevant, non-secret configuration. Credentials, tokens, endpoints containing secrets, or sensitive payloads must not be recorded in benchmark artifacts.

## Batch execution

Cases are executed in ordinal `CaseId` order, independent of catalog insertion order.

A candidate exception fails that case and is recorded as `ErrorType` plus `ErrorMessage`; it does not silently abort remaining independent cases. Cancellation remains cancellation and is propagated immediately.

## Non-compensable aggregation

The report records:

- total, passed, and failed cases;
- minimum claim recall across evaluated cases;
- minimum citation validity across evaluated cases;
- minimum groundedness across evaluated cases;
- each individual case result or execution error.

A run passes only when:

1. no case failed or errored;
2. minimum `ClaimRecall == 1.0`;
3. minimum `CitationValidity == 1.0`;
4. minimum `Groundedness == 1.0`.

No average, weighted score, or strong result from another case may compensate for a failed hard gate.

## JSON artifact

`GenerationBenchmarkJson.Serialize` emits a human-readable JSON representation of the complete report using camel-case property names.

The artifact is intended for candidate comparison and audit. At minimum, retain it together with the exact git commit, catalog version, runtime, model identifier, model configuration, seed, command used to execute the benchmark, exit code, and any execution deviation.

## Promotion rule

A candidate model/provider must not be promoted because of anecdotal output quality or aggregate preference alone. Promotion requires a reproducible benchmark artifact for the exact candidate configuration and all non-compensable gates passing on the approved catalog.

Latency, cost, availability, privacy, and operational constraints are separate gates and must be measured before production promotion; they are not implemented or inferred by this runner.

## Minimum acceptance evidence

1. catalog version mismatch is rejected before model execution;
2. duplicate fixture ids are rejected;
3. case execution order is deterministic;
4. one candidate exception is recorded and independent cases continue;
5. a failed citation/groundedness gate cannot be averaged away;
6. JSON contains reproducibility metadata and case results;
7. prior evaluation, generation-boundary, context, citation, retrieval, ingestion, persistence, domain, and architecture gates remain green.

Missing runtime, runner, local checkout, provider, or credentials is not PASS. It is `BLOCKED` or `NOT_TESTED` according to observed evidence.
