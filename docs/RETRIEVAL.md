# Retrieval baseline

## Contract

Retrieval is case-scoped. No retrieval operation may return a document from another legal case.

The deterministic baseline has two read paths:

1. identity lookup by `(case_id, document_id)`;
2. PostgreSQL full-text search over `source_name + content`, using the explicit `portuguese` text-search configuration.

The full-text projection is a stored generated `tsvector` column backed by a GIN index. Ranking uses `ts_rank_cd`; ties are broken by `document_id ASC` so result order is deterministic for equal ranks.

## Query policy

`PostgresLegalDocumentSearch`:

- rejects empty queries;
- requires `limit` between 1 and 100;
- always applies `case_id` before accepting a hit;
- uses `websearch_to_tsquery('portuguese', ...)` for user-facing textual queries;
- does not use embeddings, vector search, reranking, LLMs, or graph retrieval.

Those components remain outside the production baseline until a measured requirement justifies promotion.

## Schema

Schema version: `2`.

The schema adds `search_vector` as a stored generated column and `ix_legal_documents_search_vector` as a GIN index. PostgreSQL documents GIN as the preferred full-text search index type for `tsvector` workloads.

## Minimum evidence

The focal integration suite must demonstrate:

1. exact identity read returns the stored document;
2. case listing is isolated and deterministic;
3. Portuguese FTS retrieves a relevant document;
4. FTS never leaks a hit from another case;
5. invalid query/limit input fails before retrieval;
6. persistence, domain, and architecture gates remain non-regressed.

Focal command:

```text
dotnet test tests/RJ.IntegrationTests/RJ.IntegrationTests.csproj --configuration Release
```

Full gate:

```text
dotnet restore RJ.slnx
dotnet build RJ.slnx --configuration Release --no-restore
dotnet test RJ.slnx --configuration Release --no-build
```

Missing PostgreSQL/runtime/runner evidence is not PASS.
