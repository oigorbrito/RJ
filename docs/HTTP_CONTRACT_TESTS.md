# HTTP contract test protocol

## Scope

`RJ.HttpContractTests` boots the real `RJ.Api` through `WebApplicationFactory<Program>` and exercises HTTP contracts against PostgreSQL supplied through `RJ_POSTGRES_CONNECTION`.

The project uses `Microsoft.AspNetCore.Mvc.Testing` 10.0.11, xUnit v3, and the existing Npgsql baseline. No additional web framework or database abstraction is introduced.

## Database precondition

The test fixture performs `PostgresSchema.MigrateAsync` before creating the HTTP client. This is deliberate: production API startup remains read-only with respect to schema and calls only `PostgresSchema.EnsureCurrentAsync`.

If `RJ_POSTGRES_CONNECTION` is absent, the suite is skipped rather than reported as PASS.

Tests use unique case identifiers to avoid cross-test evidence collisions in the shared test database.

## Covered contracts

The minimum HTTP contract set verifies:

1. `/health/live` returns HTTP 200 with `status=live`;
2. `/health/ready` returns HTTP 200 with `status=ready` against migrated PostgreSQL;
3. ingestion returns 202 for valid evidence;
4. invalid ingestion returns 400 with `code=invalid_request`;
5. conflicting evidence returns 409 with `code=evidence_conflict`;
6. document collection pagination is deterministic and returns compact payloads without `rawContent` or normalized `content`;
7. search returns compact hits with rank but without full raw/normalized content;
8. evidence retrieval returns citable excerpts whose offsets reproduce the exact raw source substring.

## Execution

```text
dotnet test tests/RJ.HttpContractTests/RJ.HttpContractTests.csproj --configuration Release
```

The full solution gate remains:

```text
dotnet restore RJ.slnx
dotnet build RJ.slnx --configuration Release --no-restore
dotnet test RJ.slnx --configuration Release --no-build
```

An unavailable runner, absent PostgreSQL runtime, missing connection string, or missing execution log is `BLOCKED`/`NOT_TESTED`, never PASS.
