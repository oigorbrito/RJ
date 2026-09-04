# Benchmark CLI harness

## Scope

`RJ.BenchmarkCli` is the executable harness for the generation benchmark protocol. It does not integrate an external language-model provider.

The only model currently available through the command line is `harness-selftest-v1`. It is a deterministic test double whose sole purpose is to verify the benchmark execution path, report persistence, abstention handling, citation gates, and process exit codes. It must not be treated as a promoted generation candidate.

## Approved catalog

The executable loads `ApprovedGenerationBenchmarkCatalog.Version = generation-benchmark-v1` from code.

The self-test catalog contains both:

- a normal claim that must be reproduced with its exact evidence citation;
- an explicit abstention case.

Catalog version is copied into `GenerationBenchmarkMetadata` and must match the catalog supplied to the runner.

## Required arguments

```text
--git-commit <commit>
--runtime <runtime-description>
--model-id harness-selftest-v1
--model-config <configuration-description>
--seed <seed-description>
--output <json-path>
```

Arguments are strict name/value pairs. Unknown names, duplicates, missing values, or empty values are usage errors.

Example:

```text
dotnet run --project src/RJ.BenchmarkCli -- \
  --git-commit <git-sha> \
  --runtime ".NET 10" \
  --model-id harness-selftest-v1 \
  --model-config "deterministic-self-test" \
  --seed "0" \
  --output artifacts/generation-benchmark.json
```

## Exit codes

- `0`: every benchmark case passed all non-compensable gates and the JSON report was written;
- `1`: benchmark execution completed and at least one hard gate failed; the JSON report is still written;
- `2`: command-line/configuration/execution failure prevented a trustworthy benchmark result.

Cancellation is propagated rather than converted to an ordinary exit result.

## Report persistence

The JSON payload is produced by `GenerationBenchmarkJson`.

`AtomicTextFileWriter` writes to a uniquely named temporary file in the destination directory and then replaces the target by a same-directory move. Temporary files are removed in `finally` if replacement does not complete.

The same-directory strategy avoids exposing a partially written target file under the normal filesystem rename/replace semantics. Exact atomicity guarantees remain dependent on the host filesystem; storage systems that do not provide atomic same-filesystem rename semantics must not be claimed as providing atomic report replacement without separate verification.

## Architecture

`RJ.BenchmarkCli` references only `RJ.Application`. An architecture test enforces this dependency rule.

The CLI does not reference PostgreSQL infrastructure, the web API, Domain directly, or any provider SDK.

## Minimum acceptance evidence

The focal test set proves:

1. the deterministic self-test returns exit code `0` and writes a passing report;
2. a model output that violates hard citation gates produces exit code `1` after writing a failing report;
3. an unavailable model identifier produces exit code `2` without creating a benchmark report;
4. report replacement leaves only the final target file;
5. duplicate command-line argument names are rejected;
6. the CLI project may reference only `RJ.Application`;
7. prior benchmark, evaluator, generation, retrieval, ingestion, persistence, domain, and architecture gates remain unchanged.

A missing runtime, unavailable runner, unavailable local checkout, or absent execution log is not PASS. It remains `BLOCKED` or `NOT_TESTED` based on the observed evidence.
