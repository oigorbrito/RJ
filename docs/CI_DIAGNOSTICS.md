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

On workflow run `33889992199` for commit `b91d9fb03e0ec8edfe0cf017aaa11870ff1bf78f`:

- `runner-smoke` completed with conclusion `failure`;
- `runner-smoke` returned `steps = null` and `logs_url = null`;
- `build-test` completed as `skipped` because it depends on `runner-smoke`;
- the prior failing job log endpoint also returned no stored job log.

The smoke job has no repository checkout, .NET setup, database service, restore, build, or test dependency. Therefore the observed failure is classified as an external GitHub Actions runner/provisioning blocker, not a repository-code failure.

## Classification

`RJ-BLK-002`

- type: GitHub Actions hosted-runner/execution environment;
- blocked operation: any CI step execution;
- observed evidence: runner-only job fails before producing steps/logs;
- impact: remote compilation, tests, PostgreSQL integration, HTTP contract tests, and CLI execution remain unverified;
- work that can continue: repository edits, static review, local execution when available;
- objective unblock condition: `runner-smoke` executes its diagnostics step and produces normal step/log metadata.

## Operational rule

Do not add retries, `continue-on-error`, ignored exit codes, or fallback PASS behavior to mask this blocker. Once `runner-smoke` executes normally, `build-test` remains the authoritative remote restore/build/test gate.
