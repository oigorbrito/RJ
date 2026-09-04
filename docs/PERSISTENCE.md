# PostgreSQL persistence protocol

## Baseline

- Schema version: `3`
- PostgreSQL validation target: `18.6`
- Npgsql: `10.0.3`
- Connection environment variable: `RJ_POSTGRES_CONNECTION`
- Runtime PostgreSQL command timeout: `15 seconds`

The API does not create, migrate, or repair database objects during startup.

`PostgresSchema.MigrateAsync` owns schema changes. `PostgresSchema.EnsureCurrentAsync` is a read-only startup gate that requires the expected migration ledger version and the structural invariants needed by ingestion and retrieval before the API starts serving requests.

## Runtime command policy

`PostgresLegalDocumentWriter`, `PostgresLegalDocumentReader`, and `PostgresLegalDocumentSearch` set an explicit `CommandTimeout` of `15` seconds on application data commands.

This is an operational fail-fast baseline, not a legal-domain SLA and not a corpus-derived threshold. It prevents indefinite database commands while preserving caller cancellation through the existing `CancellationToken` passed to connection open, command execution, reader iteration, and transaction commit operations.

Timeout and cancellation are not validation failures or evidence conflicts. The API must not convert them into `400`, `409`, or a partial successful response. A cancelled operation propagates cancellation; a database timeout remains a server/dependency failure unless a future explicit transport policy defines a different server-side status.

The timeout value must be revisited only with measured production/benchmark latency evidence; it must not be raised merely to hide slow queries.

## Migration ledger

`rj_schema_migrations` records applied schema versions:

- `version integer PRIMARY KEY`
- `applied_at timestamptz NOT NULL DEFAULT now()`

Current expected version is `3`.

Migration execution is transactional and acquires a PostgreSQL transaction-scoped advisory lock before inspecting or changing the schema. Re-running the current migration is idempotent. A database ledger newer than the application version is rejected rather than modified or downgraded.

Existing databases that predate the ledger can be adopted by the explicit migration command: the existing idempotent v3 DDL is applied, then version `3` is recorded. No migration is performed implicitly by API startup.

## Explicit migration command

The operational migrator is a separate executable:

```text
dotnet run --project tools/RJ.DatabaseMigrator
```

It reads the connection string only from `RJ_POSTGRES_CONNECTION`.

Exit codes:

- `0`: migration completed successfully;
- `2`: missing configuration or migration/execution error.

The command does not print the connection string or raw exception message.

Deployment ordering is therefore:

1. set `RJ_POSTGRES_CONNECTION` for the migration environment;
2. run `RJ.DatabaseMigrator` and require exit code `0`;
3. start the API;
4. API startup executes only `EnsureCurrentAsync` and fails fast when the ledger/schema is not current.

## Startup verification

`EnsureCurrentAsync` requires:

- `rj_schema_migrations` to exist;
- maximum recorded version to equal `3` exactly;
- `legal_documents` to exist with the required runtime columns (`case_id`, `document_id`, `source_name`, `raw_content`, `content`, `content_sha256`, `search_vector`);
- primary key `pk_legal_documents` with exactly `(case_id, document_id)`;
- unique constraint `uq_legal_documents_case_hash` with exactly `(case_id, content_sha256)`;
- SHA-256 check constraint `ck_legal_documents_sha256` enforcing lowercase hexadecimal length 64;
- GIN index `ix_legal_documents_search_vector` over `search_vector`.

The startup check reads PostgreSQL catalogs only. PK and unique membership are validated structurally through `pg_constraint.conkey` resolved against `pg_attribute.attnum`. The GIN index is validated structurally through `pg_index.indkey`, access method, validity/readiness/liveness flags, key count, and absence of expression/partial-index metadata. This avoids depending on formatted output from `pg_get_constraintdef()` or `pg_get_indexdef()`.

The SHA-256 check constraint is also required to be enforced, validated, and bound structurally to `content_sha256`. Its regex semantics still require inspection of the catalog expression through `pg_get_expr(conbin, conrelid)` because PostgreSQL stores check predicates as expression trees rather than decomposed relational columns. This is the narrow remaining textual semantic check; PK, unique, and GIN identity no longer depend on deparser formatting.

The startup check does not recreate, rename, repair, or otherwise mutate missing/degraded objects. A ledger that reports v3 while any required invariant is absent or structurally inconsistent is a startup failure.

Tests verify that explicit migration installs these named invariants without destructively dropping or altering shared test-database objects. The SHA-256 predicate is additionally tested behaviorally in a uniquely named temporary schema: a 64-character lowercase hexadecimal value is accepted, while short, overlength, uppercase, and non-hexadecimal values must fail with PostgreSQL `check_violation`. The temporary schema is dropped in `finally`, so this negative proof does not alter `public.legal_documents` or depend on test ordering.

## Idempotency, transaction, and concurrency contract

`PostgresLegalDocumentWriter.StoreAsync` executes each ingestion attempt in a single PostgreSQL transaction. It commits only when a new row is inserted or when an existing row proves the same case, document identifier, and SHA-256, making the operation an idempotent no-op.

A repeated document identity with a different hash, or the same case/hash under a different document identity, fails with the Application-level `LegalDocumentConflictException`. The uncommitted attempt is disposed without commit, so the original evidence remains unchanged and no second identity is persisted.

Cancellation or another failure before commit must leave no newly committed evidence from that attempt. A later retry with the original valid document remains safe: the first successful retry writes exactly one row and subsequent identical retries remain idempotent.

Concurrent requests are resolved by PostgreSQL uniqueness constraints and the same conflict inspection path. Two concurrent writes with the same case, document identifier, and SHA-256 must converge to one committed row without an application conflict. Two concurrent writes with the same case/document identity but different SHA-256 values must commit exactly one complete document and return exactly one `LegalDocumentConflictException`; which request wins is intentionally unspecified and must not depend on scheduler ordering.

The final committed row must match one complete submitted document. Mixing `source_name`, raw content, normalized content, or hash across concurrent attempts is not permitted.

Existing evidence is never overwritten by this operation.

## Integration-test protocol

A PostgreSQL instance must be reachable through `RJ_POSTGRES_CONNECTION`. If it is absent, PostgreSQL integration tests are skipped rather than passed.

Integration setup explicitly calls `PostgresSchema.MigrateAsync`; production API startup does not.

Focal execution:

```text
dotnet restore tests/RJ.IntegrationTests/RJ.IntegrationTests.csproj
dotnet test tests/RJ.IntegrationTests/RJ.IntegrationTests.csproj --configuration Release
```

Full gate:

```text
dotnet restore RJ.slnx
dotnet build RJ.slnx --configuration Release --no-restore
dotnet test RJ.slnx --configuration Release --no-build
```

GitHub Actions provisions PostgreSQL `18.6`, database `rj_test`, and injects the test-only connection string into `RJ_POSTGRES_CONNECTION`.

## Minimum acceptance evidence

1. explicit migration creates/adopts schema v3 and records the ledger;
2. repeating the migration does not add duplicate version records or rewrite evidence;
3. startup verification accepts a migrated current schema;
4. startup verification requires the primary key, case/hash uniqueness, SHA-256 check, and GIN search index in addition to columns/version;
5. PK/unique/GIN verification uses structural PostgreSQL catalog metadata rather than formatted DDL text;
6. SHA-256 check behavior is proven in an isolated schema with positive and negative database cases;
7. the API contains no schema mutation call in its startup path;
8. persistence conflict behavior remains unchanged at the Application contract;
9. retrieval/persistence integration setup uses the explicit migrator path;
10. writer/read/search commands have an explicit `15` second command timeout;
11. caller cancellation propagates through read/search operations without returning partial results;
12. timeout/cancellation are not converted into `400` or `409` at the ingestion boundary;
13. a conflicting write leaves the original source, raw content, normalized content, and SHA-256 unchanged;
14. same-hash/different-identity conflict does not persist a second identity;
15. a cancelled ingestion attempt persists no row, and a later retry succeeds exactly once and remains idempotent;
16. two concurrent identical writes converge to one complete row without conflict;
17. two concurrent writes for the same identity with different hashes produce exactly one committed complete document and one evidence conflict;
18. prior architecture, ingestion, retrieval, and benchmark gates remain unchanged.

A missing database, unavailable runner, missing runtime, or absent connection string is not PASS. It is `BLOCKED` or `NOT_TESTED` according to observed execution evidence.
