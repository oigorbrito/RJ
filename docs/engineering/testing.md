# Testing protocol

Use the smallest test set capable of detecting violation of the changed requirement.

For each change, prefer this order:

1. focused test for the changed behavior;
2. related test project or module;
3. repository build/test gate when dependency or composition risk exists.

Run state must be reported as PASS, FAIL, BLOCKED, NOT_TESTED, or NOT_APPLICABLE. Absence of execution is never PASS.

Coverage percentage is diagnostic information only and is not an acceptance criterion.

## Local MVP gate

- Canonical command: `scripts/test-local-mvp.ps1`
- Scope: `LocalRagEvaluationTests`, `ResponseFixtureIngestionTests`, `Wave1CorpusContractTests`, `CorpusAdmissionServiceTests`
- PASS means the scoped `RJ.DomainTests` execution completed with `exit_code=0` and executed tests greater than zero.
- Outside the gate: `OabRulingBrAbRunnerTests`, `OpenAiGenerationModelTests`, PostgreSQL integration, remote CI, OpenAI, and new corpus fixtures.
