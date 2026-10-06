# Reproducibility record

Reproducibility guidance is subject to `docs/engineering/empirical-harness.md`.

When applicable to a computational claim, preserve enough information to understand and rerun the study: exact git commit, worktree state, runtime/SDK, relevant dependency versions, operating environment, dataset/query/schema versions and hashes, seed, configuration, executed commands, observation or test count, duration, exit code, raw outputs/logs, protocol deviations, and observed side effects.

Do not infer PASS from missing execution. External dependencies, unavailable data, unavailable credentials, or environment failures must be recorded explicitly and separated from failures of the evaluated system.

The repository baseline targets .NET 10 LTS and C# 14. The SDK resolver starts at 10.0.100 and rolls forward to the latest installed .NET 10 feature band. CI installs the latest available 10.0.x SDK before restore/build/test.
