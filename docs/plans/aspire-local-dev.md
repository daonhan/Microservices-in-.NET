# Plan: Aspire local-dev overlay

> Source PRD: [docs/prd/PRD-Aspire-Local-Dev.md](../prd/PRD-Aspire-Local-Dev.md) · GitHub issue [#146](https://github.com/daonhan/Microservices-in-.NET/issues/146)

Tracer-bullet vertical slices. Each phase is end-to-end demoable on its own.

## Architectural decisions

Durable across all phases.

- **AppHost project**: `apphost/Nhamnhi.AppHost`, Aspire **9.x pinned** (no floating minor), `net10.0`. F5 / `dotnet run --project apphost` entry point.
- **AppHost solution**: `apphost/AppHost.slnx`. Single documented exception to ADR-0006 (one-`.slnx`-per-service). References `Nhamnhi.AppHost`, the smoke-test project, and project references to all 8 services + `ApiGateway` (Aspire DSL requires project refs).
- **AppHost smoke-test project**: `apphost/AppHost.Tests` via `Aspire.Hosting.Testing` (`DistributedApplicationTestingBuilder`). Naming: `Given_When_Then` with underscores. CI-only — not in Husky pre-commit.
- **Shared-library extension**: new `AddAspireServiceDefaults(this IHostApplicationBuilder builder)` (and/or `WebApplicationBuilder` overload) in `ECommerce.Shared`. **Wraps, does not replace** existing `AddPlatformObservability` / `AddPlatformHealthChecks` / `AddPlatformOpenApi`. Aspire defaults register first; platform defaults register second; double-registration guarded via `TryAdd*` semantics or provider-existence checks.
- **Per-service composition root**: exactly one new line `builder.AddAspireServiceDefaults();` before existing `AddPlatform*` calls. No other code changes per service.
- **Dashboard**: auto-launches on `http://localhost:18888`. DCP resource-service port `15888`. Runbook documents how to change.
- **Infrastructure containers**: one SQL Server (one logical DB per service, mirrors ADR-0007), one Redis (Basket + Order share), one RabbitMQ. Azure Service Bus path stays Compose-only (`--profile asb`), out of scope.
- **Connection strings**: flow from Aspire DSL into services via env vars. Services keep existing `IConfiguration` keys unchanged.
- **Service discovery**: cross-service URLs (Order → Auth `/jwks`, Saga → Inventory, gateway → upstreams) flow via Aspire reference model — no hard-coded `http://auth:8003` in dev config.
- **Out of scope**: Azure publishing (`azd`, `aspirate`), AKS Aspire manifests, OTEL stack retirement, Compose retirement, migrating existing `WebApplicationFactory<Program>` tests onto `Aspire.Hosting.Testing`, Azure Service Bus under Aspire.
- **Compose is parity path**: `docker-compose.yaml` unchanged. AppHost is local-only overlay. ADR-0009 (Jaeger/Prom/Loki/Grafana) unaffected.
- **Shared-lib release flow**: per ADR-0005 — `dotnet pack -c Release` → push to `local-nuget-packages/` → bump `<Version>` so consumers pick up.

---

## Phase 1: Tracer bullet — AppHost + wrapper + Basket only

**User stories**: 1, 2, 4, 6, 7, 11

### What to build

Single thin slice cutting through every layer. Create `apphost/Nhamnhi.AppHost` (Aspire 9.x, `net10.0`) wiring SQL Server, Redis, RabbitMQ as Aspire integrations. Add `apphost/AppHost.slnx` referencing the AppHost project + project reference to `Basket.Service` (only). Add `AddAspireServiceDefaults()` to `ECommerce.Shared` with double-registration guard. Pack + push to `local-nuget-packages/` with bumped `<Version>`. Onboard `Basket.Service` with the single-line wrapper call. Dashboard auto-launches at `:18888`.

Wrapper unit tests cover: (a) Aspire defaults + `AddPlatformObservability` outputs both register, (b) no double-registration of OTEL meter/tracer providers, (c) existing platform observability behavior preserved. Prior-art test shape: `shared-libs/ECommerce.Shared.Tests/OpenTelemetryOptionsTests.cs`, `OutboxPlatformObservabilityTests.cs`, `DeadLetterPlatformObservabilityTests.cs`.

### Acceptance criteria

- [ ] `apphost/Nhamnhi.AppHost/Nhamnhi.AppHost.csproj` exists, Aspire 9.x pinned, targets `net10.0`.
- [ ] `apphost/AppHost.slnx` exists, references AppHost project + project ref to `Basket.Service`.
- [ ] AppHost DSL wires SQL Server, Redis, RabbitMQ containers and Basket as a resource.
- [ ] `ECommerce.Shared` has `AddAspireServiceDefaults(IHostApplicationBuilder)` (and/or `WebApplicationBuilder` overload) that calls Aspire defaults first, then existing `AddPlatform*` helpers.
- [ ] Wrapper guards against double-registration of OTEL meter/tracer providers (verified by unit test).
- [ ] `ECommerce.Shared` `<Version>` bumped, `.nupkg` pushed to `local-nuget-packages/`.
- [ ] `Basket.Service/Program.cs` adds exactly one new line: `builder.AddAspireServiceDefaults();` before `AddPlatform*` calls. No other changes.
- [ ] `dotnet run --project apphost/Nhamnhi.AppHost` starts SQL+Redis+RabbitMQ+Basket and auto-launches dashboard at `http://localhost:18888`.
- [ ] Basket reaches `/health/ready` under AppHost without editing `appsettings.Development.json`.
- [ ] Wrapper unit tests pass in `ECommerce.Shared.Tests` (registration + no-double-registration + behavior preservation).
- [ ] `docker compose up` still works unchanged; Jaeger/Prom/Loki/Grafana path unaffected.
- [ ] No service `.slnx` references AppHost.

---

## Phase 2: Fan out — 7 remaining services + ApiGateway

**User stories**: 1, 3, 4, 6, 8

### What to build

Extend AppHost DSL to wire the other seven services (`Auth.Service`, `Product.Service`, `Order.Service`, `Inventory.Service`, `Payment.Service`, `Shipping.Service`, `Saga.Service`) and `ApiGateway` as Aspire resources. Add project references for each to `apphost/AppHost.slnx`. Add the one-line `AddAspireServiceDefaults();` to each composition root. Wire cross-service URLs (Order → Auth `/jwks`, Saga → Inventory, gateway → upstream services) via Aspire reference model so the dev path drops hard-coded `http://<svc>:<port>` URLs.

No domain code, DTOs, endpoints, or migrations change. Existing `WebApplicationFactory<Program>` integration tests remain untouched and green.

### Acceptance criteria

- [ ] AppHost DSL wires all 8 services + `ApiGateway` as resources with connection-string handoff for SQL/Redis/RabbitMQ.
- [ ] `apphost/AppHost.slnx` has project references to all 8 services + `ApiGateway`.
- [ ] Each of `Auth/Basket/Product/Order/Inventory/Payment/Shipping/Saga/ApiGateway` `Program.cs` has exactly one new line: `builder.AddAspireServiceDefaults();` before `AddPlatform*` calls.
- [ ] No other code changes in any service (DTOs, endpoints, domain, migrations untouched).
- [ ] Service-discovery URLs (Order → Auth `/jwks`, Saga → Inventory, gateway → upstreams) flow via Aspire reference model under AppHost.
- [ ] `dotnet run --project apphost/Nhamnhi.AppHost` boots all 9 resources and dashboard shows traces/metrics/logs/console for every one.
- [ ] Each service's existing `*.Tests` project (`WebApplicationFactory<Program>`) passes unchanged.
- [ ] `docker compose up` path remains functionally identical.

---

## Phase 3: AppHost smoke test

**User stories**: 9, 10

### What to build

New `apphost/AppHost.Tests` project using `Aspire.Hosting.Testing` (`DistributedApplicationTestingBuilder`). One test: `Given_AppHost_When_Started_Then_All_Services_Are_Healthy` boots the AppHost graph and asserts `/health/ready` returns 200 on each of the 8 services + gateway within a bounded timeout. Add the project to `apphost/AppHost.slnx`. Wire into CI alongside existing service test suites. Not run by Husky.Net pre-commit hook (repo policy: Basket tests only at commit time).

### Acceptance criteria

- [ ] `apphost/AppHost.Tests/AppHost.Tests.csproj` exists, references `Aspire.Hosting.Testing` and `Nhamnhi.AppHost`.
- [ ] Test `Given_AppHost_When_Started_Then_All_Services_Are_Healthy` exists and asserts `/health/ready` returns 200 on Auth, Basket, Product, Order, Inventory, Payment, Shipping, Saga, and ApiGateway.
- [ ] Test has a bounded timeout (no indefinite hangs in CI).
- [ ] Test runs in CI under an Azure Pipelines stage and fails the pipeline on a broken AppHost wiring (missing project ref, broken connection-string handoff, service that fails to start under Aspire).
- [ ] Husky.Net pre-commit hook remains unchanged (still runs Basket tests only).
- [ ] Pre-existing per-service integration tests untouched.

---

## Phase 4: Docs (ADR-0011 + runbook + pointers)

**User stories**: 5, 12, 13, 14, 15, 16, 17, 18

### What to build

`docs/adr/0011-aspire-local-dev-overlay.md` recording: Aspire is local-only and never runs in Azure; Compose is the parity path; ADR-0009 is unaffected; ADR-0006 is amended with a single documented exception for `apphost/AppHost.slnx`; Aspire 9.x is the pinned version. Add ADR-0011 row to `docs/adr/README.md` index.

`docs/runbooks/aspire-local-dev.md` covering: `dotnet run --project apphost` entry point, dashboard URL (`http://localhost:18888`), default ports (DCP `15888`, dashboard `18888`), AppHost-vs-Compose decision matrix (when to use which), troubleshooting (port conflicts, DCP failures, container reset commands), and the standalone Aspire Dashboard container option (`mcr.microsoft.com/dotnet/aspire-dashboard`) documented for future Compose-driven setups.

Short pointer from `README.md` and `docs/wiki/Observability.md` to the runbook. No removal of existing Jaeger/Grafana content.

### Acceptance criteria

- [ ] `docs/adr/0011-aspire-local-dev-overlay.md` exists, status `Accepted`, records local-only scope, ADR-0009 unaffected, ADR-0006 exception for `apphost/AppHost.slnx`, Aspire 9.x pinned.
- [ ] `docs/adr/README.md` index lists ADR-0011 in numeric order.
- [ ] `docs/runbooks/aspire-local-dev.md` exists and covers: entry point, dashboard URL + ports, AppHost-vs-Compose matrix, troubleshooting (port conflicts, DCP failures, reset), standalone dashboard container option.
- [ ] `README.md` has a short pointer to the runbook (no removal of existing Jaeger/Grafana content).
- [ ] `docs/wiki/Observability.md` has a short pointer to the runbook (no removal of existing Jaeger/Grafana content).
- [ ] All links in ADR-0011 + runbook resolve.
- [ ] `CONTEXT.md` runbooks list (if applicable) includes `aspire-local-dev.md`.
