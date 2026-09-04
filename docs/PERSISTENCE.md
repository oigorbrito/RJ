# PostgreSQL persistence protocol

## Baseline

- Schema version: `1`
- PostgreSQL validation target: `18.6`
- Npgsql: `10.0.3`
- Connection environment variable: `RJ_POSTGRES_CONNECTION`

`PostgresSchema.InitializeAsync` owns the executable schema baseline for this phase. The `legal_documents` table uses `(case_id, document_id)` as its primary identity and enforces uniqueness of `(case_id, content_sha256)`.

## Idempotency contract

`PostgresLegalDocumentWriter.StoreAsync` is idempotent only when the same case, document identifier, and SHA-256 are repeated. A repeated document identity with a different hash, or the same case/hash under a different document identity, fails with `LegalDocumentPersistenceConflictException`. Existing evidence is never overwritten by this operation.

## Integration-test protocol

A PostgreSQL instance must be reachable through `RJ_POSTGRES_CONNECTION`. If it is absent, the PostgreSQL integration tests are reported as skipped rather than passed.

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

## Acceptance evidence

The minimum persistence evidence for this schema version is:

1. schema initialization succeeds;
2. a valid document is stored once;
3. repeating the same identity/hash does not create a duplicate;
4. same identity with a different hash is rejected;
5. same case/hash with a different identity is rejected;
6. domain and architecture suites remain green.

A missing database, unavailable runner, missing runtime, or absent connection string is not PASS. It is `BLOCKED` or `NOT_TESTED` according to the observed execution state.
