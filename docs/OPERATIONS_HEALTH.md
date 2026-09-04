# Operational health protocol

## Endpoints

The API exposes separate liveness and readiness surfaces:

- `GET /health`
- `GET /health/live`
- `GET /health/ready`

`/health` is retained as a compatibility alias for liveness.

## Liveness

Liveness has no external dependency and performs no PostgreSQL access. A running process returns HTTP 200 with:

```json
{
  "status": "live"
}
```

Liveness must not execute migrations, schema checks, legal-document queries, retrieval, generation, or benchmark operations.

## Readiness

Readiness depends only on the PostgreSQL operational dependency and the expected database schema. It invokes `IReadinessProbe`; the current implementation is `PostgresReadinessProbe`, which delegates to the read-only `PostgresSchema.EnsureCurrentAsync` verification introduced by the migration protocol.

A successful check returns HTTP 200:

```json
{
  "status": "ready"
}
```

A dependency, schema, or readiness-timeout failure returns HTTP 503:

```json
{
  "status": "not_ready"
}
```

Dependency exception messages are not copied into the HTTP response.

## Timeout and cancellation

The readiness boundary applies a two-second internal timeout. Internal timeout/cancellation is mapped to HTTP 503. Cancellation originating from the request token is propagated rather than rewritten as dependency failure.

The unit contract now proves the actual timeout path with a probe that blocks indefinitely until the linked readiness token is cancelled. The acceptance condition is both HTTP 503 and observed cancellation at the probe boundary; this distinguishes the configured timeout behavior from merely throwing a synthetic `OperationCanceledException`.

## Startup relationship

The API still performs `PostgresSchema.EnsureCurrentAsync` at startup as the fail-fast schema-version guard established by the persistence protocol. This call is read-only and does not migrate the database.

Readiness remains necessary after startup because PostgreSQL connectivity or schema availability may change while the process is running.

## Non-goals

Readiness does not:

- mutate schema;
- execute `PostgresSchema.MigrateAsync`;
- query legal evidence or case data;
- test retrieval quality;
- call generation models;
- expose database or connection error details.

## Minimum acceptance evidence

1. liveness returns 200 without invoking a dependency probe;
2. readiness returns 200 when the probe succeeds;
3. readiness returns 503 for dependency/schema failure without leaking exception details;
4. internal readiness cancellation is 503;
5. the configured readiness timeout cancels a blocked probe and returns 503;
6. request cancellation propagates;
7. PostgreSQL readiness succeeds after an explicit migration;
8. prior migration, persistence, ingestion, retrieval, and architecture gates remain unchanged.

Unavailable runtime, PostgreSQL, or GitHub Actions runner is not PASS.
