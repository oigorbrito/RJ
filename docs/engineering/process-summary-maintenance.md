# Process-summary maintenance, freshness and retention

Status: PROJECT_DECISION implementing existing RJudi freshness/refresh/retention requirements. This document does not claim production refresh execution or physical data deletion.

## Existing policy reused

The implementation reuses the existing deterministic contracts:

- `ProcessSummaryFreshnessPolicy` classifies a persisted job as `Fresh`, `Stale` or `Expired`;
- snapshot drift or summary-version drift produces `Stale`;
- `ProcessSecurityPolicy.DefaultRetentionPolicy()` defines the current summary TTL;
- `ProcessSummaryRefreshPlanner` maps `Fresh -> None`, `Stale -> Refresh`, `Expired -> Rebuild`.

Wave E does not introduce a new freshness threshold or retention duration.

## Publication retention boundary

`ProcessSummaryPersistenceCoordinator.GetValidatedSummaryAsync` evaluates the persisted job against the current retention policy before returning validated output.

- a validated, non-expired job may be published;
- an expired validated job remains addressable as job metadata so freshness/refresh decisions can still be inspected;
- validated summary content is not returned once the summary retention window has expired;
- the blocked publication emits `process_summary.retention_blocked_publication` telemetry.

This is access/publication enforcement. It is not physical deletion, database vacuuming, backup retention or evidence deletion.

## Maintenance scan

`IProcessSummaryJobStore.ListForMaintenanceAsync(currentSummaryVersion, expiredBefore, limit, ...)` provides a deterministic bounded scan of actionable persisted jobs.

Candidate filtering occurs before batching. A job is eligible when either:

- its persisted validation timestamp is before the summary-expiration cutoff; or
- its stored summary version differs from the current `ProcessSummaryPrompt.PromptVersion`.

Fresh jobs using the current summary version are excluded before `LIMIT`, so they cannot starve stale or expired jobs from a bounded maintenance batch.

The PostgreSQL implementation orders actionable candidates by persisted `updated_at`, then `job_id`. The in-memory implementation orders actionable candidates by `ValidatedAt`, then `JobId`.

`ProcessSummaryMaintenanceService.RunOnceAsync`:

1. captures one clock value for the run;
2. derives the expiration cutoff from the existing retention policy;
3. loads a bounded batch of actionable candidates only;
4. evaluates each candidate against its stored snapshot, the current prompt version and retention policy;
5. creates `Refresh` work for version-stale jobs;
6. creates `Rebuild` work for expired jobs;
7. dispatches work through `IProcessSummaryRefreshDispatcher`;
8. emits operational telemetry.

The service does not regenerate summaries itself.

## Deterministic work identity

Every maintenance work item has a SHA-256 `WorkId` derived from:

- job ID;
- snapshot hash;
- stored summary version;
- requested refresh action.

Repeated scans of the same persisted state therefore produce the same work identifier. This supports idempotent downstream dispatchers without storing a second derived queue in RJudi.

## Runtime scheduler configuration

`ProcessSummaryMaintenanceHostedService` is registered in the API host but is disabled unless both settings are supplied explicitly:

- `RJ_PROCESS_SUMMARY_MAINTENANCE_INTERVAL_SECONDS`
- `RJ_PROCESS_SUMMARY_MAINTENANCE_BATCH_SIZE`

No default cadence or batch size is invented.

Rules:

- both absent: scheduler disabled;
- only one present: configuration error;
- non-positive, non-finite or unrepresentable interval: configuration error;
- non-positive/non-integer batch size: configuration error.

When enabled, the hosted service runs maintenance immediately and then waits for the configured interval.

## Dispatcher

The runtime currently registers `LoggingProcessSummaryRefreshDispatcher`.

It records the deterministic work ID, action, job and case. It does not claim to have fetched a newer legal-process snapshot or regenerated a summary.

A future dispatcher may enqueue work into an external execution system, but it must preserve work identity/idempotency semantics.

## Explicit limits

Wave E does not demonstrate:

- current-source acquisition for automatic refresh;
- DataJud refresh execution;
- attachment reacquisition/OCR;
- distributed queue delivery guarantees;
- exactly-once delivery;
- automatic summary regeneration;
- physical deletion of expired rows;
- database backup/restore retention;
- evidence TTL enforcement;
- scheduler SLOs or production cadence selection.

Those claims require separate evidence and, where applicable, external source/runtime availability.
