# External generation benchmark catalog protocol

## Scope

This protocol defines the file format and validation gates for legal benchmark fixtures loaded without recompiling the application.

It does not declare the current in-process harness self-test fixtures to be legal corpus data. No production/legal benchmark case is fabricated by this repository phase.

## File identity

The supported document format is:

`rj-generation-benchmark-catalog-v1`

Every catalog also carries an independently controlled `catalogVersion` identifying the corpus/oracle revision.

The CLI requires the caller to provide the SHA-256 of the exact UTF-8 JSON file through `--catalog-sha256`. The file is hashed before deserialization. A mismatch aborts execution with the CLI usage/execution error status and no benchmark report is created.

Changing any byte in the catalog therefore requires a new observed checksum. Semantic corpus changes should also advance `catalogVersion`.

## Case contract

Each case requires:

- stable `id`;
- `contextCaseId`;
- query;
- oracle reference;
- oracle SHA-256;
- explicit abstention expectation;
- one or more evidence items;
- expected claims unless abstention is expected.

Duplicate case IDs are rejected by the existing benchmark catalog gate.

## Evidence provenance

Provenance is attached to each evidence item, not to the case as a whole, because one evaluation case may depend on multiple source documents.

Each item requires:

- `documentId`;
- `sourceName`;
- `sourceReference` identifying the independently retrievable source artifact;
- `sourceSha256` identifying the exact source representation used for the fixture;
- `contentSha256` used by the RAG citation contract;
- exact excerpt;
- zero-based UTF-16 `startOffset`;
- `length`;
- complete `sourceLength`;
- retrieval rank recorded by the fixture.

For catalog v1, `sourceSha256` must equal `contentSha256`. This binds the benchmark source provenance to the same raw-evidence identity used by generation citations.

`excerpt.Length` must equal the cited position length, and the position must be fully contained in `sourceLength`.

## Oracle provenance

Each case requires `oracleReference` and `oracleSha256`.

These fields identify the independently reviewable oracle artifact/revision from which expected claims and abstention decisions were derived. The loader validates their presence and hash syntax; it does not claim to have fetched or independently reviewed the external oracle artifact.

For non-abstention cases, every expected claim must have at least one citation. Every oracle citation must exactly match a context evidence item by:

- `documentId`;
- `contentSha256`;
- `startOffset`;
- `length`.

The loader fails closed when an oracle cites evidence outside the supplied context.

For abstention cases, expected claims must be empty.

## CLI

External catalog execution adds two arguments to the existing benchmark CLI:

- `--catalog <path>`
- `--catalog-sha256 <64-hex-sha256>`

They are optional only as a pair. Omitting both preserves the deterministic harness self-test catalog. Supplying only one is invalid.

The external catalog's own `catalogVersion` is propagated into benchmark reproducibility metadata; it is not replaced by the self-test catalog version.

## Corpus admission gate

A legal fixture must not be admitted merely because it is syntactically valid. Before an external corpus is treated as benchmark evidence, each case requires independently established:

1. source artifact identity and exact SHA-256;
2. authorization/right to use and retain the source in the evaluation process;
3. oracle artifact identity and exact SHA-256;
4. documented oracle construction/review procedure;
5. citation offsets reproduced against the exact source representation;
6. explicit abstention decision where evidence is insufficient;
7. catalog version and complete file checksum.

Absence of those facts is `NOT_TESTED` or `BLOCKED`, never PASS.

## Current state

The repository contains the executable format, validation and tests for external catalogs, but no corpus is labeled as a real legal benchmark in this phase. A real corpus requires external source/oracle artifacts and review evidence. Until those are supplied and admitted under this protocol, comparison of real model candidates against a legal corpus remains blocked by data availability, not by application architecture.
