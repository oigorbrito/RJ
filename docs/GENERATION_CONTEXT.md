# Generation context protocol

## Scope

This phase constructs a deterministic context envelope for downstream generation. No language model is invoked and no generated answer is produced.

## Input contract

`GenerationContextBuilder` accepts only `LegalEvidenceHit` instances. Arbitrary document text, summaries, search snippets without source positions, or generated content are not accepted as context inputs.

All evidence must belong to the requested `caseId`. Mixed-case evidence fails closed with `InvalidOperationException`.

## Ordering

Evidence is ordered deterministically by:

1. `Rank` descending;
2. `DocumentId` ordinal ascending;
3. `Position.StartOffset` ascending;
4. `Position.Length` ascending.

## Deduplication

Evidence is deduplicated by the tuple:

`(DocumentId, ContentSha256, StartOffset, Length)`.

Only the first item in deterministic order is retained.

## Budget

The context budget is measured in UTF-16 code units, matching .NET `string.Length` and the citation offset protocol.

A context item is atomic. If its complete excerpt does not fit the remaining budget, the item is skipped. Excerpts are never truncated because truncation would create a context fragment whose citation no longer matches the original evidence unit.

The API default budget is `12000` UTF-16 code units. The requested budget must be positive.

## Output

`GenerationContext` contains:

- `caseId`;
- normalized query;
- requested character budget;
- actually used characters;
- ordered `GenerationContextItem` entries.

Each item preserves `documentId`, `sourceName`, `contentSha256`, exact excerpt, source position, and retrieval rank.

## HTTP boundary

`GET /api/cases/{caseId}/generation-context?q=<query>&limit=<retrieval-limit>&budget=<character-budget>`

Defaults:

- retrieval `limit`: `20`;
- context `budget`: `12000`.

Invalid transport parameters return `400`. Their current validation messages are static application validation text and do not interpolate case identifiers, query text, document identifiers, or evidence content.

Evidence invariant violations return `422` rather than constructing a partially trusted context. The HTTP boundary does not copy the originating `InvalidOperationException.Message`; it returns the fixed message `Generation context could not be constructed from the retrieved evidence.` so internal invariant wording, case/document identifiers, and legal evidence content cannot become part of the public error contract.

## Minimum acceptance evidence

1. no non-`LegalEvidenceHit` content can enter the builder API;
2. cross-case evidence fails closed;
3. duplicate evidence is emitted once;
4. ordering is deterministic;
5. used character count equals the sum of included excerpts;
6. no excerpt is truncated to fit the budget;
7. generation-context invariant failures map to `422` with a fixed sanitized response rather than exposing internal exception details or legal evidence values;
8. invalid request parameters remain `400` and do not require sanitization while their messages remain static and non-interpolating;
9. prior ingestion, retrieval, citation, persistence, domain, and architecture gates remain green.

Missing runtime, database, runner, or connection string is not PASS; it is `BLOCKED` or `NOT_TESTED` according to observed execution evidence.
