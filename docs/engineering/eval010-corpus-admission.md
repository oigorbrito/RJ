# EVAL-010 frozen real-corpus admission

Status: executable admission harness implemented; real 30–50 case corpus remains BLOCKED until authorized source/oracle/review artifacts are supplied and verified.

## Purpose

EVAL-010 qualifies a frozen real legal-process corpus for later retrieval/generation experiments. It does not itself select a retrieval treatment, model/provider, threshold, or production configuration.

The protocol intentionally separates three artifact roles per case:

1. legal source artifact;
2. structured oracle artifact;
3. independent oracle-review artifact.

Source evidence is the only factual material eligible to enter generation context. Oracle and review artifacts are evaluation-only and must remain isolated from factual context construction.

## Frozen corpus contract

`Eval010CorpusManifest` requires:

- format `rjudi-eval010-corpus-v1`;
- explicit corpus version;
- frozen timestamp;
- 30–50 cases, matching the pre-specified RJudi EVAL-010 requirement;
- unique case IDs;
- unique normalized 20-digit CNJs;
- immutable references and SHA-256 values for source, oracle and oracle-review artifacts;
- three distinct artifact references per case;
- oracle author identifier;
- distinct oracle reviewer identifier;
- oracle review timestamp.

The exact manifest bytes are also frozen by an externally supplied SHA-256. A manifest-byte change invalidates the prior admission result even when internal artifact hashes still happen to match.

Author/reviewer identifiers may be project-controlled pseudonymous IDs; the contract requires only that the review was attributed to a different identifier from the oracle author.

No corpus-size result is inferred from data after execution. A corpus outside the pre-specified 30–50 range is rejected before benchmark execution.

## Admission

`Eval010CorpusAdmissionService` reads each immutable artifact through `IBenchmarkArtifactReader` and checks exact SHA-256 values.

A case passes admission only when:

- source hash matches;
- oracle hash matches;
- oracle-review hash matches;
- no artifact-read/hash failure remains.

A corpus passes only when every case passes. Failed cases cannot be averaged away.

## Oracle isolation

When an `ExternalGenerationBenchmarkCatalog` is provided, `ValidateOracleIsolation` additionally requires:

- benchmark case count equals frozen corpus case count;
- every benchmark case is in the frozen manifest;
- benchmark oracle reference/hash exactly match the frozen manifest;
- no generation-context evidence item references any oracle artifact;
- no generation-context evidence item references any oracle-review artifact.

The oracle bytes are read only for integrity verification. This admission path does not concatenate, inject or otherwise expose oracle/review bytes to generation context.

This is a hard gate. An oracle-isolation violation is a FAIL, not a metric tradeoff.

## External verifier

`tools/RJ.Eval010CorpusVerifier` verifies a corpus materialized outside the repository.

Usage:

```powershell
dotnet run --project tools/RJ.Eval010CorpusVerifier/RJ.Eval010CorpusVerifier.csproj -- `
  <manifest.json> `
  <manifest-sha256> `
  <artifact-root> `
  [benchmark-catalog.json]
```

Before parsing the manifest, the verifier hashes the exact manifest bytes and requires equality with `<manifest-sha256>`.

Artifact references are resolved under the supplied root. References that escape that root are rejected.

Exit codes:

- `0`: all supplied corpus admission gates pass;
- `2`: invocation/precondition error;
- `3`: manifest digest, manifest structure, artifact hash, catalog or oracle-isolation failure.

On success the output envelope records the exact manifest SHA-256 plus the per-case admission report. The tool does not promote any retrieval/generation treatment.

## Canonical gate

```powershell
.\scripts\test-rjudi-wave-g.ps1
```

The gate first runs work independent of the external corpus:

1. solution build;
2. EVAL-010 and existing corpus-admission contract tests;
3. `git diff --check`.

Only after those steps does it require:

- `RJ_EVAL010_MANIFEST_PATH`;
- `RJ_EVAL010_MANIFEST_SHA256`;
- `RJ_EVAL010_ARTIFACT_ROOT`.

Optional:

- `RJ_EVAL010_BENCHMARK_CATALOG_PATH` to activate the cross-artifact oracle-isolation gate.

If the real corpus is absent, the gate exits `2` as `RJ-BLK-003` after the executable local contract steps.

## Required real-corpus handoff

To unblock EVAL-010, supply an authorized immutable corpus containing 30–50 cases with, per case:

- source artifact + exact SHA-256;
- structured oracle + exact SHA-256;
- independent oracle-review artifact + exact SHA-256;
- case ID and CNJ;
- oracle author/reviewer IDs;
- review timestamp.

Also supply the SHA-256 of the exact frozen manifest bytes. Any manifest-byte or artifact-byte change invalidates the affected prior admission evidence and requires a new frozen digest/version.

## Explicit nonclaims

This wave does not demonstrate:

- existence of the required real 30–50 case corpus;
- quality/correctness of any not-yet-supplied oracle;
- cross-tribunal representativeness;
- retrieval superiority;
- model/provider superiority;
- production generalization;
- numeric acceptance thresholds beyond already pre-specified hard gates;
- any weighted composite score.

When treatment measurements later fail to distinguish candidates without an unambiguous pre-specified decision, the required result remains `NO_CLEAR_WINNER`.
