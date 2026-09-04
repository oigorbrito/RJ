# Database-backed test isolation

## Scope

The PostgreSQL-backed test assemblies are:

- `tests/RJ.IntegrationTests`
- `tests/RJ.HttpContractTests`

They intentionally share the PostgreSQL instance referenced by `RJ_POSTGRES_CONNECTION`. The suite does not require one database per test because business-data tests use unique case identifiers and schema setup is idempotent and serialized by the migration advisory lock.

## Parallelism policy

Both database-backed test projects contain `testconfig.json` with xUnit v3 `parallelMode` set to `none` and `parallelizeTestCollections` set to `false`.

This disables parallel execution inside each database-backed test assembly. It is intentionally not applied to Domain, Architecture, or API unit tests, which do not share mutable PostgreSQL state and should retain normal test-runner parallelism.

Cross-project execution may still overlap when a higher-level test runner schedules separate test projects concurrently. That is safe only because:

1. schema migration is idempotent and guarded by the PostgreSQL transaction-scoped advisory lock;
2. schema tests do not drop, alter, or corrupt shared schema objects;
3. writer/retrieval/HTTP business-data tests use fresh case identifiers;
4. tests do not depend on table emptiness or execution order;
5. each assertion scopes reads to the case/document created by that test.

If a future test requires destructive schema mutation, it must use a separately isolated database/schema or an equivalent disposable database boundary. It must not temporarily drop shared constraints or indexes in the common `RJ_POSTGRES_CONNECTION` database.

## Repeatability contract

A database-backed test must be repeatable against a database containing rows left by prior successful test runs. Tests must not rely on global row counts, fixed shared case identifiers, or ordering relative to other test classes.

Absence of `RJ_POSTGRES_CONNECTION` remains a skip/blocking precondition, not a passing integration result.

## Focal execution

```text
dotnet test tests/RJ.IntegrationTests/RJ.IntegrationTests.csproj --configuration Release
dotnet test tests/RJ.HttpContractTests/RJ.HttpContractTests.csproj --configuration Release
```

Full regression remains:

```text
dotnet test RJ.slnx --configuration Release --no-build
```

## Minimum acceptance evidence

1. both PostgreSQL-backed assemblies explicitly disable intra-assembly parallelism;
2. business-data tests use unique case identities;
3. schema setup is idempotent and advisory-lock serialized;
4. schema verification tests are non-destructive;
5. tests do not require a clean database or class execution order;
6. non-database test projects retain normal parallelism;
7. missing runtime, database, or runner execution is `BLOCKED`/`NOT_TESTED`, never `PASS`.
