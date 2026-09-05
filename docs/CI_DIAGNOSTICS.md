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

### Unblock evidence

On workflow run `33934362365` for commit `42a5c22c9fb7b19b9e890a41ffc0799eba509bad` after the repository transfer to `oigorbrito/RJ`:

- `runner-smoke` completed with conclusion `success` and executed its diagnostics step;
- the hosted runner was Ubuntu 24.04 and Docker was available;
- `build-test` initialized the `postgres:18.6` service container successfully and the service became healthy;
- checkout, .NET 10 setup, restore, and Release build all completed successfully;
- the Release build reported 0 warnings and 0 errors;
- the test step executed 126 tests and returned 112 passed, 14 failed, 0 skipped.

This evidence satisfies the prior unblock condition. Subsequent failures in that run are repository/test failures with executed logs, not runner-provisioning failures.

## Classification

`RJ-BLK-002`: RESOLVED.

- type: GitHub Actions hosted-runner/execution environment;
- prior blocked operation: CI step execution;
- unblock evidence: run `33934362365` executed `runner-smoke`, service-container initialization, restore, build, and tests normally;
- current impact: none as a runner blocker;
- operational consequence: `build-test` is again the authoritative remote PostgreSQL 18.6 restore/build/test gate.

The 14 test failures observed in run `33934362365` are tracked as executable engineering work, not as an external blocker. The follow-up corrections are contained in commit `eff7403d1d5c9fba040ee71f5d81f2fcf4f8869f`.

## Operational rule

Do not add retries, `continue-on-error`, ignored exit codes, or fallback PASS behavior to mask CI failures. A missing execution remains `BLOCKED` or `NOT_TESTED`; a test failure with normal runner/service execution remains `FAIL` until corrected and re-executed.
