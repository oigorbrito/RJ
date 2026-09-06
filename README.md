# RJ

Legal RAG service implemented in C# on .NET 10.

## Operating mode

RJ is an offline-only system. Supported production and benchmark execution must not depend on internet access, external model APIs, provider credentials, or remote inference services.

Supported generation paths are local:

- `harness-selftest-v1`
- `oab-bench-demo-v2`
- `oab-rulingbr-generation-challenger-v1`

Corpora, benchmark catalogs, retrieval evidence, reports, manifests, and database state are read from or written to local resources. An online model id is rejected by the benchmark CLI.

## Engineering baseline

- .NET 10 LTS / C# 14
- ASP.NET Core API
- modular monolith
- dependency direction enforced by architecture tests
- nullable reference types enabled
- compiler warnings treated as errors
- deterministic builds
- CI gate: restore, build, test
- runtime execution is offline-only

## Projects

- `RJ.Domain`: domain model and invariants; no dependency on other RJ projects.
- `RJ.Application`: use cases and ports; may depend on Domain.
- `RJ.Infrastructure`: local persistence and operational adapters; may depend on Application and Domain.
- `RJ.Api`: composition root and HTTP API; may depend on Application and Infrastructure. HTTP here means the local application API, not an external provider dependency.
- `RJ.ArchitectureTests`: executable dependency-boundary checks.

## Build

```powershell
dotnet restore RJ.slnx
dotnet build RJ.slnx --configuration Release --no-restore
dotnet test RJ.slnx --configuration Release --no-build
```

For an air-gapped machine, package restore must be satisfied from a pre-populated local NuGet cache or an internal/offline package source before the build commands are run. Application runtime and benchmark execution do not require internet access.

## Acceptance rule

A change is accepted only with the smallest test set sufficient for its risk. Coverage percentage alone is not an acceptance criterion.
