# Benchmark CLI harness

## Scope

`RJ.BenchmarkCli` is the executable harness for the generation benchmark protocol. It does not integrate an external language-model provider.

The only model currently available through the command line is `harness-selftest-v1`. It is a deterministic test double whose sole purpose is to verify the benchmark execution path, report persistence, abstention handling, citation gates, catalog loading, and process exit codes. It must not be treated as a promoted generation candidate.

## Catalog selection

When no external catalog arguments are supplied, the executable loads `ApprovedGenerationBenchmarkCatalog.Version = generation-benchmark-v1` from code.

Each successful or gate-failed benchmark execution writes two artifacts:

- the benchmark report JSON at `--output`;
- a reproducibility sidecar at the same path with `.run-manifest.json` appended.

The run manifest records the exact CLI command, git commit, runtime, catalog version, model identity, model configuration, seed, output path, report checksum, exit code, and pass/fail result. It also carries `manifestVersion = benchmark-run-manifest-v1`.

The checksum is the SHA-256 of the exact bytes written to the report file. It proves artifact integrity and report/manifest correspondence, not authorship or cryptographic authenticity.

The manifest can be verified deterministically with:

```text
dotnet run --project src/RJ.BenchmarkCli -- manifest verify artifacts/generation-benchmark.run-manifest.json
```

Verification fails closed if the manifest JSON is invalid, required fields are missing, the version is wrong, the report is absent, the report checksum does not match, or the manifest points at a report path that escapes the manifest directory through a relative traversal.

The run manifest is intended as reproducibility metadata, not as factual evidence.

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

The benchmark CLI currently accepts two explicit model ids:

- `harness-selftest-v1` for the original harness self-test;
- `oab-bench-demo-v1` for the demo OAB-Bench flow when `RJ_LEGAL_DEMO_CORPUS` points at the external read-only corpus checkout.

## Demo corpus mode

When `RJ_LEGAL_DEMO_CORPUS` is set to a read-only `oab-bench` checkout, the CLI can build an external demo catalog from:

- `data\oab_bench\question.jsonl`
- `data\oab_bench\reference_answer\guidelines.jsonl`
- `data\judge_prompts.jsonl`

This mode is for `DEMO_LEGAL_VALIDATION` only. It records provenance and SHA-256 values for the exact artifacts used, but it does not use `model_answer` files as gold truth and it does not close the real-corpus blocker.

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

## Exit codes and diagnostics

- `0`: every benchmark case passed all non-compensable gates and the JSON report was written;
- `1`: benchmark execution completed and at least one hard gate failed; the JSON report is still written;
- `2`: command-line/configuration/catalog/execution failure prevented a trustworthy benchmark result.

Cancellation is propagated rather than converted to an ordinary exit result.

Argument-validation failures retain the exception type and current static parser message. Those messages contain option names or fixed harness text, not catalog contents, local paths, hashes, or generated evidence.

Non-argument execution failures emit only the exception type plus the fixed text `Benchmark execution failed.` The originating exception message is not copied to stderr. This prevents filesystem paths, catalog paths, expected/observed hashes, parser payload fragments, and other provider/runtime details from silently becoming part of the CLI diagnostic contract.

The external-catalog checksum mismatch exception itself is also fixed and no longer embeds expected or observed hash values. Exact hashes remain inputs/provenance data and are not needed for the process-level error message.

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
5. checksum-mismatch diagnostics do not expose catalog paths or expected hashes;
6. missing-catalog filesystem diagnostics do not expose local paths or file names;
7. external catalog path/checksum must be supplied together;
8. report replacement leaves only the final target file;
9. duplicate command-line argument names are rejected;
10. the CLI project may reference only `RJ.Application`;
11. prior benchmark, evaluator, generation, retrieval, ingestion, persistence, domain, and architecture gates remain unchanged.
12. run manifest verification fails closed for malformed JSON, missing fields, checksum mismatch, missing report, and relative path escape.

A missing runtime, unavailable runner, unavailable local checkout, absent legal corpus, or absent execution log is not PASS. It remains `BLOCKED` or `NOT_TESTED` based on the observed evidence.
