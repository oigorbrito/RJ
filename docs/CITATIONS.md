# Citable retrieval protocol

## Purpose

Retrieval evidence must be independently verifiable against the stored raw source text before it can be used as a citation-bearing unit.

## Evidence contract

`LegalEvidenceHit` contains:

- `caseId`
- `documentId`
- `sourceName`
- `contentSha256`
- `excerpt`
- `position.startOffset`
- `position.length`
- `rank`

Offsets are zero-based UTF-16 string offsets into the persisted `rawContent` representation used by .NET. A citation is valid only when:

```text
rawContent.Substring(startOffset, length) == excerpt
```

`contentSha256` remains the SHA-256 digest of the UTF-8 bytes of the complete raw content and identifies the source representation against which the excerpt is verified.

## Retrieval policy

PostgreSQL FTS remains the candidate-document baseline. Citation localization is a separate fail-closed step over raw evidence.

Localization first attempts the trimmed query as an ordinal case-insensitive substring. When the exact query is not present, it tries query tokens in descending token length and uses the first deterministically ordered token that is present in raw content. The excerpt contains up to 160 UTF-16 code units of context on each side of the located text, clipped to source bounds.

If an FTS hit cannot be localized in raw content, it is omitted from the citation-bearing evidence response. The system does not invent offsets or cite normalized text as if it were raw evidence.

## API

`GET /api/cases/{caseId}/evidence?q={query}&limit={1..100}`

The endpoint is case-scoped by construction and returns only `LegalEvidenceHit` objects. The existing `/search` endpoint remains unchanged and continues to expose document-level retrieval hits.

## Minimum acceptance evidence

1. every returned source position is inside the raw source bounds;
2. every returned excerpt exactly equals the raw substring at its declared position;
3. citation-bearing retrieval never crosses `caseId` boundaries;
4. an unlocalizable FTS hit is omitted rather than assigned a fabricated position;
5. `contentSha256` accompanies each evidence unit;
6. prior ingestion, persistence, retrieval, domain, and architecture gates remain green.

Unavailable runtime, PostgreSQL, CI runner, or connection string is not PASS and must remain `BLOCKED` or `NOT_TESTED` according to observed evidence.
