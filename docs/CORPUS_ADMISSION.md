# Corpus admission protocol

## Scope

Corpus admission verifies an external benchmark catalog and its referenced local source/oracle artifacts before any generation model is involved.

The command is independent of `IGenerationModel` and produces a separate admission report. A benchmark catalog must not be treated as admitted merely because its JSON structure parses successfully.

## Command

```text
dotnet run --project src/RJ.BenchmarkCli -- catalog validate \
  --catalog <catalog.json> \
  --catalog-sha256 <64-hex-sha256> \
  --output <admission-report.json>
```

Exit codes:

- `0`: all admission gates passed and the report was written;
- `1`: catalog structure was trustworthy enough to evaluate, but one or more source/oracle admission gates failed; the report was still written;
- `2`: command usage, catalog checksum, parsing, or execution failed before a trustworthy admission result could be produced.

Cancellation is propagated.

## Artifact resolution

`sourceReference` and `oracleReference` are local paths relative to the directory containing the catalog file.

Absolute paths and references escaping the catalog directory through `..` are rejected. This keeps a catalog self-contained and prevents admission from silently reading unrelated filesystem locations.

## Catalog gate

Before artifact admission:

1. the exact catalog file bytes are hashed with SHA-256;
2. the observed hash must exactly match `--catalog-sha256`;
3. the versioned external catalog structure and oracle citation invariants must pass `ExternalGenerationBenchmarkCatalog.ToBenchmarkCatalog()`.

## Oracle artifact gate

For every case:

1. `oracleReference` must resolve successfully;
2. SHA-256 is calculated over the exact oracle artifact bytes;
3. the observed value must equal `oracleSha256`.

This phase verifies oracle artifact identity. It does not infer or repair oracle content.

## Source artifact gate

For every evidence item, admission requires all of the following:

1. `sourceReference` resolves successfully;
2. SHA-256 over exact source bytes equals `sourceSha256`;
3. the source bytes decode as strict UTF-8 with invalid byte sequences rejected;
4. decoded `.NET string.Length` equals `sourceLength`;
5. `(startOffset, length)` is fully inside the decoded source;
6. `source.Substring(startOffset, length) == excerpt` using ordinal equality.

No newline rewriting, Unicode normalization, BOM removal, whitespace normalization, OCR, summarization, or fuzzy matching is performed during admission. Offsets remain the UTF-16 convention defined by the citation protocol.

## Report

`CorpusAdmissionReport` records:

- catalog version and verified catalog SHA-256;
- total cases;
- total and verified evidence items;
- verified oracle count;
- overall `passed`;
- per-case pass/fail state;
- explicit failures with artifact reference, gate identifier, and observed reason.

A failure in one case does not erase the evidence collected for other independently verifiable cases.

## Non-compensable admission gates

Admission is fail-closed. A case passes only when its oracle is verified, every evidence item is verified, and no admission failure remains. The catalog passes only when every case passes.

Hash success cannot compensate for excerpt mismatch. Valid UTF-8 cannot compensate for a wrong hash. Oracle success cannot compensate for unverifiable source evidence.

## Corpus status

This protocol does not fabricate legal benchmark data. Until authorized real legal sources and reviewed oracle artifacts are supplied with their hashes and references, the real-corpus benchmark remains externally blocked rather than PASS.

## Minimum acceptance evidence

The focal tests cover:

1. complete successful admission;
2. source SHA-256 mismatch;
3. invalid UTF-8 despite a matching source hash;
4. exact excerpt reproduction failure;
5. oracle SHA-256 mismatch;
6. catalog checksum mismatch at the CLI boundary;
7. rejection of artifact path traversal;
8. passing admission report persistence.

Unavailable runtime, runner, local checkout, or absent legal corpus is never converted to PASS.
