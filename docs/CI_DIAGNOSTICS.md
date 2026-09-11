# CI diagnostics

## Scope

This document records the minimum evidence required to classify CI failures without mislabeling unavailable execution as a code failure.

## Diagnostic split

The `ci` workflow contains two jobs:

1. `runner-smoke` uses `ubuntu-latest` and no service container. Its only step prints non-sensitive runner/host diagnostics.
2. `build-test` depends on `runner-smoke` and contains PostgreSQL plus restore/build/test.

This split distinguishes runner provisioning from repository/service execution:

- if `runner-smoke` cannot produce steps or logs, the failure is before repository checkout, .NET setup, PostgreSQL startup, restore, build, or tests;
- if `runner-smoke` passes and `build-test` fails before steps, investigate service-container/runner initialization;
- only failures with executed repository steps may be classified against restore/build/test commands.

## Observed evidence

### Historical provisioning failure

On workflow run `33889992199` for commit `b91d9fb03e0ec8edfe0cf017aaa11870ff1bf78f`:

- `runner-smoke` completed with conclusion `failure`;
- `runner-smoke` returned `steps = null` and `logs_url = null`;
- `build-test` completed as `skipped` because it depends on `runner-smoke`;
- the prior failing job log endpoint also returned no stored job log.

At that point the failure was correctly classified as an external GitHub Actions runner/provisioning blocker rather than a repository-code failure.

### Historical unblock evidence

On workflow run `33934362365` for commit `42a5c22c9fb7b19b9e890a41ffc0799eba509bad` after the repository transfer to `oigorbrito/RJ`:

- `runner-smoke` completed with conclusion `success` and executed its diagnostics step;
- the hosted runner was Ubuntu 24.04 and Docker was available;
- `build-test` initialized the `postgres:18.6` service container successfully and the service became healthy;
- checkout, .NET 10 setup, restore, and Release build all completed successfully;
- the Release build reported 0 warnings and 0 errors;
- the test step executed 126 tests and returned 112 passed, 14 failed, 0 skipped.

This evidence satisfied the prior unblock condition at that point in time. The 14 test failures were executable repository/test failures and were followed by corrective work in commit `eff7403d1d5c9fba040ee71f5d81f2fcf4f8869f`.

### Recurrence on Wave C

On workflow run `34561587219` for Wave C commit `345a2f53863dd0b143bd318b562cca3d6669ef98`:

- `runner-smoke` completed with conclusion `failure`;
- `runner-smoke` returned `steps = null`;
- dependent `build-test` completed as `skipped`;
- no repository checkout, .NET setup, restore, build, PostgreSQL startup or test step executed.

This is the same observable pre-step provisioning pattern as the historical blocker. The earlier successful run does not make a later unexecuted run PASS.

## Current classification

`RJ-BLK-002`: BLOCKED / RECURRENT.

- type: GitHub Actions hosted-runner/execution environment;
- blocked operation: exact-head remote build/test execution;
- current evidence: Wave C run `34561587219` failed before configured runner-smoke steps were exposed;
- current impact: Wave C remote build/test remains NOT_TESTED;
- work that can continue: source review, implementation, test definitions, documentation, local exact-head execution and CI metadata inspection;
- objective unblock condition: a run on the exact Wave C head (or its successor) executes runner-smoke and exposes repository build/test steps or logs.

## Operational rule

Do not add retries, `continue-on-error`, ignored exit codes, or fallback PASS behavior to mask CI failures. A missing execution remains `BLOCKED` or `NOT_TESTED`; a test failure with normal runner/service execution remains `FAIL` until corrected and re-executed.
