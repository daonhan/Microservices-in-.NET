# Plan: Shipping.Service Clean Architecture + Vertical Slice Pilot

> Source PRD: `docs/prd/PRD-Shipping-CleanArch-VSA-Pilot.md` (tracking issue [#209](https://github.com/daonhan/Microservices-in-.NET/issues/209))
> Branch: `refactor/shipping-vsa` (already checked out)

## Context

`Shipping.Service` is organized by technical type: 11 HTTP routes inline in one 400-LOC `Endpoints/ShippingApiEndpoints.cs`, 3 event/command consumers in `IntegrationEvents/EventHandlers/`, 9 domain types in `Models/`, 8 carrier-infra types in a top-level `Carriers/` folder, `ShippingMetrics` in a top-level `Observability/` folder, and `ShippingContext : DbContext, IShipmentStore` wearing two hats (DbContext + store impl) — the same smell Inventory had pre-refactor. To trace one feature (e.g. dispatch a shipment) a contributor jumps across `Endpoints/ShippingApiEndpoints.cs`, `Models/Shipment.cs`, `Carriers/RateShoppingService.cs`, `Infrastructure/Data/EntityFramework/ShippingContext.cs`, and `IntegrationEvents/ShipmentDispatchedEvent.cs`.

This pilot (#6, after Order / Product / Basket / Auth / Inventory) applies the same Clean Architecture + VSA layout to `Shipping.Service`. Zero functional behavior change. Boundaries enforced twice (NetArchTest + Roslyn analyzer). Intended outcome: each feature owns one `Features/<Slice>/` folder; Domain has zero Infrastructure / Contracts references; `IShipmentStore` lives in Domain; `EfShipmentStore` lives in Infrastructure; carriers and metrics fold under `Infrastructure/`; saga commands continue to flow through the shared lib.

## Architectural decisions

Durable decisions that apply across all phases:

- **Project shape**: single `Shipping.Service.csproj` retained; boundaries enforced by namespace + Roslyn analyzer + NetArchTest, not by csproj split.
- **Folder topology**:
  - `Features/<Slice>/` — one folder per inbound trigger (HTTP route OR integration message). Self-contained: handler, endpoint or event/command consumer, DTOs, slice DI extension, inline integration-event construction (no `IIntegrationMap<,>`).
  - `Domain/` — aggregates + value objects + `Abstractions/IShipmentStore.cs` + `Abstractions/ICarrierGateway.cs` + (optional) `Abstractions/IRateShopper.cs`. Zero references to Infrastructure / Features / Contracts.
  - `Contracts/Integration/` — cross-service event payload classes (outbound `Shipment*Event`s + inbound `OrderConfirmedEvent`). Saga commands (`CreateShipmentCommand`, `CancelShipmentCommand`) stay in `ECommerce.Shared.IntegrationEvents.Commands` (consumed, not owned).
  - `Infrastructure/Data/EntityFramework/` — pure `ShippingContext` (DbContext only) + new `EfShipmentStore` + EF configs + `ShippingContextDesignTimeFactory` + `ShippingContextSeed` + `ShippingQaFixtures`.
  - `Infrastructure/Carriers/` — `FakeExpressCarrierGateway`, `FakeGroundCarrierGateway`, `FakeCarrierDispatchRegistry`, `FakeCarrierWebhookParser`, `CarrierStatusApplier`, `CarrierPollingService` (hosted), `RateShoppingService`, `CarrierWebhookOptions`.
  - `Infrastructure/Observability/` — `ShippingMetrics`.
  - `Infrastructure/Outbox/` — `InternalOutboxEndpoints`.
- **Namespaces**: `Shipping.Service.Domain`, `Shipping.Service.Domain.Abstractions`, `Shipping.Service.Features.<Slice>`, `Shipping.Service.Contracts.Integration`, `Shipping.Service.Infrastructure.Data.EntityFramework`, `Shipping.Service.Infrastructure.Carriers`, `Shipping.Service.Infrastructure.Observability`, `Shipping.Service.Infrastructure.Outbox`. The `Shipping.Service.Models`, `Shipping.Service.Carriers`, `Shipping.Service.Observability`, `Shipping.Service.Endpoints`, `Shipping.Service.IntegrationEvents` namespaces are retired.
- **HTTP routes**: unchanged — `GET /by-order/{orderId:guid}`, `GET /{shipmentId:guid}`, `GET /` (list), `POST /{shipmentId:guid}/pick`, `/pack`, `/dispatch`, `/deliver`, `/fail`, `/return`, `/cancel`, `POST /webhooks/carrier/{carrierKey}`, `GET /health`, `GET /internal/outbox/failed`. Same verbs, paths, auth requirements, response shapes.
- **Schema**: unchanged. No new EF migrations.
- **Event payloads**: unchanged shape. Only folder + namespace moves.
- **Dispatch**: no MediatR. Endpoints / event consumers take handler via constructor injection, call `HandleAsync(...)` directly. Handlers `internal sealed`, one public async method.
- **Slice DI**: each slice exposes `AddXxxSlice(this IServiceCollection)`; event/command-consumer slices internally call `AddEventHandler<TEvent, THandler>()` from `ECommerce.Shared.Infrastructure.EventBus`.
- **Write path**: load via `IShipmentStore` → call aggregate domain method → persist + emit integration events via `IOutboxUnitOfWork.ExecuteAsync` (from `ECommerce.Shared.Infrastructure.Outbox`).
- **Read path**: project directly from `ShippingContext` to `ShipmentResponse` (bypass `IShipmentStore` and aggregate).
- **Cross-slice rule**: duplicate first, extract on third. NetArchTest forbids `Features.<X>` ↔ `Features.<Y>`. HTTP `CancelShipment` and saga `CancelShipmentCommand` are deliberate duplicates that each construct `ShipmentCancelledEvent` independently in this pilot.
- **Divergences from Order** to honor:
  1. No `IIntegrationMap<,>` / `DomainEventOutboxInterceptor` — shipping already constructs events inline, no `Translate(...)` switch in `ShippingContext` to extract.
  2. `IShipmentStore` split from `DbContext` (matches Order / Inventory shape).
  3. `Carriers/` consolidates under `Infrastructure/Carriers/` (not a peer-layer folder) — `ICarrierGateway` abstraction declared in `Domain/Abstractions/`, implementations stay in `Infrastructure/Carriers/`.
  4. `Observability/ShippingMetrics` moves to `Infrastructure/Observability/` so the top-level folder count matches prior pilots exactly.
  5. HTTP write endpoints split per state transition (`PickShipment`, `PackShipment`, `DispatchShipment`, `DeliverShipment`, `FailShipment`, `ReturnShipment`, `CancelShipment`), not bundled into one terminal-transition slice.
  6. `CarrierPollingService` stays in `Infrastructure/Carriers/` — not a `Features/` slice. The polling tick is an internal pump, not an inbound external trigger.
  7. Saga commands consumed from `ECommerce.Shared`, not owned in local `Contracts/Integration/`.
- **Composition**: composes ADR [0011](../adr/0011-order-cleanarch-vsa-pilot.md) by reference (no new ADR). Reuses [adding-a-new-slice.md](../runbooks/adding-a-new-slice.md) runbook unchanged. Root `CLAUDE.md` gets one new "Shipping service exception" paragraph.
- **`GET /health`**: stays in `Program.cs` (one-line `MapHealthChecks`). No `Features/Health/` slice — matches Inventory/Auth, avoids precedent that ops endpoints become slices.
- **Rollout**: 13 staged commits on `refactor/shipping-vsa`, each green. Single PR for review. Pre-commit hook gates every commit (no `--no-verify`, no validation deferral).
- **Critical files to modify**:
  - `shipping-microservice/Shipping.Service/Endpoints/ShippingApiEndpoints.cs` (~400 LOC, dissolved by Phase 6d)
  - `shipping-microservice/Shipping.Service/Endpoints/InternalOutboxEndpoints.cs` (relocated Phase 8)
  - `shipping-microservice/Shipping.Service/Infrastructure/Data/EntityFramework/ShippingContext.cs` (two-hat split Phase 4)
  - `shipping-microservice/Shipping.Service/Infrastructure/Data/IShipmentStore.cs` (relocated Phase 3)
  - `shipping-microservice/Shipping.Service/Models/*` (9 types relocated Phase 2b)
  - `shipping-microservice/Shipping.Service/IntegrationEvents/*Event.cs` (payloads relocated Phase 2a)
  - `shipping-microservice/Shipping.Service/IntegrationEvents/EventHandlers/*` (3 handlers dissolved Phase 7)
  - `shipping-microservice/Shipping.Service/Carriers/*` (8 types relocated Phase 5)
  - `shipping-microservice/Shipping.Service/Observability/ShippingMetrics.cs` (relocated Phase 5)
  - `shipping-microservice/Shipping.Service/Program.cs` (becomes slice manifest by Phase 8)
  - `shipping-microservice/Shipping.Tests/Api/*` (relocated Phase 9)
  - `shipping-microservice/Shipping.Tests/Carriers/*` (relocated Phase 9 — mirror `Infrastructure/Carriers/`)
- **Critical files to copy/mirror** (prior pilots, do not modify):
  - `inventory-microservice/Inventory.Tests/Architecture/LayoutTests.cs` — NetArchTest rule shape
  - `inventory-microservice/Inventory.Tests/Architecture/LayoutAnalyzerTests.cs` — analyzer test shape
  - `inventory-microservice/Inventory.Service.LayoutAnalyzer/LayoutAnalyzer.cs` — analyzer skeleton + diagnostic IDs (rename `INVLAY***` → `SHPLAY***`)
  - `inventory-microservice/Inventory.Service/Features/Restock/RestockSliceExtensions.cs` (or equivalent) — slice DI extension shape
  - `inventory-microservice/Inventory.Service/Program.cs` — slice-manifest shape

---

## Phase 1: Scaffold NetArchTest + LayoutAnalyzer (rules off)

**User stories**: 15, 16 (boundary enforcement guardrails).

### What to build

Add new `Shipping.Service.LayoutAnalyzer` csproj (copy Inventory analyzer skeleton, rename diagnostic IDs `INVLAY***` → `SHPLAY***`, rules empty / disabled). Wire as `Analyzer` ProjectReference from `Shipping.Service.csproj`. Add `Shipping.Tests/Architecture/LayoutTests.cs` + `Shipping.Tests/Architecture/LayoutAnalyzerTests.cs` with every test marked `[Fact(Skip="enabled in Phase 10")]`. No production code changes.

### Acceptance criteria

- [ ] `dotnet build shipping-microservice` green
- [ ] `dotnet test shipping-microservice/Shipping.Tests` green (skipped tests count > 0)
- [ ] `dotnet format --verify-no-changes` green
- [ ] Commit: `refactor(shipping): Phase 1 scaffold NetArchTest + LayoutAnalyzer`

---

## Phase 2a: Move integration-event payloads to `Contracts/Integration/`

**User stories**: 18 (namespace match folders), 24 (shared lib untouched).

### What to build

Move the 8 payload classes (`ShipmentCreatedEvent`, `ShipmentDispatchedEvent`, `ShipmentDeliveredEvent`, `ShipmentCancelledEvent`, `ShipmentFailedEvent`, `ShipmentReturnedEvent`, `ShipmentStatusChangedEvent`, consumed `OrderConfirmedEvent`) from `IntegrationEvents/` to `Contracts/Integration/`. Rename namespace to `Shipping.Service.Contracts.Integration`. Leave the 3 `EventHandlers/*Handler.cs` files in `IntegrationEvents/EventHandlers/` for now (Phase 7 dissolves them); fix their `using`s. Fix all other `using`s across `Endpoints/`, `Carriers/`, `Models/`, `Infrastructure/`, tests.

### Acceptance criteria

- [ ] Build green
- [ ] Full `dotnet test shipping-microservice/Shipping.Tests` green
- [ ] `dotnet format --verify-no-changes` green
- [ ] Commit: `refactor(shipping): Phase 2a move integration event payloads to Contracts/`

---

## Phase 2b: Move domain to `Domain/`

**User stories**: 5 (rich domain), 18 (namespaces).

### What to build

Move all 9 `Models/*` types (`Shipment`, `ShipmentLine`, `ShipmentStatus`, `ShipmentStatusHistoryEntry`, `ShipmentStatusSource`, `Money`, `OrderConfirmation`, `ShippingAddress`, `Warehouse`) to `Domain/` with namespace `Shipping.Service.Domain`. No business-logic refactor — pure relocation + namespace rename. Update all consumer `using`s (`Endpoints/`, `Carriers/`, `Infrastructure/`, `Shipping.Tests/`).

### Acceptance criteria

- [ ] Build green
- [ ] Full `dotnet test shipping-microservice/Shipping.Tests` green
- [ ] `Models/` folder deleted
- [ ] Commit: `refactor(shipping): Phase 2b move domain to Domain/`

---

## Phase 3: Move `IShipmentStore` to `Domain/Abstractions/`

**User stories**: 9 (DbContext single-purpose), 18 (namespaces).

### What to build

Move `Infrastructure/Data/IShipmentStore.cs` to `Domain/Abstractions/IShipmentStore.cs` under namespace `Shipping.Service.Domain.Abstractions`. Co-locate any companion result records if present in the same file. `ShippingContext` still implements the interface — Phase 4 splits. Update all consumer `using`s.

### Acceptance criteria

- [ ] Build green
- [ ] Full `Shipping.Tests` green
- [ ] `Infrastructure/Data/IShipmentStore.cs` deleted (now under `Domain/Abstractions/`)
- [ ] Commit: `refactor(shipping): Phase 3 IShipmentStore to Domain/Abstractions/`

---

## Phase 4: Split `EfShipmentStore` out of `ShippingContext`

**User stories**: 9 (DbContext single-purpose).

### What to build

Largest mechanical phase — touches every store method. Strict in-commit migration order to keep build green between sub-steps:

1. Introduce `Infrastructure/Data/EntityFramework/EfShipmentStore.cs`. Constructor takes whatever `ShippingContext` injected today (likely `(ShippingContext ctx)` plus any helpers). Every `IShipmentStore` method either delegates to the still-present `ShippingContext` method or re-implements using `ctx.<DbSet>`. Two implementations coexist; build green.
2. Flip DI registration in `EntityFrameworkExtensions.AddSqlServerDatastore` (or local extension): `services.AddScoped<IShipmentStore, EfShipmentStore>();` replacing the prior `IShipmentStore` → `ShippingContext` resolution. Run full `Shipping.Tests` locally.
3. Delete the now-orphaned `IShipmentStore` method bodies from `ShippingContext`; remove `, IShipmentStore` from class declaration; reduce class to `DbContext` base + `DbSet<>` properties + `OnModelCreating` + any persistence-only helpers.

Single commit for all three sub-steps — splitting across commits would land a misleading "two impls coexist" or "DI flipped but methods still on context" state on bisect.

### Acceptance criteria

- [ ] Build green after each sub-step
- [ ] Full `dotnet test shipping-microservice/Shipping.Tests` green at end (manual — hook only runs Basket tests)
- [ ] `ShippingContext.cs` LOC drops to DbContext + DbSets + `OnModelCreating` + persistence-only helpers
- [ ] No `IShipmentStore` interface in `ShippingContext` declaration
- [ ] Commit: `refactor(shipping): Phase 4 split EfShipmentStore from ShippingContext`

---

## Phase 5: Relocate Carriers + Observability + extract `ICarrierGateway` abstraction

**User stories**: 10 (carriers under Infrastructure/), 11 (ICarrierGateway in Domain/Abstractions/), 13 (CarrierPollingService stays infra), 14 (ShippingMetrics in Infrastructure/Observability/).

### What to build

Three relocations bundled (all mechanical, all share usings churn — one commit avoids three rounds of namespace fix-ups):

1. **Move `Carriers/` → `Infrastructure/Carriers/`** with namespace `Shipping.Service.Infrastructure.Carriers`. Move all 8 files: `FakeExpressCarrierGateway`, `FakeGroundCarrierGateway`, `FakeCarrierDispatchRegistry`, `FakeCarrierWebhookParser`, `CarrierStatusApplier`, `CarrierPollingService`, `RateShoppingService`, `CarrierWebhookOptions`. `ICarrierGateway.cs` does **not** move with this group — see step 2.
2. **Extract `ICarrierGateway` to `Domain/Abstractions/ICarrierGateway.cs`** under namespace `Shipping.Service.Domain.Abstractions`. The two implementations (`FakeExpressCarrierGateway`, `FakeGroundCarrierGateway`) reference the new namespace; their files stay in `Infrastructure/Carriers/`.
3. **Move `Observability/ShippingMetrics.cs` → `Infrastructure/Observability/ShippingMetrics.cs`** with namespace `Shipping.Service.Infrastructure.Observability`. Delete the empty top-level `Observability/` folder.

Update all consumer `using`s: `Program.cs`, `Endpoints/ShippingApiEndpoints.cs`, `IntegrationEvents/EventHandlers/*`, `Shipping.Tests/Carriers/*` (note: test files stay where they are; Phase 9 reshapes them). `IRateShopper` Domain abstraction is **deferred** to Phase 6b — only introduce if a slice handler needs to mock it.

### Acceptance criteria

- [ ] Build green
- [ ] Full `Shipping.Tests` green (`CarrierGatewayContractTests`, `CarrierPollingServiceTests`, `CarrierStatusApplierTests`, `RateShoppingServiceTests` all green against new namespaces)
- [ ] `Carriers/` (top-level) folder deleted
- [ ] `Observability/` (top-level) folder deleted
- [ ] `Domain/Abstractions/ICarrierGateway.cs` exists; implementations in `Infrastructure/Carriers/`
- [ ] `CarrierPollingService` (hosted service registration in `Program.cs`) still wired and resolves
- [ ] Commit: `refactor(shipping): Phase 5 relocate Carriers + Observability + ICarrierGateway abstraction`

---

## Phase 6a: Extract read slices

**User stories**: 3 (one folder per HTTP route), 6 (read slices project directly from EF).

### What to build

Carve `Features/GetShipmentsByOrder/`, `Features/GetShipmentById/`, `Features/ListShipments/`. Each owns: endpoint class (returns `TypedResults.*`), `internal sealed` handler with one public async method that projects directly from `ShippingContext` to `ShipmentResponse` (bypasses `IShipmentStore` and aggregate), response DTOs (reuse existing `ApiModels/ShipmentResponse.cs` — move into the first read slice that owns it, or duplicate across the 3 read slices per the duplicate-first rule), `AddXxxSlice(this IServiceCollection)` extension. Wire each into `Program.cs`. Remove the corresponding 3 GET lambdas + their shared private helpers (`ToResponse`, `IsAuthorizedForShipments`, `IsAuthorizedForShipment`) from `Endpoints/ShippingApiEndpoints.cs` — duplicate the helpers per slice (rule-of-three; the third reuse triggers extraction). Preserve ownership/auth checks (`Administrator` role bypass + `customerId` claim match) byte-for-byte.

### Acceptance criteria

- [ ] Build green
- [ ] `GetShipmentsByOrderTests`, `ListShipmentsTests`, and the GET-side cases of `ShipmentOwnershipTests` green
- [ ] Full `Shipping.Tests` green
- [ ] Commit: `refactor(shipping): Phase 6a extract read slices`

---

## Phase 6b: Extract write slices group 1 (Pick, Pack, Dispatch — lifecycle progress)

**User stories**: 7 (write path through IShipmentStore + outbox UoW), 8 (slice constructs its own integration events).

### What to build

Carve `Features/PickShipment/`, `Features/PackShipment/`, `Features/DispatchShipment/`. Each owns: endpoint, handler, request DTO if present, `AddXxxSlice` extension. Each write handler: load via `IShipmentStore` → call `Shipment` domain method (e.g., `MarkPicked`, `MarkPacked`, `Dispatch`) → wrap in `IOutboxUnitOfWork.ExecuteAsync` → construct integration events inline (`ShipmentDispatchedEvent` + `ShipmentStatusChangedEvent` for Dispatch; just `ShipmentStatusChangedEvent` for Pick/Pack — match current behavior exactly). `DispatchShipment` slice injects `RateShoppingService` directly from `Infrastructure/Carriers/` (or introduces `IRateShopper` Domain abstraction now if a unit test wants to mock it — defer if not). Wire into `Program.cs`. Remove the corresponding 3 POST lambdas from `Endpoints/ShippingApiEndpoints.cs`. Preserve auth (`Administrator` role) and ownership checks verbatim.

### Acceptance criteria

- [ ] Build green
- [ ] `ShipmentDispatchEndpointsTests` green
- [ ] `ShipmentTransitionEndpointsTests` cases for Pick/Pack green
- [ ] Outbox UoW ordering identical (`ShippingMetrics` counters still emit from new call sites)
- [ ] Full `Shipping.Tests` green
- [ ] Commit: `refactor(shipping): Phase 6b extract Pick/Pack/Dispatch slices`

---

## Phase 6c: Extract write slices group 2 (Deliver, Fail, Return, Cancel — terminal transitions)

**User stories**: 7 (write path), 8 (slice constructs integration events), 20 (HTTP CancelShipment is its own slice, deliberately distinct from saga CancelShipmentCommand).

### What to build

Carve `Features/DeliverShipment/`, `Features/FailShipment/`, `Features/ReturnShipment/`, `Features/CancelShipment/`. Each owns: endpoint, handler, request DTO if present (`TerminalTransitionRequests.cs` provides shared request shapes today — Phase 6c **duplicates** the request type per slice rather than introducing `Features/Shared/`, honoring the duplicate-first rule), `AddXxxSlice` extension. Each handler: load via `IShipmentStore` → call `Shipment` domain method (`MarkDelivered`, `MarkFailed`, `MarkReturned`, `Cancel`) → wrap in `IOutboxUnitOfWork.ExecuteAsync` → construct terminal integration event inline (`ShipmentDeliveredEvent` / `ShipmentFailedEvent` / `ShipmentReturnedEvent` / `ShipmentCancelledEvent`) **plus** `ShipmentStatusChangedEvent` per current behavior. The HTTP `CancelShipment` slice **must remain independent** of the saga `CancelShipmentCommand` slice (Phase 7) — both construct `ShipmentCancelledEvent` independently, no shared helper. Wire into `Program.cs`. Remove the corresponding 4 POST lambdas from `Endpoints/ShippingApiEndpoints.cs`. Preserve auth + ownership checks verbatim.

### Acceptance criteria

- [ ] Build green
- [ ] `ShipmentTerminalTransitionTests`, `ShipmentTransitionEndpointsTests` (Deliver/Fail/Return/Cancel cases), `ShipmentOwnershipTests` (write cases) green
- [ ] Full `Shipping.Tests` green
- [ ] `ApiModels/TerminalTransitionRequests.cs` either deleted (if duplicated) or retained pending Phase 6d cleanup
- [ ] Commit: `refactor(shipping): Phase 6c extract Deliver/Fail/Return/Cancel slices`

---

## Phase 6d: Extract `ProcessCarrierWebhook` + retire `ShippingApiEndpoints.cs`

**User stories**: 12 (single ProcessCarrierWebhook slice dispatches per-carrier internally).

### What to build

Carve `Features/ProcessCarrierWebhook/`. Owns: endpoint (`POST /webhooks/carrier/{carrierKey}`), handler, `AddXxxSlice` extension. Handler: validate `carrierKey` against `CarrierWebhookOptions.SharedSecrets` for signature verification (preserved verbatim), parse via `FakeCarrierWebhookParser` (still in `Infrastructure/Carriers/`), apply via `CarrierStatusApplier`. Wire into `Program.cs`. Delete `Endpoints/ShippingApiEndpoints.cs` (now empty). Move `GET /health` registration into `Program.cs` as inline `MapHealthChecks("/health")` (one line) if currently inside the endpoints file. Verify no orphan `using Shipping.Service.Endpoints` remains anywhere in the service or tests. Delete any now-orphaned `ApiModels/*Request.cs` files whose contents were duplicated into slices.

### Acceptance criteria

- [ ] Build green
- [ ] `ShipmentWebhookTests` + `HealthChecksTests` green
- [ ] `Endpoints/ShippingApiEndpoints.cs` deleted; `Endpoints/` folder now contains only `InternalOutboxEndpoints.cs` (Phase 8 relocates it)
- [ ] `ApiModels/` folder gone (contents distributed to slices)
- [ ] Full `Shipping.Tests` green
- [ ] Commit: `refactor(shipping): Phase 6d extract ProcessCarrierWebhook + retire ShippingApiEndpoints`

---

## Phase 7: Extract event/command-consumer slices

**User stories**: 4 (event-driven features feel identical to HTTP), 8 (slice-local integration-event construction), 20 (saga CancelShipmentCommand is its own slice), 31 (CausationId/SagaId propagation unchanged).

### What to build

Carve `Features/OrderConfirmed/`, `Features/CreateShipmentCommand/`, `Features/CancelShipmentCommand/`. Each owns: event-handler class (implements `IEventHandler<TEvent>`), `internal sealed` slice handler with the business logic, inline integration-event construction (`OrderConfirmed` produces `ShipmentCreatedEvent` per current behavior; `CreateShipmentCommand` likewise; `CancelShipmentCommand` produces `ShipmentCancelledEvent` — **deliberate duplicate of HTTP `Features/CancelShipment/` Phase 6c**, no shared helper). Each slice's `AddXxxSlice` calls `AddEventHandler<TEvent, THandler>()`. Replace `Program.cs` per-handler `AddEventHandler` block (currently `.AddEventHandler<OrderConfirmedEvent, OrderConfirmedEventHandler>().AddEventHandler<CreateShipmentCommand, CreateShipmentCommandHandler>().AddEventHandler<CancelShipmentCommand, CancelShipmentCommandHandler>()`) with fluent chain of `AddXxxSlice()` calls. Delete `IntegrationEvents/EventHandlers/` folder. Delete the now-empty `IntegrationEvents/` folder (payloads moved Phase 2a). Confirm `CausationId` / `SagaId` propagation paths through the new slice handlers are byte-for-byte identical to the pre-refactor handlers.

### Acceptance criteria

- [ ] Build green
- [ ] `CancelShipmentCommandHandlerTests`, `CreateShipmentCommandHandlerTests`, any OrderConfirmed tests all green
- [ ] `IntegrationEvents/EventHandlers/` folder deleted
- [ ] `IntegrationEvents/` folder deleted
- [ ] Full `Shipping.Tests` green including `MessagingProviderBootTests` (provider switch still boots) and `EventStreamConsolidationTests`
- [ ] Commit: `refactor(shipping): Phase 7 extract event/command-consumer slices`

---

## Phase 8: Relocate `InternalOutboxEndpoints` + `Program.cs` becomes slice manifest

**User stories**: 21 (ops plumbing out of feature manifest), 30 (DLQ poller call still works).

### What to build

Move `Endpoints/InternalOutboxEndpoints.cs` to `Infrastructure/Outbox/InternalOutboxEndpoints.cs` with namespace `Shipping.Service.Infrastructure.Outbox`. Delete the now-empty `Endpoints/` folder. Reshape `Program.cs` into a slice manifest: chained `AddXxxSlice()` registration block + `app.MapXxxSlice()` mapping block + `app.RegisterInternalOutboxEndpoints()` (or equivalent) + `MapHealthChecks("/health")` + `CarrierPollingService` hosted-service registration retained as-is. `RequireService` policy gate preserved on `/internal/outbox/failed`.

### Acceptance criteria

- [ ] Build green
- [ ] `InternalOutboxEndpointsTests` green (DLQ poller route still gated)
- [ ] `Endpoints/` folder deleted
- [ ] `Program.cs` reads as a manifest (slice registrations + mappings + ops endpoints + hosted services)
- [ ] Full `Shipping.Tests` green
- [ ] Commit: `refactor(shipping): Phase 8 relocate InternalOutboxEndpoints + Program.cs manifest`

---

## Phase 9: Reshape `Shipping.Tests` to mirror slices

**User stories**: 19 (tests mirror Features/<Slice>/, Domain/ kept separate).

### What to build

Move the existing test classes from `Shipping.Tests/Api/` and `Shipping.Tests/Carriers/` to `Shipping.Tests/Features/<Slice>/` and `Shipping.Tests/Infrastructure/Carriers/` per PRD Testing Decisions mapping:

- `GetShipmentsByOrderTests.cs` → `Features/GetShipmentsByOrder/`
- `ListShipmentsTests.cs` → `Features/ListShipments/`
- `ShipmentDispatchEndpointsTests.cs` → `Features/DispatchShipment/`
- `ShipmentTerminalTransitionTests.cs`, `ShipmentTransitionEndpointsTests.cs` → split across `Features/PickShipment/`, `Features/PackShipment/`, `Features/DeliverShipment/`, `Features/FailShipment/`, `Features/ReturnShipment/`, `Features/CancelShipment/` based on the transition each test method exercises (keep file-per-slice; if a test exercises two transitions, duplicate it into both — rule-of-three applies to test code too)
- `ShipmentOwnershipTests.cs` → split per slice that enforces ownership (read slices + write slices that re-check ownership)
- `ShipmentWebhookTests.cs` → `Features/ProcessCarrierWebhook/`
- `CancelShipmentCommandHandlerTests.cs` → `Features/CancelShipmentCommand/`
- `CreateShipmentCommandHandlerTests.cs` → `Features/CreateShipmentCommand/`
- `InternalOutboxEndpointsTests.cs` → `Infrastructure/Outbox/` (mirror)
- `HealthChecksTests.cs`, `Authentication/*` → top-level (cross-cutting)
- `Carriers/CarrierGatewayContractTests.cs`, `Carriers/CarrierPollingServiceTests.cs`, `Carriers/CarrierStatusApplierTests.cs`, `Carriers/RateShoppingServiceTests.cs` → `Infrastructure/Carriers/` (mirror)
- `IntegrationEvents/EventStreamConsolidationTests.cs`, `IntegrationEvents/MessagingProviderBootTests.cs` → either `Infrastructure/Outbox/` or kept under a `IntegrationEvents/` root (platform plumbing tests — decision: keep under `IntegrationEvents/` root, since they exercise messaging-provider boot, not a slice)

Keep `Domain/ShipmentTests.cs`, `Domain/QaSeedFixturesTests.cs` in `Shipping.Tests/Domain/`. Keep `IntegrationTestBase.cs` + `ShippingWebApplicationFactory.cs` at project root. Delete the emptied `Api/` and `Carriers/` test folders. Namespace updates only — no behavior change.

### Acceptance criteria

- [ ] Build green
- [ ] Full `dotnet test shipping-microservice/Shipping.Tests` green (zero behavior diff)
- [ ] `Shipping.Tests/Api/` folder deleted
- [ ] `Shipping.Tests/Carriers/` folder deleted (contents under `Infrastructure/Carriers/`)
- [ ] `Shipping.Tests/Features/` folder count = slice count
- [ ] Commit: `refactor(shipping): Phase 9 reshape Shipping.Tests into Features/`

---

## Phase 10: Enable NetArchTest + LayoutAnalyzer rules

**User stories**: 15, 16, 17, 29 (boundaries enforced; AI edits cannot drift).

### What to build

Unskip `LayoutTests.cs` + `LayoutAnalyzerTests.cs`. Fill in NetArchTest rules:

- `Shipping.Service.Domain.*` must not depend on `Shipping.Service.Infrastructure.*`, `Shipping.Service.Features.*`, `Shipping.Service.Contracts.*`
- `Shipping.Service.Features.<X>` must not depend on `Shipping.Service.Features.<Y>` for distinct slices
- `Shipping.Service.Infrastructure.*` may reference only `Domain` + `Contracts` (+ allowed shared-lib namespaces)
- `Shipping.Service.Contracts.*` must not reference anything internal beyond `Shipping.Service.Contracts.*`

Promote `Shipping.Service.LayoutAnalyzer` diagnostics from hidden to error severity (`.editorconfig` or analyzer manifest). Fill in analyzer banned-namespace / banned-symbol diagnostics mirroring `Inventory.Service.LayoutAnalyzer` with `SHPLAY***` IDs.

### Acceptance criteria

- [ ] `dotnet build shipping-microservice` green (analyzer doesn't fire on existing code — proves the refactor satisfies the rules)
- [ ] Full `Shipping.Tests` green including all unskipped Architecture tests
- [ ] `LayoutAnalyzerTests.cs` proves each rule fires on synthetic violation input
- [ ] Commit: `refactor(shipping): Phase 10 enforce layout boundaries`

---

## Phase 11: Docs — root `CLAUDE.md` Shipping exception paragraph

**User stories**: 26 (composes ADR 0011 by reference), 27 (root CLAUDE.md exception paragraph documents divergences).

### What to build

Add one paragraph to root `CLAUDE.md` under the existing pilot-exception block (after the Inventory paragraph), matching the Order/Product/Basket/Auth/Inventory style:

> **Shipping service exception** — sixth Clean Architecture + Vertical Slices pilot, same layout as Order/Product/Basket/Inventory: `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/`. Boundaries enforced by NetArchTest (`Shipping.Tests/Architecture/LayoutTests.cs`) and the Roslyn `Shipping.Service.LayoutAnalyzer`. Composes ADR [0011](docs/adr/0011-order-cleanarch-vsa-pilot.md) by reference (no new ADR); reuses the [adding-a-new-slice.md](docs/runbooks/adding-a-new-slice.md) runbook unchanged. **Diverges from Order: no `IIntegrationMap<,>` / outbox interceptor seam (Shipping constructs integration events inline per slice — no DbContext-level translation switch to extract, matches Inventory); `IShipmentStore` lives in `Domain/Abstractions/` with `EfShipmentStore` in Infrastructure (matches Order/Inventory); carrier adapters (`FakeExpressCarrierGateway`, `FakeGroundCarrierGateway`, `FakeCarrierDispatchRegistry`, `FakeCarrierWebhookParser`, `CarrierStatusApplier`, `CarrierPollingService`, `RateShoppingService`, `CarrierWebhookOptions`) consolidated under `Infrastructure/Carriers/` with `ICarrierGateway` abstraction in `Domain/Abstractions/`; `ShippingMetrics` moved to `Infrastructure/Observability/` (no peer-layer `Observability/` folder); HTTP write endpoints split per state transition (`PickShipment`, `PackShipment`, `DispatchShipment`, `DeliverShipment`, `FailShipment`, `ReturnShipment`, `CancelShipment`, `ProcessCarrierWebhook`); HTTP `CancelShipment` and saga `CancelShipmentCommand` are two distinct slices that each construct `ShipmentCancelledEvent` independently; `CarrierPollingService` (hosted) stays in `Infrastructure/Carriers/`, not a `Features/` slice; saga commands (`CreateShipmentCommand`/`CancelShipmentCommand`) consumed from `ECommerce.Shared.IntegrationEvents.Commands`, not owned in local `Contracts/Integration/`.** Propagation to remaining services (payment, saga) is a separate ADR.

No new ADR. No runbook changes.

### Acceptance criteria

- [ ] `CLAUDE.md` contains the new paragraph; existing pilot paragraphs unchanged
- [ ] `dotnet format --verify-no-changes` green
- [ ] Markdown links resolve (ADR 0011 + adding-a-new-slice)
- [ ] Commit: `refactor(shipping): Phase 11 docs root CLAUDE.md Shipping exception`

---

## Verification (end-to-end, after Phase 11)

Run each from a clean `dotnet restore`:

1. **Format + build + test full Shipping stack**
   ```bash
   find shipping-microservice -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
   cd shipping-microservice && dotnet restore && dotnet format --verify-no-changes && dotnet build && dotnet test
   ```
   Expected: all green.

2. **Pre-commit hook on the final commit**
   ```bash
   dotnet husky run --group pre-commit
   ```
   Expected: green (format + build + Basket tests).

3. **End-to-end stack smoke**
   ```bash
   docker compose up --build
   ```
   Then via Bruno/curl against `http://localhost:8006`:
   - `GET /by-order/{orderId}` (auth) → 200 + array OR 404 + 403 per ownership
   - `GET /{shipmentId}` (auth) → 200 / 404 / 403
   - `GET /` (list, Admin) → 200 + array
   - Saga path: produce a `CreateShipmentCommand` (via saga service or test publisher) → verify `ShipmentCreatedEvent` carries identical `CausationId` / `SagaId`
   - `POST /{shipmentId}/pick` (Admin) → 200; verify `ShipmentStatusChangedEvent` published (RabbitMQ Mgmt UI or service-bus explorer)
   - `POST /{shipmentId}/pack` → 200
   - `POST /{shipmentId}/dispatch` → 200; verify `ShipmentDispatchedEvent` + `ShipmentStatusChangedEvent` published; `ShippingMetrics` dispatch counter increments
   - `POST /{shipmentId}/deliver` → 200; verify `ShipmentDeliveredEvent`
   - `POST /{shipmentId}/fail` → 200; verify `ShipmentFailedEvent`
   - `POST /{shipmentId}/return` → 200; verify `ShipmentReturnedEvent`
   - `POST /{shipmentId}/cancel` → 200; verify `ShipmentCancelledEvent`
   - `POST /webhooks/carrier/express` with a fake signed payload → 200; verify `CarrierStatusApplier` ran and `Shipment` advanced
   - Saga `CancelShipmentCommand` → verify identical `ShipmentCancelledEvent` payload shape as HTTP cancel
   - `GET /internal/outbox/failed` with user token → 403; with service token → 200
   - `GET /health` → 200

4. **Boundary regression check**
   Add a deliberate violation locally (e.g. `Domain/Shipment.cs` adds `using Shipping.Service.Contracts.Integration;`); confirm:
   - `dotnet build` fails with `SHPLAY***` analyzer diagnostic
   - `dotnet test Shipping.Tests --filter LayoutTests` fails the matching NetArchTest assertion
   Revert.

5. **DLQ poller still ingests Shipping failures**
   In a stack run, induce a poison-message scenario and confirm the API gateway DLQ poller still persists Shipping rows from `/internal/outbox/failed`.

6. **Carrier polling tick still pumps**
   Watch logs / `ShippingMetrics` counters during a stack run; `CarrierPollingService` continues to tick at `PollingIntervalSeconds` and apply status updates via `CarrierStatusApplier`. Webhook signature verification under `POST /webhooks/carrier/{carrierKey}` rejects unsigned payloads exactly as before.

7. **Metrics parity**
   Hit Prometheus `/metrics` endpoint on Shipping and confirm `ShippingMetrics` counters (dispatch/deliver/fail/return/cancel/webhook) still emit with identical names / labels.

8. **PR open + bisect spot-check**
   Open single PR `refactor/shipping-vsa` → `main`. `git bisect` any 3 random commits in the branch range and confirm each builds + tests green in isolation.

## Phases needing manual `dotnet test shipping-microservice/Shipping.Tests` before commit

Pre-commit hook only runs Basket tests. Run Shipping tests locally before staging on every phase, but pay especially close attention to behavior-touching phases:

- **Phase 4** — `EfShipmentStore` split (largest behavior-translation surface)
- **Phase 5** — Carriers / Observability relocation (hosted service + DI wiring risk)
- **Phase 6b** — Pick/Pack/Dispatch slices (outbox UoW ordering / event-construction parity / `RateShoppingService` injection)
- **Phase 6c** — Deliver/Fail/Return/Cancel slices (terminal-transition parity; `TerminalTransitionRequests` duplication risk)
- **Phase 6d** — webhook slice (signature verification + `FakeCarrierWebhookParser` integration)
- **Phase 7** — event/command-consumer slices (`AddEventHandler` wire-up only validated by integration tests; HTTP-vs-saga cancel duplicate must produce byte-identical `ShipmentCancelledEvent`)
- **Phase 10** — rule enablement (NetArchTest only fires under `dotnet test`)

If hook fails with `MSB3248`: clean `bin`/`obj` → `dotnet restore --force` → rerun hook (per root `CLAUDE.md` sandbox policy). Do not `--no-verify`, do not defer validation. If still failing, **STOP and hand off to user — do not commit**.
