# ADR-0011 — .NET Aspire local-dev overlay

- **Status**: Accepted
- **Date**: 2026-05-19

## Context

The daily inner loop has three pain points that are not bugs in the existing stack but still slow development: `docker compose up --build` is slow and opaque while it boots; observing a running cross-service change means juggling Jaeger, Grafana, and `docker logs` in separate tabs with no single timeline; and IDE-attached debugging of one service still requires manually starting its SQL Server, Redis, RabbitMQ, and other-service dependencies in order. The Jaeger/Prometheus/Loki/Grafana stack ([ADR-0009](0009-otel-jaeger-prometheus-loki-grafana.md)) is correct for staging/AKS parity and is not the thing being changed here.

Forces: we want an F5 / one-command local start with a single dashboard for traces, metrics, logs, and console, **without** retiring Docker Compose (the AKS-shape parity path), retiring the OTEL stack, or weakening the one-`.slnx`-per-service boundary ([ADR-0006](0006-one-slnx-solution-per-service.md)).

Source PRD [`docs/prd/PRD-Aspire-Local-Dev.md`](../prd/PRD-Aspire-Local-Dev.md) (GitHub issue [#146](https://github.com/daonhan/Microservices-in-.NET/issues/146)) and plan [`docs/plans/aspire-local-dev.md`](../plans/aspire-local-dev.md). Implemented in [`apphost/`](../../apphost/). Operational detail lives in the runbook [`aspire-local-dev.md`](../runbooks/aspire-local-dev.md).

## Decision

Add a **.NET Aspire local-dev overlay** that runs **alongside** Docker Compose, not in place of it.

- A new `apphost/Nhamnhi.AppHost` project (Aspire **9.x pinned** — currently `9.5.2`, no floating minor) becomes the F5 / `dotnet run --project apphost/Nhamnhi.AppHost` entry point. It wires SQL Server, Redis, and RabbitMQ as Aspire integrations and all 8 services + `ApiGateway` as Aspire resources, with cross-service references flowing connection strings and service-discovery URLs via the Aspire reference model.
- Each service's composition root gains exactly one new line — `builder.AddAspireServiceDefaults();` — a thin wrapper in `ECommerce.Shared` that **wraps, does not replace** the existing `AddPlatformObservability` / `AddPlatformHealthChecks` / `AddPlatformOpenApi` helpers, with a double-registration guard for the OTEL meter/tracer providers.
- **Aspire is local-only and never runs in Azure.** No `azd`/`aspirate` publishing, no AKS Aspire manifests. Docker Compose remains the parity path for AKS-shape runs and the supported path for local smoke/QA and the saga regression suite.
- **[ADR-0009](0009-otel-jaeger-prometheus-loki-grafana.md) is unaffected.** The OTEL Collector → Jaeger/Prometheus/Loki/Grafana stack is unchanged; the Aspire dashboard is an additional local-only view, not a replacement.
- **[ADR-0006](0006-one-slnx-solution-per-service.md) is amended with a single documented exception**: `apphost/AppHost.slnx` is the one solution permitted to hold project references across service boundaries, because the Aspire app-model DSL requires `<ProjectReference>`s to the services it orchestrates. No service `.slnx` references the AppHost; the exception does not generalise.

## Consequences

- One command starts the whole platform with a single dashboard at `http://localhost:18888` for traces, metrics, logs, and console; IDE-attached debugging no longer requires hand-starting dependencies.
- The codebase carries two local entry points (AppHost and Compose). The runbook [`aspire-local-dev.md`](../runbooks/aspire-local-dev.md) owns the AppHost-vs-Compose decision matrix so the choice is not ambiguous.
- `apphost/AppHost.slnx` is now a known, documented carve-out from ADR-0006; reviewers must not treat it as precedent for cross-service references elsewhere.
- Aspire is pinned at 9.x; an Aspire major bump is a deliberate follow-up, not an automatic upgrade.
- Out of scope: Azure publishing, AKS Aspire manifests, OTEL-stack retirement, Compose retirement, Azure Service Bus under Aspire, and migrating existing `WebApplicationFactory<Program>` tests onto `Aspire.Hosting.Testing`.
