# RJ

**Legal RAG service implemented in C# 14 / .NET 10 with explicit architectural boundaries and reproducible validation.**

RJ is a compact public backend project focused on two things:

1. demonstrating conventional **ASP.NET Core / .NET engineering** with enforceable dependency rules;
2. applying that structure to a legal-document RAG workload without letting retrieval concerns collapse the application architecture.

---

## Engineering baseline

- **.NET 10 / C# 14**
- ASP.NET Core API
- modular monolith
- nullable reference types enabled
- compiler warnings treated as errors
- deterministic builds
- centrally managed package versions
- architecture tests for dependency direction
- CI gate: restore -> build -> test

---

## Architecture

```text
RJ.Domain
   ↑
RJ.Application
   ↑
RJ.Infrastructure
   ↑
RJ.Api

RJ.ArchitectureTests
   └── verifies dependency rules
```

### Project responsibilities

- **RJ.Domain** — domain model and invariants; no dependency on other RJ projects.
- **RJ.Application** — use cases and ports; depends only on Domain.
- **RJ.Infrastructure** — persistence/external adapters; may depend on Application and Domain.
- **RJ.Api** — composition root and HTTP surface.
- **RJ.ArchitectureTests** — executable dependency-boundary checks.

The point is not to maximize project count. The point is to keep dependency direction explicit enough that the architecture can be tested instead of only documented.

---

## RAG work

The service is used as a legal RAG engineering surface.

Current repository history includes:

- local RAG MVP validation;
- robustness work over an expanded legal corpus;
- reproducible local acceptance gates;
- ongoing evaluation work preserved separately from architectural claims.

The project intentionally keeps:

```text
RAG behavior
!=
architecture correctness
!=
production readiness
```

A retrieval result or evaluation run does not override the normal backend quality gates.

---

## Quality model

A change is validated according to risk rather than by a single coverage target.

Examples:

- domain rule -> focused unit/behavior test;
- dependency-boundary change -> architecture test;
- API change -> build + relevant HTTP/application test;
- RAG behavior change -> reproducible evaluation against the intended corpus;
- bug fix -> regression evidence where practical.

Coverage percentage alone is not an acceptance criterion.

---

## Build

```powershell
dotnet restore RJ.slnx
dotnet build RJ.slnx --configuration Release --no-restore
dotnet test RJ.slnx --configuration Release --no-build
```

The same basic restore/build/test contract is intended to remain valid in CI.

---

## Repository layout

```text
src/
  RJ.Domain/
  RJ.Application/
  RJ.Infrastructure/
  RJ.Api/

tests/
  ...
  RJ.ArchitectureTests/

docs/
scripts/
tools/
```

---

## What this project is meant to demonstrate

RJ is intentionally smaller than projects such as BPT2.

Its value is clarity:

- C# / .NET backend engineering;
- dependency inversion in an executable codebase;
- modular-monolith boundaries;
- deterministic build discipline;
- architecture verification;
- RAG work contained behind normal application boundaries.

It is a compact proof that the broader AI-systems work in this portfolio sits on top of conventional software-engineering fundamentals rather than replacing them.
