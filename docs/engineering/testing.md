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
