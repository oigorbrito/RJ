# RJudi process/API security boundary

Status: PROJECT_DECISION implementing the existing security requirements. This document does not select an external authentication vendor or protocol.

## Trust boundary

HTTP request JSON is not an authority for identity or ACL state.

The public `ProcessSummaryHttpRequest` contains process data only. Tenant, subject, case authorization, sealed-case access and evidence-source authorization are derived from the authenticated `HttpContext.User` principal by `IProcessSummaryCallerContextResolver`.

The current provider-neutral claims contract is:

- `ClaimTypes.NameIdentifier`: subject identity;
- `rjudi:tenant_id`: tenant identity;
- repeated `rjudi:case_id`: authorized legal-case identifiers;
- optional `rjudi:sealed_access=true`: sealed-case access;
- repeated `rjudi:evidence_source`: authorized evidence sources.

Missing authentication or required identity claims fails closed. Absence of evidence-source claims means no evidence source is authorized.

## Route enforcement

Health endpoints remain public.

The following API surfaces require an authenticated principal:

- legal-document ingestion;
- case document listing/read;
- case search;
- case evidence retrieval;
- generation-context retrieval;
- process-summary submission;
- process-summary job polling;
- validated-summary retrieval;
- refresh-plan retrieval.

Case-scoped routes require the case identifier to be present in the caller's authorized case claims. Ingestion additionally requires the supplied source name to be authorized. Unauthorized case access is returned as not-found at the HTTP boundary to avoid disclosing resource existence.

Document listing, document reads, search hits and evidence retrieval are filtered by the caller's authorized evidence-source claims. Generation-context evidence is filtered before `GenerationContextBuilder` receives it, so an unauthorized source is not admitted into the constructed context.

Process-summary jobs are bound, for the current in-memory runtime, to tenant + subject + case through `IProcessSummaryJobAccessStore`. A caller must match that scope before polling a job, retrieving a validated summary or requesting a refresh plan.

## Audit and telemetry

Audit and telemetry are separate contracts:

- `IProcessSummaryAuditSink` records security/access decisions;
- `IProcessSummaryTelemetry` records operational process-summary telemetry.

Tenant and subject identifiers are SHA-256 hashed before they enter these event contracts. Raw process content and raw attachment text are not audit/telemetry attributes.

The API host currently uses structured logging sinks for both contracts. External log/metrics export configuration is deployment infrastructure and is not claimed by this change.

## Explicit limits

This change does not demonstrate or select:

- JWT/OIDC/API-key/reverse-proxy identity as the concrete authentication mechanism;
- persistent ACL storage;
- persistent process-summary job storage;
- restart recovery;
- PostgreSQL runtime behavior;
- production audit retention;
- production telemetry export/SLOs.

Until a concrete authentication provider populates an authenticated principal satisfying the claims contract, protected API calls fail closed with 401.
