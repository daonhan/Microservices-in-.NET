# PRD: Aspire Local-Dev Overlay

## Problem Statement

When I work on a cross-service change locally I need to see traces, metrics, structured logs, and console output for all eight services on one screen, and I need a one-keystroke way to start the whole platform with F5. Today the dev loop has three pain points. First, `docker compose up --build` is slow, opaque while it boots, and rebuilds layer caches on touched code; iterating on a single service still costs a full Compose stack restart for anything that crosses a service boundary. Second, observing the running platform means juggling several browser tabs — Jaeger for traces, Grafana for metrics dashboards, `docker logs <container>` for stdout — and there is no single timeline that lines all of those up while I am actively poking the system. Third, when I want to launch a service from the IDE with the debugger attached, I still have to manually start its SQL Server, Redis, RabbitMQ, and other-service dependencies in the right order and feed connection strings through `appsettings.Development.json`. None of these are bugs in the existing stack — Jaeger/Prometheus/Loki/Grafana (ADR-0009) is correct for staging/AKS parity — but the daily inner loop deserves something tighter.

## Solution

Add a .NET Aspire local-dev overlay that runs **alongside** the existing Docker Compose stack, not in place of it. A new `apphost/ECommerce.AppHost` project (Aspire 9.x) becomes the F5 entry point: it wires SQL Server, Redis, RabbitMQ, and the eight service projects with the Aspire DSL, hands connection strings to services through Aspire's reference model, and auto-launches the Aspire Dashboard on `http://localhost:18888` for a single pane of traces, metrics, structured logs, and console output across the whole platform. A new `AddAspireServiceDefaults()` extension in `ECommerce.Shared` **wraps** the existing `AddPlatformObservability` / `AddPlatformHealthChecks` / `AddPlatformOpenApi` helpers and layers Aspire's service-discovery + OTEL defaults on top, so each service's `Program.cs` adds exactly one new line and the existing observability path (Jaeger, Prometheus, Loki, Grafana) is unchanged. `docker-compose.yaml` stays as the **parity** path for full-fidelity local runs matching AKS shape. A new ADR-0011 records that AppHost is a local-only overlay, that ADR-0009 (OTEL stack) is unaffected, and that AppHost gets its own `apphost/AppHost.slnx` as a documented exception to ADR-0006's one-slnx-per-service rule. Azure deployment, AKS manifests, and the Bicep + Azure Pipelines flow are out of scope: Aspire never runs in Azure for this PRD.

## User Stories

### Developer Perspective

1. As a developer, I want a single `dotnet run --project apphost` (or F5 in the IDE) to start every service and its infrastructure, so that I can begin debugging a cross-service flow in seconds instead of orchestrating Compose by hand.
2. As a developer, I want the Aspire Dashboard to auto-launch and show traces, metrics, structured logs, and console output for all eight services on one screen, so that I do not juggle Jaeger, Grafana, and `docker logs` tabs while iterating.
3. As a developer, I want to attach the IDE debugger to any service inside the AppHost graph, so that breakpoints work without me wiring up dependent containers manually.
4. As a developer, I want Aspire-managed SQL Server, Redis, and RabbitMQ containers with their connection strings handed to consumers automatically, so that I never edit `appsettings.Development.json` to make services find their dependencies.
5. As a developer, I want the dashboard URL, default ports, and reset commands documented in a runbook, so that a new collaborator can run the platform without reading the AppHost code.
6. As a developer, I want a single `builder.AddAspireServiceDefaults()` line in each `Program.cs`, so that adopting the overlay is one diff per service and trivially revertible.
7. As a developer, I want `AddAspireServiceDefaults()` to wrap the existing `AddPlatformObservability` and `AddPlatformHealthChecks` calls rather than replace them, so that the Jaeger/Prometheus/Loki/Grafana path keeps working unchanged when I switch to Compose.
8. As a developer, I want service discovery between services (e.g. Order → Auth `/jwks`, Saga → Inventory) to flow through Aspire's reference model when running under AppHost, so that I do not hard-code `http://auth:8003` URLs in dev config.
9. As a developer, I want existing `WebApplicationFactory<Program>` integration tests untouched by this work, so that the test suite stays green and CI behavior is unchanged.
10. As a developer, I want an AppHost smoke test that asserts the graph boots and every service reaches `/health/ready`, so that a broken Aspire wiring fails fast in CI before it lands on `main`.
11. As a developer, I want the AppHost project, the dashboard configuration, and the new shared-library extension to pin Aspire 9.x explicitly, so that minor-version drift does not silently change behavior.

### Operator / Maintainer Perspective

12. As a repo maintainer, I want AppHost to live in a new `apphost/` directory with its own `apphost/AppHost.slnx`, so that ADR-0006 ("one .slnx per service, no root .sln") stays honored in spirit and the AppHost solution is the only documented exception.
13. As a repo maintainer, I want ADR-0011 to record that Aspire is local-only and that ADR-0009 (OTEL stack) is unaffected, so that future contributors do not assume Aspire runs in AKS or that Jaeger/Prom/Loki/Grafana can be retired.
14. As a repo maintainer, I want `docker-compose.yaml` to remain the parity path for AKS-shape local runs, so that anyone validating staging behavior can still run the same set of containers Kubernetes runs.
15. As a repo maintainer, I want the runbook to spell out *when* to use AppHost vs Compose, so that contributors do not pick the wrong path and waste an hour.
16. As a repo maintainer, I want the standalone Aspire Dashboard container option (`mcr.microsoft.com/dotnet/aspire-dashboard`) documented even though it is not the default, so that future work (e.g. wiring the dashboard to a Compose-driven OTLP feed) has a starting point.

### AI Agent Perspective

17. As an AI agent working on a cross-service change, I want a documented `dotnet run --project apphost` entry point, so that I can verify a multi-service change end-to-end without standing up the Compose stack.
18. As an AI agent, I want the existing `AddPlatform*` helpers preserved, so that prior plans/PRDs and the `load-session-context` retrievals stay accurate.

## Implementation Decisions

### New AppHost project (`apphost/ECommerce.AppHost`)

- Uses the Aspire 9.x AppHost SDK and the standard Aspire DSL. Targets `net10.0` to match the rest of the repo.
- Adds Aspire integrations for **SQL Server** (one container, one logical database per service to mirror ADR-0007), **Redis** (one container shared by Basket and Order), and **RabbitMQ** (one container; the Azure Service Bus path stays Compose-only and is explicitly out of scope here).
- Wires the eight service projects (`Auth.Service`, `Basket.Service`, `Product.Service`, `Order.Service`, `Inventory.Service`, `Payment.Service`, `Shipping.Service`, `Saga.Service`) and the `ApiGateway` project as referenced resources. Each service's connection strings (SQL, Redis, RabbitMQ) flow from the Aspire DSL into the service via environment variables; the service still reads them through its existing `IConfiguration` keys.
- Auto-launches the Aspire Dashboard on `http://localhost:18888` when AppHost starts.
- AppHost is **not** referenced by services and does not appear in any service's `.slnx`.

### New AppHost solution (`apphost/AppHost.slnx`)

- Lives at `apphost/AppHost.slnx`. References `ECommerce.AppHost`, the AppHost smoke-test project, and **project references** to each of the eight service projects + `ApiGateway` (Aspire needs project refs to wire the DSL).
- Documented in ADR-0011 as the single exception to ADR-0006. The eight per-service `*.slnx` solutions continue to be the canonical build/test boundary for each service.

### New AppHost smoke-test project (`apphost/AppHost.Tests`)

- Uses `Aspire.Hosting.Testing` (`DistributedApplicationTestingBuilder`).
- One test: the AppHost graph boots, and `/health/ready` returns 200 on each of the eight services and the gateway within a bounded timeout.
- Naming follows the repo's `Given_When_Then` convention (`Given_AppHost_When_Started_Then_All_Services_Are_Healthy`).
- Runs in CI alongside existing service test suites. Not run by the Husky.Net pre-commit hook (which still runs Basket tests only, per repo policy).

### Shared library (`ECommerce.Shared`) — extend, do not replace

- Add `AddAspireServiceDefaults(this IHostApplicationBuilder builder)` (or equivalent `WebApplicationBuilder` overload) that:
  - Calls Aspire's `AddServiceDefaults()` (or its constituent pieces — service discovery, OTEL defaults, default health endpoints) **first**, so Aspire's exporters and propagators register before the platform's.
  - Then calls the existing `AddPlatformObservability(serviceName)`, `AddPlatformHealthChecks(...)`, and `AddPlatformOpenApi(...)` so the Jaeger/Prometheus/Loki/Grafana path is identical to today.
  - Guards against double-registration when both Aspire defaults and platform defaults register the same OTEL component (e.g. uses `TryAdd*` semantics or checks for an existing meter provider before adding a second one).
- Bump `ECommerce.Shared` `<Version>` and repack/push to the local NuGet feed per the existing flow.
- No behavior change for services that do **not** call `AddAspireServiceDefaults()` — adoption is opt-in per service.

### Per-service `Program.cs` changes (×8 services + gateway)

- Exactly one new line added to each composition root: `builder.AddAspireServiceDefaults();`. Placed before the existing `AddPlatform*` calls.
- No other code changes in services. DTOs, endpoints, domain models, and migrations are untouched.

### Docs

- **ADR-0011 — Aspire local-dev overlay.** Records: Aspire is local-only and never runs in Azure; Compose is the parity path for AKS-shape runs; ADR-0009 is unaffected; ADR-0006 is amended with a single documented exception for `apphost/AppHost.slnx`; Aspire 9.x is the pinned version.
- **Runbook `docs/runbooks/aspire-local-dev.md`.** Covers: `dotnet run --project apphost`, the dashboard URL, default ports, when to use AppHost vs Compose (matrix), troubleshooting (port conflicts, DCP failures, container reset), and the standalone dashboard container option for future Compose-driven setups.
- **`README.md`** and **`docs/wiki/Observability.md`** gain a short pointer to the runbook. No removal of existing Jaeger/Grafana content.

### Out of (PRD) scope, documented for future reference

- Aspire publishing to Azure Container Apps via `azd up`, or AKS via `aspirate`. Captured in "Further Notes" only.

## Testing Decisions

A good test exercises **external behavior** that a user (or operator, or downstream service) cares about, not implementation details. For this PRD that means: "Did the platform come up and is each service ready?" and "Does the shared wrapper actually compose Aspire defaults + platform defaults without double-registering?". It does **not** mean "Is the AppHost DSL graph wired exactly so" — that is structure, not behavior, and would break on every refactor.

### Modules with tests

1. **AppHost graph (`apphost/AppHost.Tests`).** Smoke test via `Aspire.Hosting.Testing`. Asserts the AppHost boots and `/health/ready` returns 200 on each of the eight services and the gateway. Failure modes covered implicitly: missing project reference, broken connection-string handoff, a service that fails to start under Aspire's environment. Prior art: existing `*.Tests` projects already pattern integration tests against `WebApplicationFactory<Program>` and use the `Given_When_Then` naming convention.
2. **`AddAspireServiceDefaults()` in `ECommerce.Shared.Tests`.** Unit-level coverage that:
   - The wrapper registers (a) Aspire service-defaults + (b) the existing `AddPlatformObservability` outputs.
   - There is no double-registration of OTEL meter/tracer providers when both Aspire and platform defaults run.
   - Existing platform observability behavior is preserved (delegate to existing `OpenTelemetryOptionsTests` / `OutboxPlatformObservabilityTests` / `DeadLetterPlatformObservabilityTests` patterns).
   - Prior art for the test shape: `shared-libs/ECommerce.Shared.Tests/OpenTelemetryOptionsTests.cs`, `OutboxPlatformObservabilityTests.cs`, `DeadLetterPlatformObservabilityTests.cs`.

### Modules without new tests

- AppHost project itself (the DSL) — behavior is covered transitively by the AppHost smoke test.
- ADR-0011, runbook, README/wiki updates — documentation only.
- Per-service `Program.cs` changes — additive single-line opt-in; existing `WebApplicationFactory<Program>` integration tests in each service's `*.Tests` project already cover startup behavior and remain green by construction (no existing assertion changes).

## Out of Scope

- **Aspire publishing to Azure.** No `azd`, no `aspirate`, no Aspire-emitted manifests. AKS continues to deploy via the existing Bicep + per-service Azure Pipelines flow. App Insights wiring stays as already planned via the Azure Monitor OpenTelemetry exporter (separate PRD).
- **Retiring or modifying the OTEL stack.** ADR-0009 (Jaeger + Prometheus + Loki + Grafana) is unaffected. `docker-compose.yaml`'s observability services are unchanged.
- **Retiring `docker-compose.yaml`.** Compose stays as the parity path for AKS-shape local runs. AppHost and Compose are intentionally both supported.
- **Migrating existing integration tests onto `Aspire.Hosting.Testing`.** Per-service `WebApplicationFactory<Program>` tests stay as they are. Only the new AppHost smoke test uses `DistributedApplicationTestingBuilder`.
- **Replacing `AddPlatformObservability` / `AddPlatformHealthChecks`.** The wrapper composes, it does not replace. ADR-0009's observability conventions are the source of truth for staging/prod parity.
- **Azure Service Bus path under Aspire.** The `Messaging:Provider=AzureServiceBus` path stays Compose-only (`--profile asb`) for now.
- **Auth dev-key handling beyond what Aspire-managed config already provides.** RS256 keys continue to live in `auth-microservice/Auth.Service/dev-keys/`.

## Further Notes

### How Aspire would interact with Azure if we ever did go there

Captured here because it was an explicit question during the PRD interview, and recording the shape now avoids re-deriving it later.

- **Azure Container Apps (native path).** `azd up` reads the AppHost manifest and provisions/deploys to ACA directly. This is the path Aspire targets first-class. It would supersede AKS for the service plane, which is a major infra change and explicitly out of scope here.
- **AKS via `aspirate`.** The community tool `aspirate` (`aspir8`) emits Kubernetes manifests from an AppHost graph. It would compete with the hand-written `kubernetes/aks-*.yml` manifests. Bicep would still own cluster/ACR/SQL/Redis/Service Bus/Key Vault provisioning.
- **Application Insights / Azure Monitor.** Independent of which compute target is chosen, the Azure Monitor OpenTelemetry exporter is already on the roadmap (see `docs/prd/azure-infrastructure-deployment.md` and ADR-0009). Aspire's OTEL defaults are OTLP-based and compatible with that exporter without changes to AppHost.
- **`OpenTelemetry__Exporter` switch.** The repo already plans a runtime-switchable exporter knob between local Collector and Azure Monitor. Aspire does not change that contract.

### Versioning and compatibility

- Aspire 9.x targets .NET 9/10 and is the pinned version. AppHost requires Docker Desktop (or equivalent OCI runtime) on the developer machine because Aspire's container integrations rely on it; this is already a repo prerequisite via Docker Compose.
- Aspire's developer-control-plane (DCP) needs ports `15888` (resource service) and `18888` (dashboard) free by default; the runbook documents how to change them.
- The smoke test runs in the same CI environment as existing integration tests and inherits their Docker availability.

### Why this is small on purpose

The wrapper-not-replace decision keeps the blast radius of this PRD bounded to: one new project (`ECommerce.AppHost`), one new test project (`AppHost.Tests`), one new solution file (`apphost/AppHost.slnx`), one new shared-library extension method, one new line per service, one new ADR, and one new runbook. If Aspire turns out to be a net loss for the inner loop, every piece is independently revertible without touching the OTEL stack, the Compose stack, the AKS manifests, or any service's domain code.
