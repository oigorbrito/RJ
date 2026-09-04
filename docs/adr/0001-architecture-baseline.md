# ADR 0001: Architecture baseline

## Status

Accepted.

## Context

The MVP requires a production-oriented Legal RAG service with explicit dependency boundaries, deterministic validation, and minimal operational complexity.

## Decision

Use .NET 10 LTS with C# 14 and ASP.NET Core. Structure the service as a modular monolith with dependency direction from API and Infrastructure toward Application and Domain. Keep Domain independent of framework and infrastructure concerns. Introduce additional architectural components only when a measured requirement justifies them.

## Consequences

The initial system has one deployable API and separate projects for Domain, Application, Infrastructure, and architecture tests. Boundary rules are executable. Microservices, mediator/event-bus infrastructure, CQRS, graph storage, and separate vector infrastructure are not part of the baseline.
