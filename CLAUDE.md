# CLAUDE.md

Guidance for Claude Code in this repo. See [README.md](README.md), [CONTEXT.md](CONTEXT.md), [.claude/CLAUDE.md](.claude/CLAUDE.md) (behavioral rules).

If prior work may be relevant, use the `load-session-context` skill to search the local QMD index of prior sessions (and, after Phase 4, repo docs) and load only the relevant past context before continuing.

## Repo shape

.NET microservices monorepo, **net10.0**, no root solution. Each `*-microservice/`, `api-gateway/`, `shared-libs/` is its own `.slnx`.

Services (port, datastore): basket 8000 Redis · order 8001 SQL+Redis · product 8002 SQL · auth 8003 SQL · api-gateway 8004 — · inventory 8005 SQL · shipping 8006 SQL · payment 8007 SQL · saga 8008 SQL.

## Build / test / run

Per-solution from the service directory.

```bash
cd <svc>-microservice && dotnet build
cd <svc>-microservice && dotnet test
dotnet test --filter "FullyQualifiedName~OrderEndpointTests"   # class
dotnet test --filter "DisplayName~Given_X_When_Y_Then_Z"       # single
dotnet format --verify-no-changes --verbosity minimal          # mirrors pre-commit
docker compose up --build                                       # full stack
docker compose up sql rabbitmq redis -d                         # infra only
```

`Directory.Build.props` sets `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild`; its `NoWarn` list documents intentional exemptions.

## Pre-commit (Husky.Net)

Activate once: `dotnet tool restore && dotnet husky install`. Hook runs `dotnet format --verify-no-changes`, `dotnet build --no-restore`, then Basket tests only — **run other suites manually before pushing cross-service changes**.

### Sandbox policy (WSL / virtiofs / Docker)

Known failure: `MSB3248 No such device` on `dotnet build --no-restore` (or on `ECommerce.Shared.Tests` reading a freshly built shared DLL) caused by root-owned or sandbox-created `bin`/`obj`. Not a regression.

**Mandatory order before any commit in sandbox:**

1. Clean + restore + rerun hook:
   ```bash
   find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
   dotnet tool restore
   dotnet restore && dotnet husky run --group pre-commit
   ```
2. If still `MSB3248`, retry once more after `dotnet restore --force`.
3. If hook still fails: **STOP. Do not commit.** Report blocker to user with the exact failing command + error. User commits from host.

**Hard prohibitions** (no exceptions, no "sandbox-only" escape hatch):

- No `--no-verify`, no `-c core.hooksPath=`, no skipping `dotnet format` / `dotnet build` / tests.
- No `Hooks-Deferred:` / `Validation-Deferred:` / similar commit-message footer.
- No "passed clean in sandbox, defer remainder to host" partial commits.
- No closing the issue / marking task done while validation is deferred.

Rationale: a commit with deferred validation pollutes history, blocks downstream automation, and shifts unfinished work onto the user without their consent. The correct sandbox outcome when hooks cannot pass is **handoff, not commit**.

## Shared library (`ECommerce.Shared`)

Consumed as a NuGet package (not project ref). Local feed: `local-nuget-packages/` (gitignored). After edits:

```bash
cd shared-libs/ECommerce.Shared
dotnet pack -c Release
dotnet nuget push bin/Release/*.nupkg -s ../../local-nuget-packages
# bump <Version> in .csproj so consumers pick it up
```

Consumers see no change until version bump + new `.nupkg` in feed.

## Cross-service architecture

Read together: each service's `Program.cs` (composition root, uses `ECommerce.Shared` extensions: `AddSqlServerDatastore`, `AddOutbox`, `AddPlatformEventBus`, `AddPlatformEventPublisher`, `AddPlatformSubscriberService`, `AddEventHandler<TEvent,THandler>`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddPlatformOpenApi`); `shared-libs/ECommerce.Shared/Infrastructure/` (`EventBus/`, `Messaging/`, `RabbitMq/`, `AzureServiceBus/` — `Messaging:Provider` selects RabbitMQ by default or Azure Service Bus; `Outbox/` — `OutboxBackgroundService`, services that publish need `AddOutbox(...)` + `app.ApplyOutboxMigrations()` in Dev). New cross-cutting concerns belong in `ECommerce.Shared`.

**Saga (orchestrator-only):** Saga service owns the order saga end-to-end. It starts from `OrderCreatedEvent`, persists saga state, and drives participants exclusively with commands: `ReserveStockCommand`/`CommitStockCommand`/`ReleaseStockCommand` (Inventory), `AuthorizePaymentCommand`/`CapturePaymentCommand`/`VoidPaymentCommand`/`RefundPaymentCommand` (Payment), `ConfirmOrderCommand`/`CancelOrderCommand` (Order), `CreateShipmentCommand`/`CancelShipmentCommand` (Shipping). Participants reply with the existing integration events (`StockReserved|StockReservationFailed|StockCommitted|StockReleased|PaymentAuthorized|Captured|Failed|Voided|Refunded|OrderConfirmed|OrderCancelled|ShipmentCreated|Dispatched|Delivered|Cancelled|Returned|Failed`) carrying `CausationId`/`SagaId`. Cutover completed 2026-05-18 (issue #132); legacy event-driven saga handlers removed. Runbook: [saga-orchestrator-strangler.md](docs/runbooks/saga-orchestrator-strangler.md). ADR: [0010](docs/adr/0010-saga-orchestrator-supersedes-choreography.md). Events: `IntegrationEvents/Events/`; handlers: `IntegrationEvents/EventHandlers/`.

Per-service layout: `Endpoints/` (Minimal API), `ApiModels/` (DTOs), `Models/` (domain), `Infrastructure/Data/`, `IntegrationEvents/`, `Migrations/`. Keep DTOs vs domain split.

**Order service exception** — pilots Clean Architecture + Vertical Slices: `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/`. Boundaries enforced by NetArchTest (`Order.Tests/Architecture/LayoutTests.cs`) and the Roslyn `LayoutAnalyzer`. ADR: [0011](docs/adr/0011-order-cleanarch-vsa-pilot.md). Runbook for new slices: [adding-a-new-slice.md](docs/runbooks/adding-a-new-slice.md). Propagation to other services is a separate ADR.

**Product service exception** — second Clean Architecture + Vertical Slices pilot, same layout as Order: `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/`. Boundaries enforced by NetArchTest (`Product.Tests/Architecture/LayoutTests.cs`) and the Roslyn `Product.Service.LayoutAnalyzer`. Composes ADR [0011](docs/adr/0011-order-cleanarch-vsa-pilot.md) by reference (no new ADR); reuses the [adding-a-new-slice.md](docs/runbooks/adding-a-new-slice.md) runbook unchanged. Propagation to remaining services is a separate ADR.

**Basket service exception** — third Clean Architecture + Vertical Slices pilot, same layout as Order/Product: `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/`. Boundaries enforced by NetArchTest (`Basket.Tests/Architecture/LayoutTests.cs`) and the Roslyn `Basket.Service.LayoutAnalyzer`. Composes ADR [0011](docs/adr/0011-order-cleanarch-vsa-pilot.md) by reference (no new ADR); reuses the [adding-a-new-slice.md](docs/runbooks/adding-a-new-slice.md) runbook unchanged. **Diverges from Order/Product: no outbox seam (Basket emits no integration events); no CQRS-lite read split (one read, no projection benefit).** Propagation to remaining services is a separate ADR.

**Auth service exception** — fourth Clean Architecture + Vertical Slices pilot, same layout as Order/Product/Basket minus `Contracts/`: `Features/<Slice>/`, `Domain/`, `Infrastructure/`. Boundaries enforced by NetArchTest (`Auth.Tests/Architecture/LayoutTests.cs`) and the Roslyn `Auth.Service.LayoutAnalyzer`. Composes ADR [0011](docs/adr/0011-order-cleanarch-vsa-pilot.md) by reference (no new ADR); reuses the [adding-a-new-slice.md](docs/runbooks/adding-a-new-slice.md) runbook unchanged. **Diverges from Order/Product: no `Contracts/` folder, no outbox seam, no integration events (Auth produces and consumes no cross-service payloads).** Propagation to remaining services is a separate ADR.

**Inventory service exception** — fifth Clean Architecture + Vertical Slices pilot, same layout as Order/Product/Basket: `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/`. Boundaries enforced by NetArchTest (`Inventory.Tests/Architecture/LayoutTests.cs`) and the Roslyn `Inventory.Service.LayoutAnalyzer`. Composes ADR [0011](docs/adr/0011-order-cleanarch-vsa-pilot.md) by reference (no new ADR); reuses the [adding-a-new-slice.md](docs/runbooks/adding-a-new-slice.md) runbook unchanged. **Diverges from Order: no `IIntegrationMap<,>` / outbox interceptor seam (Inventory constructs integration events inline per slice — no DbContext-level translation switch to extract); `IInventoryStore` lives in `Domain/Abstractions/` with `EfInventoryStore` in Infrastructure (matches Order); `StockLevelMonitor` returns domain-typed crossings (`LowStockCrossing`, `StockDepletion`) with slices mapping to Contracts events; saga commands (`ReserveStockCommand`/`CommitStockCommand`/`ReleaseStockCommand`) consumed from `ECommerce.Shared.IntegrationEvents.Commands`, not owned in local `Contracts/Integration/`.** Propagation to remaining services is a separate ADR.

**Shipping service exception** — sixth Clean Architecture + Vertical Slices pilot, same layout as Order/Product/Basket/Inventory: `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/`. Boundaries enforced by NetArchTest (`Shipping.Tests/Architecture/LayoutTests.cs`) and the Roslyn `Shipping.Service.LayoutAnalyzer`. Composes ADR [0011](docs/adr/0011-order-cleanarch-vsa-pilot.md) by reference (no new ADR); reuses the [adding-a-new-slice.md](docs/runbooks/adding-a-new-slice.md) runbook unchanged. **Diverges from Order: no `IIntegrationMap<,>` / outbox interceptor seam (Shipping constructs integration events inline per slice — no DbContext-level translation switch to extract, matches Inventory); `IShipmentStore` lives in `Domain/Abstractions/` with `EfShipmentStore` in Infrastructure (matches Order/Inventory); carrier adapters (`FakeExpressCarrierGateway`, `FakeGroundCarrierGateway`, `FakeCarrierDispatchRegistry`, `FakeCarrierWebhookParser`, `CarrierStatusApplier`, `CarrierPollingService`, `RateShoppingService`, `CarrierWebhookOptions`) consolidated under `Infrastructure/Carriers/` with `ICarrierGateway` abstraction in `Domain/Abstractions/`; `ShippingMetrics` moved to `Infrastructure/Observability/` (no peer-layer `Observability/` folder); HTTP write endpoints split per state transition (`PickShipment`, `PackShipment`, `DispatchShipment`, `DeliverShipment`, `FailShipment`, `ReturnShipment`, `CancelShipment`, `ProcessCarrierWebhook`); HTTP `CancelShipment` and saga `CancelShipmentCommand` are two distinct slices that each construct `ShipmentCancelledEvent` independently; `CarrierPollingService` (hosted) stays in `Infrastructure/Carriers/`, not a `Features/` slice; saga commands (`CreateShipmentCommand`/`CancelShipmentCommand`) consumed from `ECommerce.Shared.IntegrationEvents.Commands`, not owned in local `Contracts/Integration/`.** Propagation to remaining services (payment, saga) is a separate ADR.

## API Gateway provider switch

Gateway compiles both YARP and Ocelot. `Gateway:Provider` (env `Gateway__Provider`) = `Yarp` (default) or `Ocelot`; unknown values fail fast. Routes/port/auth/health/metrics identical across both.

## DLQ + operator API

Retry-exhausted messages dead-letter to `ecommerce-dlq` exchange. API Gateway poller persists them (plus failed outbox rows from each service's `/internal/outbox/failed`, gated by `RequireService`) into gateway-owned `dead_letter_messages`. Operator endpoints under `/operator/api/failures*` (Bearer + `Operator` claim): list/detail/single replay/batch replay/discard. Replay re-publishes to `OriginalQueue`; `dlq.replay` spans carry original `CorrelationId`. Counters: `dlq_messages_total`, `dlq_replays_total`, `dlq_discards_total`.

When changing event payloads, retry policy, or queue routing, audit both consumer side **and** the DLQ poller.

## Authentication

RS256 user JWTs from Auth (`POST /login`); `client_credentials` service tokens (`POST /token`). Resources validate via `AddJwtAuthentication()` (fetches+caches `/jwks` — no shared secret). `/internal/*` gated by `RequireService` policy (`scope=service`); user tokens cannot reach. Dev keys: `auth-microservice/Auth.Service/dev-keys/`.

## CI/CD

Azure Pipelines per service (`azure-pipelines.yml`). Bicep provisions AKS/ACR/Azure SQL/Service Bus/App Insights. Deploys to `ecommerce-{dev,staging,prod}` namespaces. **GitHub Actions is not used.**

## Conventions

- File-scoped namespaces, `var` preferred, usings outside namespace (`.editorconfig`).
- Test names `Given_When_Then` with underscores (`CA1707` suppressed).
- `**/Migrations/*.cs` marked `generated_code = true` — don't hand-edit style.
- Per-service `IDesignTimeDbContextFactory` so `dotnet ef migrations add` works without running `Program.cs`.
- Integration tests use `WebApplicationFactory<Program>`; each service has `public partial class Program { }` at end of `Program.cs`.

## Behavioral

Apply `.claude/CLAUDE.md` (think first, simplicity, surgical changes, goal-driven). Make only changes the user asked for; match existing style; prefer smallest correct change; push back on over-engineering; state a brief plan + success criteria for non-trivial work.
