# Offline operation contract

## Requirement

RJ production and benchmark execution are offline-only. A supported run must not require internet access, an external inference endpoint, an API key, or provider-specific credentials.

## Supported local generation models

- `harness-selftest-v1`
- `oab-bench-demo-v2`
- `oab-rulingbr-generation-challenger-v1`

The benchmark CLI rejects unsupported model ids, including former online-provider ids.

## Local dependencies

Runtime inputs are local files, local corpora, local benchmark catalogs, and the configured PostgreSQL instance. PostgreSQL may run on the same host or on an isolated private network; it must not require public internet access.

The application HTTP API is a local service boundary and is not an external provider dependency.

## Air-gapped build

`dotnet restore` normally resolves NuGet packages. On an air-gapped machine, restore must use a pre-populated local NuGet cache or an approved offline/internal package feed. After dependencies are present locally, build, test, database migration, benchmark execution, report generation, and manifest verification must operate without public internet access.

## Acceptance gates

An offline-only change is accepted only when:

1. Release build passes.
2. Database migration passes against the local PostgreSQL test service.
3. Full tests pass.
4. The benchmark CLI rejects online model ids.
5. Supported benchmark paths use only local evidence and local model implementations.
6. No external provider secret is required for runtime or benchmark execution.

External legal-corpus provenance/oracle validation remains a separate validation concern and is not satisfied merely by offline execution.
