# Reproducibility record

When applicable, record: git commit, runtime/SDK, relevant dependency versions, dataset/query/schema versions, seed, configuration, executed commands, test count, duration, exit code, raw logs, protocol deviations, and observed side effects.

The repository baseline targets .NET 10 LTS and C# 14. The SDK resolver starts at 10.0.100 and rolls forward to the latest installed .NET 10 feature band. CI installs the latest available 10.0.x SDK before restore/build/test.
