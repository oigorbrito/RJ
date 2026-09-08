# RJ

Legal RAG service implemented in C# on .NET 10.

## Engineering baseline

- .NET 10 LTS / C# 14
- ASP.NET Core API
- modular monolith
- dependency direction enforced by architecture tests
- nullable reference types enabled
- compiler warnings treated as errors
- deterministic builds
- CI gate: restore, build, test

## Projects

- `RJ.Domain`: domain model and invariants; no dependency on other RJ projects.
- `RJ.Application`: use cases and ports; may depend on Domain.
- `RJ.Infrastructure`: adapters and external integrations; may depend on Application and Domain.
- `RJ.Api`: composition root and HTTP API; may depend on Application and Infrastructure.
- `RJ.ArchitectureTests`: executable dependency-boundary checks.

## Build

```powershell
dotnet restore RJ.slnx
dotnet build RJ.slnx --configuration Release --no-restore
dotnet test RJ.slnx --configuration Release --no-build
```

## Local MVP validation

The canonical local MVP gate is:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-local-mvp.ps1
```

It runs `LOCAL_MVP_GATE_V1` against `tests/RJ.DomainTests/RJ.DomainTests.csproj` using the fixed local MVP filter.

See `docs/engineering/testing.md` for scope, exclusions, and acceptance criteria.

## Acceptance rule

A change is accepted only with the smallest test set sufficient for its risk. Coverage percentage alone is not an acceptance criterion.
