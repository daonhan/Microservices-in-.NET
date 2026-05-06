# ADR-0005 — `ECommerce.Shared` distributed as a NuGet package via a local feed

- **Status**: Accepted
- **Date**: 2026-05-06

## Context

Several cross-cutting concerns — event bus, RabbitMQ wiring, outbox, observability, health checks, OpenAPI defaults — are identical across services and need a single home. The naive option is a project reference (`<ProjectReference>`) from each service to a shared library project. That couples build graphs, makes per-service `.slnx` solutions leaky, and hides what a real shared library propagation looks like in production (where teams rarely have project references across service boundaries).

Implemented in [`shared-libs/ECommerce.Shared/`](../../shared-libs/ECommerce.Shared/) and published to [`local-nuget-packages/`](../../local-nuget-packages/). See also the wiki page [`Shared-Library.md`](../wiki/Shared-Library.md).

## Decision

`ECommerce.Shared` is built and packed as a real NuGet package and published to a local file-based feed at `local-nuget-packages/`. Every service consumes it with a versioned `<PackageReference>`. To roll out a change, the library's `<Version>` is bumped, a new `.nupkg` is pushed to the local feed, and consumers are bumped explicitly per-service.

## Consequences

- Each service has an independent build boundary — its `.slnx` doesn't drag in the shared library's source.
- Version bumps are explicit and deliberate; you cannot accidentally ship an in-progress shared change across all services.
- Adds a small ceremony when iterating on the shared library (`dotnet pack`, `dotnet nuget push`, bump consumers).
- `local-nuget-packages/` is gitignored, so contributors regenerate it locally; CI does the same on each build.
- Out of scope: pushing to a real feed (NuGet.org, Azure Artifacts) — the local-feed pattern is intentional for the portfolio scope.
