# Case-scoped read API

## Contract

All document retrieval routes are scoped by `caseId` in the URL. There is no unscoped document-list or full-text-search endpoint.

- `GET /api/cases/{caseId}/documents`
- `GET /api/cases/{caseId}/documents/{documentId}`
- `GET /api/cases/{caseId}/search?q={query}&limit={1..100}`

Search defaults to `limit=20`. Invalid identifiers, blank queries, and limits outside `1..100` return HTTP 400. A missing document returns HTTP 404.

## Provenance fields

Document payloads expose:

- `caseId`
- `documentId`
- `sourceName`
- `rawContent`
- `content`
- `contentSha256`

`rawContent` is the preserved ingested evidence representation. `content` is the normalized operational representation. `contentSha256` is derived from UTF-8 bytes of `rawContent` during ingestion.

Search hits additionally expose deterministic PostgreSQL FTS `rank`. Search remains PostgreSQL FTS only and is not semantic/vector retrieval.

## Isolation rule

`caseId` is converted to the domain `LegalCaseId` in the application layer before the repository/search port is invoked. PostgreSQL reader and search SQL both include mandatory `case_id` predicates. Cross-case results are therefore outside the retrieval contract and covered by integration tests.

## Minimum acceptance evidence

1. transport identifiers are converted before repository access;
2. invalid case identifiers fail before repository execution;
3. list and get are case-scoped;
4. search is case-scoped and respects the requested limit;
5. read/search payloads contain evidence identity and raw/normalized provenance;
6. missing documents return 404 and invalid inputs return 400;
7. prior architecture, ingestion, persistence, and retrieval gates remain green.

Unavailable runner, runtime, or PostgreSQL is not PASS.
