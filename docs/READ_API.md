# Case-scoped read API

## Contract

All document retrieval routes are scoped by `caseId` in the URL. There is no unscoped document-list or full-text-search endpoint.

- `GET /api/cases/{caseId}/documents?page={>=1}&pageSize={1..100}`
- `GET /api/cases/{caseId}/documents/{documentId}`
- `GET /api/cases/{caseId}/search?q={query}&limit={1..100}`
- `GET /api/cases/{caseId}/evidence?q={query}&limit={1..100}`

Document listing defaults to `page=1&pageSize=20`. Search defaults to `limit=20`. Invalid identifiers, blank queries, invalid page values, and limits outside their documented ranges return HTTP 400 with `{ "code": "invalid_request", "error": "..." }`. A missing document returns HTTP 404.

The read routes use one boundary contract for validation failures. Document detail and evidence retrieval are routed through `ReadEndpoint` together with list/search, so they no longer return anonymous error payloads without a stable code.

Current `ArgumentException` validation messages are static application/domain validation text and do not interpolate case identifiers, document identifiers, query text, or legal evidence content. They remain part of the current 400 response only while that property holds; infrastructure/internal exceptions are not converted into read `400` responses.

## Collection payload minimization

Collection/list and search endpoints do not expose full source text.

`GET /documents` returns a page envelope containing only:

- `caseId`;
- `documentId`;
- `sourceName`;
- `contentSha256`.

`GET /search` returns the same evidence identity plus deterministic PostgreSQL FTS `rank`.

Neither collection response contains `rawContent` or normalized `content`. This prevents large source bodies from being serialized accidentally through browse/search surfaces.

Pagination is applied by PostgreSQL using mandatory case scope, `ORDER BY document_id ASC`, `OFFSET`, and `LIMIT`; it is not an in-memory slice after loading an unbounded case.

## Explicit evidence/detail surfaces

`GET /documents/{documentId}` remains the explicit full-document detail surface and exposes exactly `caseId`, `documentId`, `sourceName`, `rawContent`, `content`, and `contentSha256` through the API-owned `LegalDocumentDetail` projection.

The endpoint does not serialize the Application-layer `LegalDocumentSnapshot` directly. This is a contract boundary: adding a field to an internal retrieval snapshot cannot silently add that field to the public HTTP payload. Raw and normalized content remain intentional on this explicit detail route because `rawContent` is the preserved evidence representation while `content` is the deterministic operational normalization; removing either would be a contract change not supported by the current requirements.

`GET /evidence` remains the explicit citable retrieval surface. It exposes exactly `caseId`, `documentId`, `sourceName`, `contentSha256`, `excerpt`, `position`, and `rank` through the API-owned `LegalEvidenceResult` projection. `position` is itself an API-owned `EvidencePosition` containing only `startOffset` and `length`.

The evidence endpoint does not serialize Application-layer `LegalEvidenceHit` or `SourcePosition` directly. This prevents later internal fields or derived properties from silently becoming part of the HTTP contract while preserving the current citation semantics. The excerpt remains bounded evidence text tied to the source identity and exact source offsets; search itself is not an evidence-text API.

`rawContent` is the preserved ingested evidence representation. `content` is the normalized operational representation. `contentSha256` is derived from UTF-8 bytes of `rawContent` during ingestion.

Search remains PostgreSQL FTS only and is not semantic/vector retrieval.

## Isolation and ordering

`caseId` is converted to the domain `LegalCaseId` in the application layer before repository/search access. PostgreSQL reader and search SQL include mandatory `case_id` predicates.

List ordering is deterministic by `document_id ASC`. Search ordering remains rank descending with `document_id ASC` as tie-break.

## Minimum acceptance evidence

1. list pagination is validated before repository execution;
2. page/page-size are converted to deterministic offset/limit;
3. PostgreSQL applies case scope, deterministic ordering, offset, and limit;
4. list payload omits raw and normalized content;
5. search payload omits raw and normalized content while retaining evidence identity and rank;
6. document detail uses an API-owned projection with exactly the six documented fields instead of serializing `LegalDocumentSnapshot` directly;
7. evidence uses API-owned projections with exactly the seven documented evidence fields and source position limited to `startOffset`/`length`, instead of serializing `LegalEvidenceHit` or `SourcePosition` directly;
8. `/evidence` remains the explicit bounded excerpt surface;
9. missing documents return 404;
10. invalid inputs across list, document detail, search, and evidence return 400 with stable code `invalid_request`;
11. read validation responses do not convert infrastructure/internal failures into client errors;
12. prior architecture, ingestion, persistence, retrieval, citation, and generation gates remain unchanged.

Unavailable runner, runtime, or PostgreSQL is not PASS. It remains `BLOCKED` or `NOT_TESTED` according to observed execution evidence.
