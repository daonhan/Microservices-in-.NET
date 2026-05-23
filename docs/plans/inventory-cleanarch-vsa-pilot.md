# Plan: Inventory.Service Clean Architecture + Vertical Slice Pilot

> Source PRD: `docs/prd/PRD-Inventory-CleanArch-VSA-Pilot.md`
> Branch: `refactor/inventory-vsa` (already checked out)

## Context

`Inventory.Service` is organized by technical type: 8 HTTP routes inline in one 231-LOC `InventoryApiEndpoints.cs`, 4 event consumers in `IntegrationEvents/EventHandlers/`, 12 domain types in `Models/`, and `InventoryContext` wearing two hats (DbContext + `IInventoryStore` impl). Worse, `Models/StockLevelMonitor.cs` directly references `IntegrationEvents.LowStockEvent` / `StockDepletedEvent` — the exact Domain → Contracts boundary violation the prior 4 pilots forbid. To trace one feature (e.g. saga `ReserveStockCommand`) a contributor jumps across six folders.

This pilot (#5, after Order/Product/Basket/Auth) applies the same Clean Architecture + VSA layout to `Inventory.Service`. Zero functional behavior change. Boundaries enforced twice (NetArchTest + Roslyn analyzer). Intended outcome: each feature owns one `Features/<Slice>/` folder; Domain has zero Contracts references; `IInventoryStore` lives in Domain; `EfInventoryStore` lives in Infrastructure; saga commands continue to flow through the shared lib.

## Architectural decisions

Durable decisions that apply across all phases:

- **Project shape**: single `Inventory.Service.csproj` retained; boundaries enforced by namespace + Roslyn analyzer + NetArchTest, not by csproj split.
- **Folder topology**:
  - `Features/<Slice>/` — one folder per inbound trigger (HTTP route OR integration message). Self-contained: handler, endpoint or event handler, DTOs, slice DI extension, integration-event construction.
  - `Domain/` — aggregates + value objects + `StockLevelMonitor` (refactored) + `Domain/Abstractions/IInventoryStore.cs` + result records. Zero references to Infrastructure / Features / Contracts.
  - `Contracts/Integration/` — cross-service event payload classes. Saga commands stay in `ECommerce.Shared.IntegrationEvents.Commands` (consumed, not owned).
  - `Infrastructure/Data/EntityFramework/` — pure `InventoryContext` (DbContext only) + new `EfInventoryStore`.
  - `Infrastructure/Outbox/` — `InternalOutboxEndpoints`.
- **Namespaces**: `Inventory.Service.Domain`, `Inventory.Service.Domain.Abstractions`, `Inventory.Service.Features.<Slice>`, `Inventory.Service.Contracts.Integration`, `Inventory.Service.Infrastructure.Data.EntityFramework`, `Inventory.Service.Infrastructure.Outbox`. The `Inventory.Service.Models` namespace is retired.
- **HTTP routes**: unchanged — `GET /`, `GET /{productId:int}`, `GET /{productId:int}/movements`, `POST /{productId:int}/restock`, `PUT /{productId:int}/threshold`, `POST /{productId:int}/reserve`, `POST /{productId:int}/backorder`, `GET /health`, `GET /internal/outbox/failed`. Same verbs, paths, auth requirements, response shapes.
- **Schema**: unchanged. No new EF migrations.
- **Event payloads**: unchanged shape. Only folder + namespace moves.
- **Dispatch**: no MediatR. Endpoints / event consumers take handler via constructor injection, call `HandleAsync(...)` directly. Handlers `internal sealed`, one public async method.
- **Slice DI**: each slice exposes `AddXxxSlice(this IServiceCollection)`; event-consumer slices internally call `AddEventHandler<TEvent, THandler>()` from `ECommerce.Shared.Infrastructure.EventBus`.
- **Write path**: load via `IInventoryStore` → call aggregate domain method → persist + emit integration events via `IOutboxUnitOfWork.ExecuteAsync` (from `ECommerce.Shared.Infrastructure.Outbox`).
- **Read path**: project directly from `InventoryContext` to response DTOs (bypass `IInventoryStore` and the aggregate).
- **Cross-slice rule**: duplicate first, extract on third. NetArchTest forbids `Features.<X>` ↔ `Features.<Y>`. `ReserveByHttp` and `ReserveStock` are deliberate duplicates that both construct `StockReservedEvent` independently in this pilot.
- **Divergences from Order** to honor:
  1. No `IIntegrationMap<,>` / `DomainEventOutboxInterceptor` — Inventory already constructs events inline, no `Translate(...)` switch to extract.
  2. `IInventoryStore` split from `DbContext` (matches Order shape).
  3. `StockLevelMonitor` returns domain records (`LowStockCrossing?`, `StockDepletion?`), slices map to Contracts events.
  4. Saga commands consumed from `ECommerce.Shared`, not owned in local `Contracts/Integration/`.
- **Composition**: composes ADR [0011](../docs/adr/0011-order-cleanarch-vsa-pilot.md) by reference (no new ADR). Reuses [adding-a-new-slice.md](../docs/runbooks/adding-a-new-slice.md) runbook unchanged. Root `CLAUDE.md` gets one new "Inventory service exception" paragraph.
- **`GET /health`**: stays in `Program.cs` (one-line `MapHealthChecks`). No `Features/Health/` slice — matches Auth, avoids precedent that ops endpoints become slices.
- **Rollout**: 13 staged commits on `refactor/inventory-vsa`, each green. Single PR for review. Pre-commit hook gates every commit (no `--no-verify`, no validation deferral).
- **Critical files to modify**:
  - `inventory-microservice/Inventory.Service/Endpoints/InventoryApiEndpoints.cs` (231 LOC, dissolved by Phase 5c)
  - `inventory-microservice/Inventory.Service/Endpoints/InternalOutboxEndpoints.cs` (relocated Phase 7)
  - `inventory-microservice/Inventory.Service/Models/StockLevelMonitor.cs` (return-type refactor Phase 2b)
  - `inventory-microservice/Inventory.Service/Infrastructure/Data/EntityFramework/InventoryContext.cs` (two-hat split Phase 4)
  - `inventory-microservice/Inventory.Service/Infrastructure/Data/IInventoryStore.cs` (relocated Phase 3)
  - `inventory-microservice/Inventory.Service/IntegrationEvents/` (payloads relocated Phase 2a, handlers dissolved Phase 6)
  - `inventory-microservice/Inventory.Service/Program.cs` (becomes slice manifest by Phase 7)
  - `inventory-microservice/Inventory.Tests/Api/*` (relocated Phase 8)
- **Critical files to copy/mirror** (prior pilots, do not modify):
  - `auth-microservice/Auth.Tests/Architecture/LayoutTests.cs` — NetArchTest rule shape
  - `auth-microservice/Auth.Tests/Architecture/LayoutAnalyzerTests.cs` — analyzer test shape
  - `auth-microservice/Auth.Service.LayoutAnalyzer/LayoutAnalyzer.cs` — analyzer skeleton + diagnostic IDs (rename `AUT***` → `INV***`)
  - `auth-microservice/Auth.Service/Features/Login/LoginSliceExtensions.cs` — slice DI extension shape
  - `auth-microservice/Auth.Service/Program.cs` — slice-manifest shape

---

## Phase 1: Scaffold NetArchTest + LayoutAnalyzer (rules off)

**User stories**: 12, 13 (boundary enforcement guardrails).

### What to build

Add new `Inventory.Service.LayoutAnalyzer` csproj (copy Auth analyzer skeleton, rename diagnostic IDs `AUTLAY00*` → `INVLAY00*`, rules empty / disabled). Wire as `Analyzer` ProjectReference from `Inventory.Service.csproj`. Add `Inventory.Tests/Architecture/LayoutTests.cs` + `Inventory.Tests/Architecture/LayoutAnalyzerTests.cs` with every test marked `[Fact(Skip="enabled in Phase 9")]`. No production code changes.

### Acceptance criteria

- [ ] `dotnet build inventory-microservice` green
- [ ] `dotnet test inventory-microservice/Inventory.Tests` green (skipped tests count > 0)
- [ ] `dotnet format --verify-no-changes` green
- [ ] Commit: `refactor(inventory): Phase 1 scaffold NetArchTest + LayoutAnalyzer`

---

## Phase 2a: Move integration-event payloads to `Contracts/Integration/`

**User stories**: 16 (namespace match folders), 21 (shared lib untouched).

### What to build

Move the 8 payload classes (`StockReservedEvent`, `StockReservationFailedEvent`, `StockCommittedEvent`, `StockReleasedEvent`, `StockAdjustedEvent`, `StockDepletedEvent`, `LowStockEvent`, consumed `ProductCreatedEvent`) from `IntegrationEvents/` to `Contracts/Integration/`. Rename namespace to `Inventory.Service.Contracts.Integration`. Leave the 4 `EventHandlers/*Handler.cs` files in `IntegrationEvents/EventHandlers/` for now (Phase 6 dissolves them); fix their `using`s. Fix all other `using`s across `Endpoints/`, `Models/StockLevelMonitor.cs`, `Infrastructure/`, tests.

### Acceptance criteria

- [ ] Build green
- [ ] Full `dotnet test inventory-microservice/Inventory.Tests` green
- [ ] `dotnet format --verify-no-changes` green
- [ ] Commit: `refactor(inventory): Phase 2a move integration event payloads to Contracts/`

---

## Phase 2b: Move domain to `Domain/` + refactor `StockLevelMonitor`

**User stories**: 5 (rich domain), 10 (Monitor returns domain records), 11 (slices map crossings to Contracts events), 16 (namespaces).

### What to build

Move all 12 `Models/*` types to `Domain/` with namespace `Inventory.Service.Domain`. Add `Domain/LowStockCrossing.cs` record `(ProductId, WarehouseId, AvailableAfter, ThresholdAfter)` and `Domain/StockDepletion.cs` record `(ProductId, WarehouseId)`. Rewrite `StockLevelMonitor.TryLowStockCrossing` / `TryDepletedCrossing` to return those records (delete the `using Inventory.Service.Contracts.Integration` line). In the same commit update the only two callers — the inline lambdas inside `Endpoints/InventoryApiEndpoints.cs` for `POST /{productId}/restock` and `PUT /{productId}/threshold` — to map the returned record into `LowStockEvent` / `StockDepletedEvent` before adding to the outbox event list. Update `Inventory.Tests/Domain/StockLevelMonitorTests.cs` to assert against the records.

### Acceptance criteria

- [ ] Build green
- [ ] `Inventory.Tests/Domain/StockLevelMonitorTests.cs` green against new return types
- [ ] `RestockApiTests` + `ThresholdApiTests` green (caller-mapping parity)
- [ ] Full `dotnet test inventory-microservice/Inventory.Tests` green
- [ ] Commit: `refactor(inventory): Phase 2b move domain to Domain/ + StockLevelMonitor returns crossings`

---

## Phase 3: Move `IInventoryStore` to `Domain/Abstractions/`

**User stories**: 9 (DbContext single-purpose), 16 (namespaces).

### What to build

Move `Infrastructure/Data/IInventoryStore.cs` to `Domain/Abstractions/IInventoryStore.cs`. Co-locate companion result records (`RestockResult`, `SetThresholdResult`, `ReserveLine`, `ReservedLine`, `FailedReserveLine`, `ReserveResult`, `CommittedLine`, `CommitResult`, `ReleasedLine`, `ReleaseResult`, `BackorderResult`, `FulfilledBackorder`) into the same folder under namespace `Inventory.Service.Domain.Abstractions`. `InventoryContext` still implements the interface — Phase 4 will split. Update all consumer `using`s.

### Acceptance criteria

- [ ] Build green
- [ ] Full `Inventory.Tests` green
- [ ] Commit: `refactor(inventory): Phase 3 IInventoryStore to Domain/Abstractions/`

---

## Phase 4: Split `EfInventoryStore` out of `InventoryContext`

**User stories**: 9 (DbContext single-purpose).

### What to build

The largest phase — touches every store method. Strict in-commit migration order to keep build green between sub-steps:

1. Introduce `Infrastructure/Data/EntityFramework/EfInventoryStore.cs`. Constructor `(InventoryContext ctx, MetricFactory mf)`. Every `IInventoryStore` method either delegates to the still-present `InventoryContext` method or re-implements using `ctx.<DbSet>`. Two implementations coexist; build green.
2. Flip DI registration in `EntityFrameworkExtensions.AddSqlServerDatastore` (or `AddInventoryDatastore` local extension): `services.AddScoped<IInventoryStore, EfInventoryStore>();` replacing the prior `IInventoryStore` → `InventoryContext` resolution. Run full `Inventory.Tests` locally.
3. Delete the now-orphaned `IInventoryStore` method bodies from `InventoryContext`; remove `: IInventoryStore` from class declaration; reduce class to `DbContext` base + `DbSet<>` properties + `OnModelCreating` + private `RecordStockMovement` helper.

Single commit for all three sub-steps — splitting across commits would land a misleading "two impls coexist" or "DI flipped but methods still on context" state on bisect.

### Acceptance criteria

- [ ] Build green after each sub-step
- [ ] Full `dotnet test inventory-microservice/Inventory.Tests` green at end (manual — hook only runs Basket tests)
- [ ] `InventoryContext.cs` LOC drops to DbContext + DbSets + `OnModelCreating` + `RecordStockMovement` only
- [ ] No `IInventoryStore` interface in `InventoryContext` declaration
- [ ] Commit: `refactor(inventory): Phase 4 split EfInventoryStore from InventoryContext`

---

## Phase 5a: Extract read slices

**User stories**: 3 (one folder per HTTP route), 6 (read slices project directly from EF).

### What to build

Carve `Features/ListStockItems/`, `Features/GetStockItem/`, `Features/GetStockMovements/`. Each owns: endpoint class (returns `TypedResults.*`), `internal sealed` handler with one public async method that projects directly from `InventoryContext` to response DTO (bypasses `IInventoryStore` and aggregate), response DTOs, `AddXxxSlice(this IServiceCollection)` extension. Wire each into `Program.cs`. Remove the corresponding 3 lambdas + their helpers from `Endpoints/InventoryApiEndpoints.cs`.

### Acceptance criteria

- [ ] Build green
- [ ] `Inventory.Tests/Api/InventoryListApiTests`, `InventoryApiTests` (GET path), `MovementsApiTests` all green
- [ ] Full `Inventory.Tests` green
- [ ] Commit: `refactor(inventory): Phase 5a extract read slices`

---

## Phase 5b: Extract write slices (Restock, SetThreshold, CreateBackorder)

**User stories**: 7 (write path through `IInventoryStore` + outbox UoW), 8 (slice constructs its own integration events).

### What to build

Carve `Features/Restock/`, `Features/SetThreshold/`, `Features/CreateBackorder/`. Each owns: endpoint, handler, request/response DTOs, `AddXxxSlice` extension. Each write handler: load via `IInventoryStore` → call domain method → wrap mutation in `IOutboxUnitOfWork.ExecuteAsync` → construct integration events inline (Restock + SetThreshold call `StockLevelMonitor` and map crossings). Wire into `Program.cs`. Remove the corresponding lambdas from `Endpoints/InventoryApiEndpoints.cs`. Preserve inline validation (`Quantity > 0`, `Threshold >= 0`) verbatim.

### Acceptance criteria

- [ ] Build green
- [ ] `RestockApiTests`, `ThresholdApiTests`, `BackorderApiTests` all green
- [ ] Outbox UoW ordering identical (verify reservation-latency / stock-movements metrics still emit from new call sites)
- [ ] Full `Inventory.Tests` green
- [ ] Commit: `refactor(inventory): Phase 5b extract Restock/SetThreshold/Backorder slices`

---

## Phase 5c: Extract `ReserveByHttp` + retire `InventoryApiEndpoints.cs`

**User stories**: 15 (explicit duplicate of saga ReserveStock).

### What to build

Carve `Features/ReserveByHttp/` — handler constructs `StockReservedEvent` independently (deliberate duplicate of `Features/ReserveStock/` to land in Phase 6; no shared helper). Wire into `Program.cs`. Delete `Endpoints/InventoryApiEndpoints.cs` (now empty). Move `GET /health` into `Program.cs` as inline `MapHealthChecks("/health")` (one line). Verify no orphan `using Inventory.Service.Endpoints` remains anywhere in the service or tests.

### Acceptance criteria

- [ ] Build green
- [ ] `ReserveApiTests` + `HealthChecksTests` green
- [ ] `Endpoints/InventoryApiEndpoints.cs` deleted; `Endpoints/` folder now contains only `InternalOutboxEndpoints.cs` (Phase 7 relocates it)
- [ ] Full `Inventory.Tests` green
- [ ] Commit: `refactor(inventory): Phase 5c extract ReserveByHttp + retire InventoryApiEndpoints`

---

## Phase 6: Extract event-consumer slices

**User stories**: 4 (event-driven features feel identical to HTTP), 8 (slice-local integration-event construction), 28 (CausationId/SagaId propagation unchanged).

### What to build

Carve `Features/ProductCreated/`, `Features/ReserveStock/`, `Features/CommitStock/`, `Features/ReleaseStock/`. Each owns: event-handler class (implements `IEventHandler<TEvent>`), `internal sealed` slice handler with the business logic, inline integration-event construction (`Commit` and `Release` build their respective events; `ReserveStock` builds `StockReservedEvent` / `StockReservationFailedEvent` — explicit duplicate of `ReserveByHttp` Phase 5c). Each slice's `AddXxxSlice` calls `AddEventHandler<TEvent, THandler>()`. Replace `Program.cs` per-handler `AddEventHandler` block with fluent chain of `AddXxxSlice()` calls. Delete `IntegrationEvents/EventHandlers/` folder. Confirm `CausationId` / `SagaId` propagation paths through the new slice handlers are byte-for-byte identical to the pre-refactor handlers.

### Acceptance criteria

- [ ] Build green
- [ ] `ReserveStockCommandHandlerTests`, `CommitStockCommandHandlerTests`, `ReleaseStockCommandHandlerTests`, ProductCreated tests all green
- [ ] `IntegrationEvents/EventHandlers/` folder deleted
- [ ] `IntegrationEvents/` folder now empty (payloads moved Phase 2a) — delete it
- [ ] Full `Inventory.Tests` green including `MessagingProviderBootTests` (provider switch still boots)
- [ ] Commit: `refactor(inventory): Phase 6 extract event-consumer slices`

---

## Phase 7: Relocate `InternalOutboxEndpoints` + `Program.cs` becomes slice manifest

**User stories**: 18 (ops plumbing out of feature manifest), 27 (DLQ poller call still works).

### What to build

Move `Endpoints/InternalOutboxEndpoints.cs` to `Infrastructure/Outbox/InternalOutboxEndpoints.cs` with namespace `Inventory.Service.Infrastructure.Outbox`. Delete the now-empty `Endpoints/` folder. Reshape `Program.cs` into a slice manifest: chained `AddXxxSlice()` registration block + `app.MapXxxSlice()` mapping block + `app.RegisterInternalOutboxEndpoints()` (or equivalent) + `MapHealthChecks("/health")`. `RequireService` policy gate preserved on `/internal/outbox/failed`.

### Acceptance criteria

- [ ] Build green
- [ ] `InternalOutboxEndpointsTests` green (DLQ poller route still gated)
- [ ] `Endpoints/` folder deleted
- [ ] `Program.cs` reads as a manifest (slice registrations + mappings + ops endpoints)
- [ ] Full `Inventory.Tests` green
- [ ] Commit: `refactor(inventory): Phase 7 relocate InternalOutboxEndpoints + Program.cs manifest`

---

## Phase 8: Reshape `Inventory.Tests` to mirror slices

**User stories**: 17 (tests mirror Features/<Slice>/, Domain/ kept separate).

### What to build

Move the 10 test classes from `Inventory.Tests/Api/` to `Inventory.Tests/Features/<Slice>/` per PRD Testing Decisions mapping:

- `InventoryApiTests.cs` + `InventoryListApiTests.cs` → `Features/ListStockItems/` + `Features/GetStockItem/` (split if needed)
- `MovementsApiTests.cs` → `Features/GetStockMovements/`
- `RestockApiTests.cs` → `Features/Restock/`
- `ThresholdApiTests.cs` → `Features/SetThreshold/`
- `ReserveApiTests.cs` → `Features/ReserveByHttp/`
- `BackorderApiTests.cs` → `Features/CreateBackorder/`
- `ReserveStockCommandHandlerTests.cs` → `Features/ReserveStock/`
- `CommitStockCommandHandlerTests.cs` → `Features/CommitStock/`
- `ReleaseStockCommandHandlerTests.cs` → `Features/ReleaseStock/`
- `InternalOutboxEndpointsTests.cs` → `Infrastructure/Outbox/` (mirror)
- `HealthChecksTests.cs`, `ObservabilityTests.cs` → top-level (cross-cutting)

Keep `Domain/StockItemTests.cs`, `StockReservationTests.cs`, `StockLevelMonitorTests.cs` in `Inventory.Tests/Domain/`. Keep `IntegrationTestBase.cs` + `InventoryWebApplicationFactory.cs` at project root. Delete the emptied `Api/` folder. Namespace updates only — no behavior change.

### Acceptance criteria

- [ ] Build green
- [ ] Full `dotnet test inventory-microservice/Inventory.Tests` green (zero behavior diff)
- [ ] `Inventory.Tests/Api/` folder deleted
- [ ] `Inventory.Tests/Features/` folder count = slice count
- [ ] Commit: `refactor(inventory): Phase 8 reshape Inventory.Tests into Features/`

---

## Phase 9: Enable NetArchTest + LayoutAnalyzer rules

**User stories**: 12, 13, 14, 26 (boundaries enforced; AI edits cannot drift).

### What to build

Unskip `LayoutTests.cs` + `LayoutAnalyzerTests.cs`. Fill in NetArchTest rules:

- `Inventory.Service.Domain.*` must not depend on `Inventory.Service.Infrastructure.*`, `Inventory.Service.Features.*`, `Inventory.Service.Contracts.*`
- `Inventory.Service.Features.<X>` must not depend on `Inventory.Service.Features.<Y>` for distinct slices
- `Inventory.Service.Infrastructure.*` may reference only `Domain` + `Contracts` (+ allowed shared-lib namespaces)
- `Inventory.Service.Contracts.*` must not reference anything internal beyond `Inventory.Service.Contracts.*`

Promote `Inventory.Service.LayoutAnalyzer` diagnostics from hidden to error severity (`.editorconfig` or analyzer manifest). Fill in analyzer banned-namespace / banned-symbol diagnostics mirroring `Auth.Service.LayoutAnalyzer`.

### Acceptance criteria

- [ ] `dotnet build inventory-microservice` green (analyzer doesn't fire on existing code — proves the refactor satisfies the rules)
- [ ] Full `Inventory.Tests` green including all unskipped Architecture tests
- [ ] `LayoutAnalyzerTests.cs` proves each rule fires on synthetic violation input
- [ ] Commit: `refactor(inventory): Phase 9 enforce layout boundaries`

---

## Phase 10: Docs — root `CLAUDE.md` Inventory exception paragraph

**User stories**: 23 (composes ADR 0011 by reference), 24 (root CLAUDE.md exception paragraph documents divergences).

### What to build

Add one paragraph to root `CLAUDE.md` under the existing pilot-exception block (after the Auth paragraph), matching the Order/Product/Basket/Auth style:

> **Inventory service exception** — fifth Clean Architecture + Vertical Slices pilot, same layout as Order/Product/Basket: `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/`. Boundaries enforced by NetArchTest (`Inventory.Tests/Architecture/LayoutTests.cs`) and the Roslyn `Inventory.Service.LayoutAnalyzer`. Composes ADR [0011](docs/adr/0011-order-cleanarch-vsa-pilot.md) by reference (no new ADR); reuses the [adding-a-new-slice.md](docs/runbooks/adding-a-new-slice.md) runbook unchanged. **Diverges from Order: no `IIntegrationMap<,>` / outbox interceptor seam (Inventory constructs integration events inline per slice — no DbContext-level translation switch to extract); `IInventoryStore` lives in `Domain/Abstractions/` with `EfInventoryStore` in Infrastructure (matches Order); `StockLevelMonitor` returns domain-typed crossings (`LowStockCrossing`, `StockDepletion`) with slices mapping to Contracts events; saga commands (`ReserveStockCommand`/`CommitStockCommand`/`ReleaseStockCommand`) consumed from `ECommerce.Shared.IntegrationEvents.Commands`, not owned in local `Contracts/Integration/`.** Propagation to remaining services is a separate ADR.

No new ADR. No runbook changes.

### Acceptance criteria

- [ ] `CLAUDE.md` contains the new paragraph; existing pilot paragraphs unchanged
- [ ] `dotnet format --verify-no-changes` green
- [ ] Markdown links resolve (ADR 0011 + adding-a-new-slice)
- [ ] Commit: `refactor(inventory): Phase 10 docs root CLAUDE.md Inventory exception`

---

## Verification (end-to-end, after Phase 10)

Run each from a clean `dotnet restore`:

1. **Format + build + test full Inventory stack**
   ```bash
   find inventory-microservice -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
   cd inventory-microservice && dotnet restore && dotnet format --verify-no-changes && dotnet build && dotnet test
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
   Then via Bruno/curl against `http://localhost:8005`:
   - `GET /` — list stock items (auth) → 200 + array
   - `POST /{productId}/restock` (Admin token) → 200 + `RestockResponse`; verify outbox row written
   - `PUT /{productId}/threshold` crossing low-stock threshold → 200; verify `LowStockEvent` published (RabbitMQ Mgmt UI or service-bus explorer)
   - `POST /{productId}/reserve` (Admin) → 200 + reserved lines; verify `StockReservedEvent` published
   - Saga path: produce a `ReserveStockCommand` (via saga service or test publisher) with `SagaId` / `CausationId` → verify `StockReservedEvent` carries identical correlation
   - `GET /internal/outbox/failed` with user token → 403; with service token → 200
   - `GET /health` → 200

4. **Boundary regression check**
   Add a deliberate violation locally (e.g. `Domain/StockItem.cs` adds `using Inventory.Service.Contracts.Integration;`); confirm:
   - `dotnet build` fails with `INVLAY***` analyzer diagnostic
   - `dotnet test Inventory.Tests --filter LayoutTests` fails the matching NetArchTest assertion
   Revert.

5. **DLQ poller still ingests Inventory failures**
   In a stack run, induce a poison-message scenario and confirm the API gateway DLQ poller still persists Inventory rows from `/internal/outbox/failed`.

6. **Metrics parity**
   Hit Prometheus `/metrics` endpoint on Inventory and confirm `reservation-latency-ms` histogram, `stock-movements` / `stock-reservations-failed` / `stock-depleted` counters still emit with identical names / labels.

7. **PR open + bisect spot-check**
   Open single PR `refactor/inventory-vsa` → `main`. `git bisect` any 3 random commits in the branch range and confirm each builds + tests green in isolation.

## Phases needing manual `dotnet test inventory-microservice/Inventory.Tests` before commit

Pre-commit hook only runs Basket tests. Run Inventory tests locally before staging on every phase, but pay especially close attention to behavior-touching phases:

- **Phase 2b** — StockLevelMonitor return-type + caller mapping (silent semantic risk if condition inverts)
- **Phase 4** — `EfInventoryStore` split (largest behavior-translation surface)
- **Phase 5b** — write slices (outbox UoW ordering / event-construction parity)
- **Phase 5c** — `ReserveByHttp` (must match `Features/ReserveStock/` byte-for-byte)
- **Phase 6** — event-consumer slices (`AddEventHandler` wire-up only validated by integration tests)
- **Phase 9** — rule enablement (NetArchTest only fires under `dotnet test`)

If hook fails with `MSB3248`: clean `bin`/`obj` → `dotnet restore --force` → rerun hook (per root `CLAUDE.md` sandbox policy). Do not `--no-verify`, do not defer validation. If still failing, **STOP and hand off to user — do not commit**.
