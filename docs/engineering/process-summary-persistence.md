# RJudi process-summary persistence

Status: PROJECT_DECISION implementing the existing operational requirements for durable process-summary jobs, idempotency, ownership and restart recovery.

## Scope

Wave D moves process-summary runtime state from memory-only ownership to PostgreSQL-backed durable storage without changing the deterministic generation algorithm.

The persistence boundary is split into two application ports:

- `IProcessSummaryJobStore` stores and recovers the complete terminal `ProcessSummaryJob` payload;
- `IProcessSummaryJobAccessStore` validates persisted tenant + subject + case ownership before HTTP reads.

`ProcessSummaryPersistenceCoordinator` provides the operational contract used by the HTTP boundary:

1. compute a scoped idempotency key from hashed tenant, hashed subject and the caller-provided idempotency key;
2. check persisted state before generation;
3. reject a persisted idempotency key bound to another snapshot;
4. persist the terminal job after deterministic generation/validation;
5. resolve insert races by returning the already persisted job;
6. recover job, validated summary and refresh-plan inputs after application restart.

## PostgreSQL schema

Schema version: `4`.

Table: `process_summary_jobs`.

Persisted fields:

- `job_id` primary key;
- `scoped_idempotency_key` unique;
- SHA-256 `tenant_id_hash`;
- SHA-256 `subject_id_hash`;
- `case_id`;
- SHA-256 `snapshot_sha256`;
- complete terminal job payload in `job_json` (`jsonb`);
- `created_at`;
- `updated_at`.

The schema contains explicit SHA-256 check constraints and a case-id index. `PostgresSchema.EnsureCurrentAsync` verifies both the existing legal-document structure and the process-summary persistence structure without repairing schema drift at API startup.

Migration remains explicit through `RJ.DatabaseMigrator`. The API performs verification only.

## Restart recovery

The runtime registers:

- `PostgresProcessSummaryJobStore`;
- `PostgresProcessSummaryJobAccessStore`;
- `ProcessSummaryPersistenceCoordinator`.

The HTTP process-summary routes use `PersistentProcessSummaryEndpoint`. A new application instance can retrieve a job and validated output from PostgreSQL without relying on the previous process memory.

The existing `ProcessSummaryJobService` remains the deterministic local processor. It is not treated as the durable source of truth.

## Idempotency

The persistent idempotency namespace is scoped by tenant and subject:

`sha256(tenant) : sha256(subject) : sha256(idempotency-key)`

A replay with the same scope and snapshot returns the persisted job. The same scope/key with a different snapshot fails closed with the existing idempotency conflict contract. Different tenant/subject scopes do not collide.

The database unique constraint is the concurrency authority. `INSERT ... ON CONFLICT DO NOTHING` is followed by deterministic resolution of the persisted winner.

## Security

Raw tenant and subject identifiers are not stored in the process-summary persistence table; only SHA-256 values are persisted. HTTP access still requires the authenticated principal to match persisted tenant/subject ownership and to retain authorization for the persisted case.

Persistence does not weaken evidence-source authorization or sealed-case rules established by the previous security wave.

## Reproducibility

Canonical PostgreSQL Wave D gate:

```powershell
.\scripts\test-rjudi-wave-d.ps1
```

Precondition:

`RJ_POSTGRES_CONNECTION` must point to a reachable PostgreSQL instance. Absence of that dependency is BLOCKED, not PASS or product FAIL.

CI supplies PostgreSQL 18.6, executes `RJ.DatabaseMigrator` twice to exercise migration idempotency, builds the complete solution and executes the complete test suite.

## Explicit limits

This wave does not claim:

- scheduler execution;
- automatic refresh execution;
- distributed queue semantics;
- multi-region consistency;
- production backup/restore policy;
- production retention enforcement;
- external authentication provider selection;
- production-ready status.
