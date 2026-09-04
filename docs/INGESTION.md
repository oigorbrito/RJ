# Legal document ingestion protocol

## Contract

The ingestion boundary accepts only transport data: `caseId`, `documentId`, `sourceName`, and `rawContent`. The caller does not provide a content hash and cannot declare normalized content.

## Evidence preservation

`rawContent` is the authoritative ingested text representation and is persisted unchanged in `legal_documents.raw_content`. Normalized `content` is a derived operational representation and must not replace raw evidence.

Current normalization is deliberately minimal and deterministic: CRLF and CR line endings are converted to LF. No whitespace collapsing, rewriting, summarization, language-model transformation, or semantic cleanup occurs at ingestion.

SHA-256 is computed by the application from the UTF-8 bytes of `rawContent`. Idempotency and persistence conflict checks therefore derive from system-observed raw evidence rather than a caller-supplied digest.

## Schema

Schema version: `3`.

Existing schema-v1/v2 rows are migrated by copying existing normalized `content` into `raw_content` because no earlier raw representation exists. That migration preserves the best available pre-v3 evidence but does not claim byte-level provenance that was never stored.

## API

`POST /api/legal-documents` accepts:

```json
{
  "caseId": "case-1",
  "documentId": "doc-1",
  "sourceName": "source.txt",
  "rawContent": "original text"
}
```

The API constructs an application command only. Domain identifiers, normalization, hashing, and persistence remain behind the application boundary.

The ingestion HTTP boundary returns:

- `202 Accepted` when ingestion succeeds or is an idempotent no-op;
- `400 Bad Request` with `{ "code": "invalid_request", "error": "..." }` when transport/domain validation rejects the request;
- `409 Conflict` with `{ "code": "evidence_conflict", "error": "..." }` when the same case/document identity conflicts with different evidence or the same case/evidence hash conflicts with a different document identity.

Persistence-specific exceptions are not part of the HTTP contract. Infrastructure signals evidence conflicts through the Application-level `LegalDocumentConflictException`. Unexpected failures are not converted into client errors and continue through the normal server error path.

## Minimum acceptance evidence

1. raw content is preserved exactly;
2. normalized content is derived independently;
3. SHA-256 matches UTF-8 raw content;
4. invalid/blank content is rejected before persistence;
5. PostgreSQL stores both raw and normalized representations;
6. valid ingestion maps to HTTP `202`;
7. validation maps to `400` with stable error code `invalid_request`;
8. evidence conflict maps to `409` with stable error code `evidence_conflict`;
9. unexpected failures are not mislabeled as client errors;
10. prior domain, architecture, persistence, retrieval, benchmark, and corpus-admission gates remain unchanged.

Missing runtime, database, runner, or connection string is not PASS; it is `BLOCKED` or `NOT_TESTED` according to observed execution evidence.
