# Benchmark CLI harness

## Scope

`RJ.BenchmarkCli` is the executable harness for the generation benchmark protocol. It does not integrate an external language-model provider.

The only model currently available through the command line is `harness-selftest-v1`. It is a deterministic test double whose sole purpose is to verify the benchmark execution path, report persistence, abstention handling, citation gates, catalog loading, and process exit codes. It must not be treated as a promoted generation candidate.

## Catalog selection

When no external catalog arguments are supplied, the executable loads `ApprovedGenerationBenchmarkCatalog.Version = generation-benchmark-v1` from code.

The self-test catalog contains both:

- a normal claim that must be reproduced with its exact evidence citation;
- an explicit abstention case.

An external versioned catalog may instead be supplied with:

```text
--catalog <json-path>
--catalog-sha256 <64-hex-sha256>
```

The arguments are valid only as a pair. The exact UTF-8 file is hashed before deserialization, and checksum mismatch is an execution error. The external document's `catalogVersion` becomes the version recorded in `GenerationBenchmarkMetadata`.

External catalog structure and provenance gates are defined in `docs/EXTERNAL_BENCHMARK_CATALOG.md`.

## Required arguments

```text
--git-commit <commit>
--runtime <runtime-description>
--model-id harness-selftest-v1
--model-config <configuration-description>
--seed <seed-description>
--output <json-path>
```

Arguments are strict name/value pairs. Unknown names, duplicates, missing values, empty values, malformed catalog checksums, or an unpaired catalog path/checksum are usage errors.

Self-test example:

```text
dotnet run --project src/RJ.BenchmarkCli -- \
  --git-commit <git-sha> \
  --runtime ".NET 10" \
  --model-id harness-selftest-v1 \
  --model-config "deterministic-self-test" \
  --seed "0" \
  --output artifacts/generation-benchmark.json
```

External-catalog harness example:

```text
dotnet run --project src/RJ.BenchmarkCli -- \
  --git-commit <git-sha> \
  --runtime ".NET 10" \
  --model-id harness-selftest-v1 \
  --model-config "deterministic-self-test" \
  --seed "0" \
  --catalog benchmark/catalog.json \
  --catalog-sha256 <exact-file-sha256> \
  --output artifacts/generation-benchmark.json
```

Using the self-test model with an external legal catalog validates the harness path only; it is not a model-quality benchmark and may legitimately return hard-gate failure.

## Exit codes

- `0`: every benchmark case passed all non-compensable gates and the JSON report was written;
- `1`: benchmark execution completed and at least one hard gate failed; the JSON report is still written;
- `2`: command-line/configuration/catalog/execution failure prevented a trustworthy benchmark result.

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
4. external catalog checksum mismatch produces exit code `2` before creating a report;
5. external catalog path/checksum must be supplied together;
6. report replacement leaves only the final target file;
7. duplicate command-line argument names are rejected;
8. the CLI project may reference only `RJ.Application`;
9. prior benchmark, evaluator, generation, retrieval, ingestion, persistence, domain, and architecture gates remain unchanged.

A missing runtime, unavailable runner, unavailable local checkout, absent legal corpus, or absent execution log is not PASS. It remains `BLOCKED` or `NOT_TESTED` based on the observed evidence.
