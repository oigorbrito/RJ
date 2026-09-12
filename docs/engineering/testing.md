# Testing protocol

Testing guidance is subject to `docs/engineering/empirical-harness.md`. A testing recommendation must not be presented as methodologically required unless its support is identified there as empirical, reproducibility-based, or derived from an explicit methodological criterion.

Use the smallest test set capable of detecting violation of the changed requirement.

For each change, prefer this order:

1. focused test for the changed behavior;
2. related test project or module;
3. repository build/test gate when dependency or composition risk exists.

This ordering is the repository's risk-based testing policy. It is a `PROJECT_DECISION` unless a more specific empirical or reproducibility criterion applies to the evaluated study.

Run state must be reported as PASS, FAIL, BLOCKED, NOT_TESTED, or NOT_APPLICABLE. Absence of execution is never PASS.

Coverage percentage is diagnostic information only and is not an acceptance criterion.

## Local MVP gate

- Gate version: `LOCAL_MVP_GATE_V1`
- Canonical command: `scripts/test-local-mvp.ps1`
- Underlying project: `tests/RJ.DomainTests/RJ.DomainTests.csproj`
- Underlying filter: `LocalRagEvaluationTests`, `ResponseFixtureIngestionTests`, `Wave1CorpusContractTests`, `CorpusAdmissionServiceTests`
- PASS means the scoped `RJ.DomainTests` execution completed with `exit_code=0` and executed tests greater than zero.
- The wrapper is intentionally thin: it resolves the repository root and calls `dotnet test` directly on the project above with the fixed filter.
- Outside the gate: `OabRulingBrAbRunnerTests`, `OpenAiGenerationModelTests`, PostgreSQL integration, remote CI, OpenAI, and new corpus fixtures.

## RJudi MVP wave 2 closure gate

- Gate version: `RJUDI_MVP_WAVE_2_V1`
- Canonical command: `scripts/test-rjudi-mvp-wave-2.ps1`
- Scope: the persistent demo catalog, CNJ lookup, document availability by case, and the negative unknown-CNJ behavior.
- Preconditions: a reachable PostgreSQL instance through `RJ_POSTGRES_CONNECTION`; port `5001` must not already have a listener.
- The gate explicitly runs the database migrator and demo seeder, starts the API from the current checkout, waits for the live endpoint, queries every case declared in `demo-data/processes.json`, verifies each returned `caseId`, verifies at least one persisted document for every declared case, and requires the designated unknown valid CNJ to return `404`.
- The gate records commit, branch, worktree state, .NET environment, dataset SHA-256, fixture count, timestamps, and observations into `.artifacts/rjudi-mvp-wave-2/`.
- PASS means every gate step executed and satisfied its declared condition in that run. A prior failed manual run remains historical evidence and is not reclassified by a later PASS.
- The current count of five demo cases is a `PROJECT_DECISION`; it is not an empirical sample-size claim.
- The API startup wait window is a `PROJECT_DECISION`; it is not an empirically calibrated performance threshold.
- Automating this gate is `DERIVED_FROM_METHOD` only in the narrow sense that it makes the declared closure protocol repeatable and preserves execution evidence; PowerShell, the file layout, and the endpoint names are not methodological requirements.

## RJudi MVP wave 3 live-generation gate

- Gate version: `RJUDI_MVP_WAVE_3_V1`
- Canonical command: `scripts/test-rjudi-mvp-wave-3.ps1`
- Scope: API runtime provider selection, OpenAI execution through `IGenerationModel`, structured generation, citation-boundary enforcement, downstream RJudi validation, and retrieval of the validated summary for the controlled Judit fixture.
- Preconditions: reachable PostgreSQL through `RJ_POSTGRES_CONNECTION`, local `RJ_GENERATION_MODEL`, local `OPENAI_API_KEY`, provider network access, and a free local gate port (default `5002`).
- The API key must never be emitted into gate output or artifacts.
- The gate first builds the repository and runs `RJ.ApiTests`, then migrates/seeds the local database, starts the API from the current checkout with `RJ_GENERATION_PROVIDER=openai`, submits `demo-data/process-summary-openai-case.json`, and requires a validated result containing at least one claim and at least one citation.
- The gate records commit, branch, worktree state, .NET environment, configured model identifier, fixture SHA-256, timestamps, and non-secret observations into `.artifacts/rjudi-mvp-wave-3/`.
- Missing provider credentials or unavailable provider/network access are `BLOCKED`, not `PASS` and not evidence of model failure.
- A PASS demonstrates only the scoped live path for the controlled fixture and configured environment. It does not establish comparative model superiority, production legal accuracy, or generalization to other corpora.
- The specific provider, model identifier, fixture, port, and startup window are `PROJECT_DECISION` values. Requiring an executed, preserved gate before a live-generation claim is treated as `DERIVED_FROM_METHOD` under the repository reproducibility policy.
