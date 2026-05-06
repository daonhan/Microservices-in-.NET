# ADR-0006 — One `.slnx` solution per service; no root `.sln`

- **Status**: Accepted
- **Date**: 2026-05-06

## Context

A monorepo with seven services + a gateway + shared libraries can be organised in two ways: one big root solution that opens everything at once, or one solution per service. The big-solution approach is convenient for jump-to-definition across the whole repo, but it blurs ownership boundaries, slows down IDE load, and tempts contributors to add cross-service `<ProjectReference>`s that should have been events or shared-package consumption. The platform also wants to demonstrate independent build/test/deploy boundaries, which a root solution actively undermines.

Implemented at the repository root and inside each service folder. See also the wiki page [`Architecture.md`](../wiki/Architecture.md).

## Decision

There is **no root `.sln`**. Each service has its own `.slnx` (the modern XML solution format) — for example `order-microservice/Order.Service.slnx`. All build, test, and `dotnet format` operations are run from the service directory. The shared library has its own `.slnx` too. CI pipelines and the pre-commit hook target individual service solutions.

## Consequences

- Service boundaries are enforced by the solution layout: you cannot accidentally pull another service into your build.
- IDE workflows assume you open one service at a time, which mirrors how you'd actually work in production.
- Cross-service refactors require visiting each affected service explicitly — a feature, not a bug, given the saga's contract surface is integration events, not shared types.
- Out of scope: any tooling that requires a root `.sln` (legacy `.NET Framework` workloads, certain third-party analyzers).
