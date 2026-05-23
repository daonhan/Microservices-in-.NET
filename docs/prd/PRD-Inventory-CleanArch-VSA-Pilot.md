# Inventory Service Clean Architecture + Vertical Slices Pilot PRD

## Problem Statement

`Inventory.Service` is organized by technical type: all HTTP routes inline in one giant `InventoryApiEndpoints.cs` static class, all domain types in `Models/`, all integration-event consumers in `IntegrationEvents/EventHandlers/`, all integration-event payloads in `IntegrationEvents/`, all persistence (and the `IInventoryStore` implementation) inside `InventoryContext`. To understand or change one feature (e.g., "what happens when the saga reserves stock?") a developer hops across `Endpoints/InventoryApiEndpoints.cs` (HTTP `/reserve`), `IntegrationEvents/EventHandlers/ReserveStockCommandHandler.cs` (saga consumer), `Models/StockItem.cs` (aggregate), `Models/StockLevelMonitor.cs` (crossing helper), `Infrastructure/Data/EntityFramework/InventoryContext.cs` (store + persistence), and `IntegrationEvents/StockReservedEvent.cs` (event shape) — six folders to reconstruct one feature.

Concrete smells:

1. `InventoryApiEndpoints.cs` is ~280 lines holding seven unrelated routes with inline lambdas that mix HTTP shape, store calls, outbox UoW orchestration, and integration-event construction.
2. `StockLevelMonitor` (declared as Domain) directly references `IntegrationEvents.LowStockEvent` and `StockDepletedEvent`, so Domain imports a Contracts namespace. This is exactly the boundary violation NetArchTest must catch in every other pilot.
3. `InventoryContext` is both an EF `DbContext` and the `IInventoryStore` implementation — one class wears two hats.
4. Integration-event payloads (`StockReservedEvent`, `StockReservationFailedEvent`, `StockCommittedEvent`, `StockReleasedEvent`, `StockAdjustedEvent`, `StockDepletedEvent`, `LowStockEvent`, consumed `ProductCreatedEvent`) live alongside event-handler classes in the same folder — no separation between cross-service contracts and the code that handles them.
5. Boundaries between domain, application, and infrastructure exist only as conventions. Nothing enforces them, so they erode silently — especially under AI-assisted edits.

The team wants the same Clean Architecture + Vertical Slice layout applied to Order/Product/Basket/Auth, with three pilot-specific decisions confirmed up front:

- **Persistence**: split `InventoryContext` into a pure `DbContext` + a new `EfInventoryStore` that implements `IInventoryStore` (matches Order pilot).
- **Domain cleanliness**: relocate `StockLevelMonitor` to `Domain/` with a domain-typed return contract (e.g., `LowStockCrossing?` / `StockDepletion?` records); slices map crossings to integration events. Restores the Domain → no-Contracts rule.
- **Reserve duplication**: HTTP `POST /{productId}/reserve` and saga `ReserveStockCommand` consumer become two separate slices that each construct `StockReservedEvent` independently. "Duplicate first, extract on third" — no shared helper introduced in this pilot.

## Solution

Pilot the same Clean Architecture + VSA layout used by Order/Product/Basket/Auth on `Inventory.Service` only, with zero behavior change. Inside a single `Inventory.Service.csproj` (no extra projects), reorganize source into:

- `Features/<Slice>/` — one folder per inbound trigger (HTTP route OR integration message), each self-contained: endpoint or event handler, request/response DTOs, slice-local handler class, slice-local DI registration extension, slice-local integration-event construction.
- `Domain/` — `StockItem`, `StockLevel`, `StockMovement`, `StockReservation`, `Warehouse`, `BackorderRequest`, `MovementType`, `ReservationStatus`, `HoldResult`, `CommitItemResult`, `ReleaseItemResult`, `StockLevelMonitor` (with refactored return types), and `Domain/Abstractions/IInventoryStore.cs` + its result-record companions (`RestockResult`, `SetThresholdResult`, `ReserveLine`, `ReserveResult`, `CommitResult`, `ReleaseResult`, `BackorderResult`, etc.). Zero references to Infrastructure, Features, or Contracts.
- `Contracts/Integration/` — cross-service integration-event payload classes (`StockReservedEvent`, `StockReservationFailedEvent`, `StockCommittedEvent`, `StockReleasedEvent`, `StockAdjustedEvent`, `StockDepletedEvent`, `LowStockEvent`, consumed `ProductCreatedEvent`). Saga commands (`ReserveStockCommand` / `CommitStockCommand` / `ReleaseStockCommand`) stay in `ECommerce.Shared.IntegrationEvents.Commands` (consumed, not owned by Inventory).
- `Infrastructure/Data/EntityFramework/` — `InventoryContext` (pure `DbContext`), new `EfInventoryStore` implementing `IInventoryStore`, EF configurations, seed, design-time factory, `EntityFrameworkExtensions` registration.
- `Infrastructure/Outbox/` — `InternalOutboxEndpoints` (DLQ-poller ops surface, `RequireService` policy gate).

Slice handlers are invoked through plain DI (constructor injection of the handler class into the endpoint or event consumer) — no MediatR, no in-house dispatcher. Read slices (`ListStockItems`, `GetStockItem`, `GetStockMovements`) project directly from EF to response DTOs. Write slices (`Restock`, `SetThreshold`, `ReserveByHttp`, `CreateBackorder`, `ReserveStock`, `CommitStock`, `ReleaseStock`, `ProductCreated`) go through `IInventoryStore` and wrap mutations in `IOutboxUnitOfWork.ExecuteAsync` — preserving the current explicit-emit pattern (no `IIntegrationMap<,>` / outbox interceptor seam: Inventory does not have a DbContext-level event-translation switch to extract).

Boundaries are enforced with both NetArchTest assertions (`Inventory.Tests/Architecture/LayoutTests.cs`) and a Roslyn `Inventory.Service.LayoutAnalyzer` (mirrors Order/Product/Basket/Auth analyzers). Tests are reshaped to mirror slices, with aggregate-level unit tests kept separate. Namespaces are renamed to match the new folder layout so the architecture is grep-able and analyzer-targetable. Composes ADR 0011 by reference (no new ADR) and reuses the [adding-a-new-slice.md](../runbooks/adding-a-new-slice.md) runbook unchanged. Root `CLAUDE.md` gets one new "Inventory service exception" paragraph documenting Inventory's specific divergences from Order. The work lands as staged commits on `refactor/inventory-vsa` and merges via a single PR.

## User Stories

1. As an Inventory service developer, I want to open a single folder to see everything the "reserve stock for a saga" feature does, so that I do not have to reconstruct the feature from six scattered folders.
2. As an Inventory service developer, I want each slice to register its own dependencies via an `AddXxxSlice()` extension, so that adding a new feature is a drop-in change and `Program.cs` reads like a manifest of slices.
3. As an Inventory service developer, I want to add a new HTTP route by creating one new `Features/<Name>/` folder, so that I never need to touch unrelated handlers or DTOs in the existing 280-line `InventoryApiEndpoints.cs`.
4. As an Inventory service developer, I want to add a new integration-event consumer by creating one new `Features/<EventName>/` folder, so that event-driven features feel identical to HTTP features.
5. As an Inventory service developer, I want `Domain/StockItem.cs` to keep all aggregate invariants (`EvaluateHold`, `ApplyHold`, `Commit`, `Release`) and slice handlers to be thin orchestration only, so that business rules cannot be silently bypassed.
6. As an Inventory service developer, I want read slices (`ListStockItems`, `GetStockItem`, `GetStockMovements`) to project directly from EF to response DTOs, so that reads do not pay the cost of hydrating the aggregate for views.
7. As an Inventory service developer, I want write slices to load through `IInventoryStore`, call the aggregate's domain methods, and emit integration events via `IOutboxUnitOfWork.ExecuteAsync`, so that the write path always enforces invariants and atomically pairs state + outbox.
8. As an Inventory service developer, I want each slice that emits an integration event to construct that event inline within the slice, so that "what does this slice publish?" is answerable by reading one folder.
9. As an Inventory service maintainer, I want `InventoryContext` to contain only EF persistence (DbContext + DbSets + OnModelCreating + `RecordStockMovement` helper) and a new `EfInventoryStore` to implement `IInventoryStore`, so that the DbContext is a single-purpose persistence module rather than wearing two hats.
10. As an Inventory service maintainer, I want `StockLevelMonitor` relocated under `Domain/` and refactored to return domain-typed crossings (`LowStockCrossing?` and `StockDepletion?` records), so that the Domain layer no longer imports `IntegrationEvents` and the NetArchTest "Domain references nothing from Contracts" rule can be applied uniformly.
11. As an Inventory service maintainer, I want slices that need to publish `LowStockEvent` or `StockDepletedEvent` to call `StockLevelMonitor.TryLowStockCrossing` / `TryDepletedCrossing` and translate the returned crossing into the Contracts event themselves, so that integration-event construction lives in slices rather than in Domain.
12. As an Inventory service maintainer, I want NetArchTest rules that fail the test suite if `Domain` references Infrastructure, Contracts, or Features; if any slice references another slice; or if Infrastructure leaks past Domain + Contracts, so that boundary violations are caught in CI rather than in code review.
13. As an Inventory service maintainer, I want a Roslyn `Inventory.Service.LayoutAnalyzer` as a second guardrail beside NetArchTest, so that violations surface as compiler errors during development — not only when tests run.
14. As an Inventory service contributor, I want the cross-slice sharing rule documented as "duplicate first, extract on third" with a NetArchTest rule forbidding slice-to-slice references, so that I do not accidentally create a hidden coupling between two slices.
15. As an Inventory service contributor, I want `Features/ReserveByHttp/` (HTTP) and `Features/ReserveStock/` (saga `ReserveStockCommand` consumer) to each construct `StockReservedEvent` independently in this pilot, so that the duplication is visible and intentional rather than pre-extracted into a hidden helper.
16. As an Inventory service contributor, I want namespaces to match the new folder layout (`Inventory.Service.Domain`, `Inventory.Service.Features.<Slice>`, `Inventory.Service.Contracts.Integration`, `Inventory.Service.Infrastructure.Data.EntityFramework`, `Inventory.Service.Infrastructure.Outbox`), so that I can grep for layer membership and analyzer rules can target namespaces.
17. As an Inventory service contributor, I want `Inventory.Tests` reshaped to mirror `Features/<Slice>/` while keeping `Domain/` aggregate tests (`StockItemTests`, `StockReservationTests`, `StockLevelMonitorTests`) separate, so that feature tests and domain unit tests are each easy to locate.
18. As an Inventory service contributor, I want `InternalOutboxEndpoints` to move from `Endpoints/` to `Infrastructure/Outbox/`, so that operational plumbing does not pollute the feature manifest.
19. As a reviewer, I want the pilot to land as staged commits on `refactor/inventory-vsa` and a single PR, with each commit building and tests passing, so that the refactor is bisectable and reviewable end-to-end.
20. As a reviewer, I want zero functional behavior change from the pilot — every existing `Inventory.Tests` test passes (modulo namespace renames) — so that the layout migration cannot regress functional behavior.
21. As a release engineer, I want the pilot to leave `ECommerce.Shared` untouched (no nupkg version bump), so that other services are not forced to consume a new shared-package version.
22. As a release engineer, I want the pre-commit hook (`dotnet format`, `dotnet build`, Basket tests) to gate every commit on the refactor branch, so that the branch cannot accumulate partial-validation commits.
23. As an architect, I want the pilot to compose ADR 0011 by reference (no new ADR) and reuse the `adding-a-new-slice.md` runbook unchanged, matching how Product, Basket, and Auth landed, so that the pattern is consistently documented across all five pilots.
24. As an architect, I want the root `CLAUDE.md` to gain one "Inventory service exception" paragraph documenting Inventory's specific divergences (no event-translation interceptor seam; `IInventoryStore` split from `DbContext`; `StockLevelMonitor` returns domain-typed crossings; saga commands are consumed from shared lib, not owned in `Contracts/Integration/`), so that future contributors know what to expect when comparing Inventory to Order.
25. As an architect, I want the propagation order for remaining services (shipping, payment, saga) to remain a separate ADR concern after this pilot lands, so that decisions about the next propagation step are informed by what we learned from Inventory.
26. As an AI-assisted contributor, I want the layout, namespaces, and architecture rules to be self-describing and analyzer-enforced, so that AI edits cannot silently drift across boundaries when extending Inventory.
27. As an operator, I want the DLQ poller's call to `/internal/outbox/failed` (gated by `RequireService`) to continue working after the refactor, so that DLQ ingestion is not interrupted.
28. As an operator, I want trace IDs, correlation IDs, `CausationId`, and `SagaId` to propagate identically through saga `ReserveStockCommand` → outbox `StockReservedEvent` (and the matching commit/release flows) after the refactor, so that observability dashboards and saga state machines do not break.
29. As an operator, I want the existing reservation-latency histogram (`reservation-latency-ms`) and counters (`stock-movements`, `stock-reservations-failed`, `stock-depleted`) to continue to be emitted from their current call sites (now relocated into slices), so that Prometheus dashboards keep the same metric series after the refactor.

## Implementation Decisions

### Pilot scope

- Pilot is `Inventory.Service` only. No other service changes. Propagation to shipping / payment / saga handled by a follow-up ADR (separate from this PRD).

### Project shape

- Single `Inventory.Service.csproj` retained. No split into `Inventory.Domain` / `Inventory.Application` / `Inventory.Infrastructure` projects.
- Boundaries enforced by namespace conventions + Roslyn analyzer rules + NetArchTest, not by csproj references.

### Folder topology

- `Features/<Slice>/` — one folder per inbound trigger. Slice = one HTTP route OR one integration message handler. Each slice owns its handler class, request/response DTOs (if any), slice DI extension, and any integration-event construction it performs.
- `Domain/` — aggregates, value objects, helpers (`StockLevelMonitor` after refactor), `IDomainEvent`-style abstractions if any are introduced (not required for this pilot), and `Domain/Abstractions/IInventoryStore.cs`. No EF, no HTTP, no Contracts references.
- `Contracts/Integration/` — cross-service event payload classes Inventory publishes or consumes (`StockReservedEvent`, `StockReservationFailedEvent`, `StockCommittedEvent`, `StockReleasedEvent`, `StockAdjustedEvent`, `StockDepletedEvent`, `LowStockEvent`, consumed `ProductCreatedEvent`). Saga commands (`ReserveStockCommand`, `CommitStockCommand`, `ReleaseStockCommand`) stay in `ECommerce.Shared.IntegrationEvents.Commands` and are simply imported by the consuming slices.
- `Infrastructure/Data/EntityFramework/` — `InventoryContext` (pure DbContext), `EfInventoryStore` (impl of `IInventoryStore`), EF configurations, `InventoryContextSeed`, `InventoryContextDesignTimeFactory`, `EntityFrameworkExtensions`.
- `Infrastructure/Outbox/` — `InternalOutboxEndpoints` (ops surface, `RequireService` policy gate).
- `Migrations/` — unchanged; `generated_code = true`. No new migrations in this pilot.

### Slice inventory

HTTP slices:

- `Features/ListStockItems/` — `GET /`
- `Features/GetStockItem/` — `GET /{productId:int}`
- `Features/GetStockMovements/` — `GET /{productId:int}/movements`
- `Features/Restock/` — `POST /{productId:int}/restock` (requires `Administrator`)
- `Features/SetThreshold/` — `PUT /{productId:int}/threshold` (requires `Administrator`)
- `Features/ReserveByHttp/` — `POST /{productId:int}/reserve` (requires `Administrator`)
- `Features/CreateBackorder/` — `POST /{productId:int}/backorder`
- Health endpoint `GET /health` stays in `Program.cs` (or a tiny `Features/Health/` if it lines up cleanly — exact placement is a Phase-7 implementation detail, no behavior change).

Event-consumer slices:

- `Features/ProductCreated/` — consumes `ProductCreatedEvent`, provisions stock item.
- `Features/ReserveStock/` — consumes `ReserveStockCommand`, emits `StockReservedEvent` / `StockReservationFailedEvent`.
- `Features/CommitStock/` — consumes `CommitStockCommand`, emits `StockCommittedEvent`.
- `Features/ReleaseStock/` — consumes `ReleaseStockCommand`, emits `StockReleasedEvent`.

### Dispatch model

- No MediatR, no in-house mediator.
- Endpoints and event consumers take their slice handler via constructor injection and call `HandleAsync(...)` directly.
- Slice handler classes are `internal sealed` with one public async method.

### Domain richness rule

- Rich domain preserved: `StockItem` aggregate keeps invariants (`EvaluateHold`, `ApplyHold`, `Commit`, `Release`). `StockReservation` keeps its state-machine methods. `StockLevelMonitor` becomes a Domain decision helper returning domain-typed crossings.
- Slice handlers are orchestration only: load aggregate / call domain method / persist via `IInventoryStore` / wrap in `IOutboxUnitOfWork.ExecuteAsync` / construct integration events from results + crossings.
- Read slices bypass the aggregate and project directly from `InventoryContext` to response DTOs.

### Persistence

- `IInventoryStore` (+ result records: `RestockResult`, `SetThresholdResult`, `ReserveLine`, `ReservedLine`, `FailedReserveLine`, `ReserveResult`, `CommittedLine`, `CommitResult`, `ReleasedLine`, `ReleaseResult`, `BackorderResult`, `FulfilledBackorder`) lives under `Domain/Abstractions/`.
- New `EfInventoryStore` class lives in `Infrastructure/Data/EntityFramework/`. It takes `InventoryContext` + `MetricFactory` via DI and implements every `IInventoryStore` method by delegating to context DbSets.
- `InventoryContext` is reduced to pure persistence: `DbContext` base, `DbSet<>` properties, `OnModelCreating`, and the `RecordStockMovement` private helper. All store methods (`GetStockItem`, `Restock`, `Reserve`, `CommitReservations`, `ReleaseReservations`, `SetThreshold`, `CreateBackorder`, etc.) move out into `EfInventoryStore`.
- `EntityFrameworkExtensions.AddSqlServerDatastore` (shared lib hook) keeps wiring the DbContext; new lines register `EfInventoryStore` against `IInventoryStore`. Existing shared lib remains untouched — wiring lives in Inventory's local `AddInventoryDatastore` extension (renamed from current shape if needed) or the slice extensions.

### Domain → Contracts boundary fix

- `StockLevelMonitor.TryLowStockCrossing` returns `LowStockCrossing?` (new Domain record: `ProductId, WarehouseId, AvailableAfter, ThresholdAfter`) instead of `LowStockEvent`.
- `StockLevelMonitor.TryDepletedCrossing` returns `StockDepletion?` (new Domain record: `ProductId, WarehouseId`) instead of `StockDepletedEvent`.
- Slices that emit those events (`Restock`, `SetThreshold`, and any slice that mutates stock and may cross a threshold) call the monitor, then map the returned crossing into the corresponding Contracts event, then add it to the outbox event list.
- Result: `Domain` has zero references to `Contracts.Integration.*`; NetArchTest can enforce the standard rule unmodified.

### No outbox-interceptor seam

- Inventory does NOT introduce `IIntegrationMap<TDomainEvent, TIntegrationEvent>` or a `DomainEventOutboxInterceptor`.
- Reason: Inventory has no `InventoryContext.Translate(...)` switch to extract. Integration events are already constructed explicitly inside each slice and added to the outbox via `IOutboxUnitOfWork.ExecuteAsync`. The Order-pilot seam exists to fix a smell that Inventory does not have.
- This divergence from Order is documented in the new "Inventory service exception" paragraph in root `CLAUDE.md`.

### Slice DI

- Each slice exposes a static class with `Add<SliceName>Slice(this IServiceCollection)`. The extension registers the slice handler (`AddScoped`) and, for event-consumer slices, calls `AddEventHandler<TEvent, THandler>()` from `ECommerce.Shared`.
- `Program.cs` chains slice extensions in a fluent manifest. The current per-handler `AddEventHandler` block is removed and becomes per-slice. Endpoint registration moves from `app.RegisterEndpoints()` (single static class) to per-slice `app.MapXxxSlice()` extensions or a chained registration call.

### Namespaces

- Renamed to match folders: `Inventory.Service.Domain`, `Inventory.Service.Domain.Abstractions`, `Inventory.Service.Features.<Slice>`, `Inventory.Service.Contracts.Integration`, `Inventory.Service.Infrastructure.Data.EntityFramework`, `Inventory.Service.Infrastructure.Outbox`.
- Existing namespace `Inventory.Service.Models` is fully retired.

### Cross-slice sharing rule

- Rule of three: duplicate freely between slices; extract to `Domain/` (behavioral) or `Features/Shared/` (helper) only on the third use.
- NetArchTest forbids `Inventory.Service.Features.<X>` referencing `Inventory.Service.Features.<Y>` for any `X != Y`.
- HTTP `ReserveByHttp` and saga `ReserveStock` are explicitly *expected* duplicates in this pilot. Both construct `StockReservedEvent` independently. No extraction.

### Boundary enforcement

NetArchTest rules in `Inventory.Tests/Architecture/LayoutTests.cs`:

- `Domain` types must not reference `Inventory.Service.Infrastructure.*`, `Inventory.Service.Features.*`, or `Inventory.Service.Contracts.*`.
- `Features.<X>` types must not reference `Features.<Y>` for distinct slices.
- `Infrastructure` types may reference only `Domain` + `Contracts` (+ allowed shared-lib namespaces).
- `Contracts` types reference nothing internal beyond `Inventory.Service.Contracts.*`.

Roslyn `Inventory.Service.LayoutAnalyzer` provides the second guardrail (compile-time errors), mirroring the Auth/Basket/Product/Order analyzers. Same rules expressed as banned-namespace / banned-symbol diagnostics.

### Internal ops endpoints

- `InternalOutboxEndpoints` moves from `Endpoints/` to `Infrastructure/Outbox/InternalOutboxEndpoints.cs`.
- Wiring done from `Program.cs` after slice registration (`app.RegisterInternalOutboxEndpoints()` kept as-is or renamed to match prior pilots; behavior unchanged).
- `RequireService` policy gate preserved.

### Shared library

- `ECommerce.Shared` is not modified. No nupkg version bump. No consumer impact.
- The pilot composes existing shared hooks: `AddSqlServerDatastore`, `AddOutbox`, `AddPlatformEventBus`, `AddPlatformEventPublisher`, `AddPlatformSubscriberService`, `AddEventHandler`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddJwtAuthentication`, `AddRequireServicePolicy`, `AddPlatformOpenApi`, `AuthorizationPolicies.RequireServicePolicy`.

### Validation

- Out of scope. Existing inline validation (`Quantity > 0`, `OrderId != Guid.Empty`, `Threshold >= 0`, etc.) is preserved verbatim in slice handlers. "Add per-slice FluentValidation" is listed as future work in the per-slice runbook follow-up.

### Rollout

- Branch `refactor/inventory-vsa` (already checked out). Staged commits land in this order, each green:
  1. Scaffold NetArchTest project reference + skipped layout tests + `Inventory.Service.LayoutAnalyzer` skeleton (analyzer present, rules off).
  2. Move domain types from `Models/` to `Domain/`; rename namespaces. `StockLevelMonitor` relocated to `Domain/` with refactored domain-typed return contract (Phase 2 sub-step). Callers updated to consume crossings + construct events at call site.
  3. Move integration event payload classes from `IntegrationEvents/` to `Contracts/Integration/`; rename namespaces. Event-handler classes stay where they are temporarily (Phase 5 moves them).
  4. Split `IInventoryStore` impl out of `InventoryContext` into `EfInventoryStore` under `Infrastructure/Data/EntityFramework/`. `InventoryContext` reduced to DbContext + DbSets + OnModelCreating + `RecordStockMovement`. Move `IInventoryStore.cs` + its result records to `Domain/Abstractions/`.
  5. Extract HTTP slices one at a time, each green: `ListStockItems`, `GetStockItem`, `GetStockMovements`, `Restock`, `SetThreshold`, `ReserveByHttp`, `CreateBackorder`. `Endpoints/InventoryApiEndpoints.cs` shrinks then disappears.
  6. Extract event-consumer slices: `ProductCreated`, `ReserveStock`, `CommitStock`, `ReleaseStock`. `IntegrationEvents/EventHandlers/` shrinks then disappears.
  7. Move `InternalOutboxEndpoints` to `Infrastructure/Outbox/`. `Program.cs` becomes a slice manifest.
  8. Reshape `Inventory.Tests` to mirror `Features/<Slice>/`. `Domain/` aggregate tests stay separate.
  9. Unskip + enable NetArchTest rules; enable Roslyn analyzer rules (`Inventory.Service.LayoutAnalyzer`).
  10. Docs: add "Inventory service exception" paragraph to root `CLAUDE.md` (compose ADR 0011 by reference, reuse adding-a-new-slice runbook unchanged, list Inventory-specific divergences).
- Single PR for review. Pre-commit hook gates every commit (no `--no-verify`, no validation deferral). Mandatory sandbox-policy order from root `CLAUDE.md` followed if `MSB3248` appears.

## Testing Decisions

### Test philosophy

- A good test verifies external behavior of a module through its public interface, not internal implementation details.
- Refactor must produce zero functional behavior change. Every existing `Inventory.Tests` test must continue to pass without modification beyond namespace updates required by the rename and any necessary `using` shuffles after files move.
- New tests are added only for new seams (the `EfInventoryStore` split, the `StockLevelMonitor` return-type refactor) and for the architecture rules themselves.

### Modules to test

- **`StockItem` aggregate (unchanged tests)** — existing `Inventory.Tests/Domain/StockItemTests.cs` covers `EvaluateHold`, `ApplyHold`, `Commit`, `Release` invariants. Kept verbatim, only namespace touched.
- **`StockReservation` (unchanged tests)** — `StockReservationTests.cs` keeps its state-machine coverage. Namespace touched only.
- **`StockLevelMonitor` (rewritten tests)** — `StockLevelMonitorTests.cs` updates to assert the new domain-typed return (`LowStockCrossing` / `StockDepletion` records). The tests still cover all four scenarios currently covered (was/is low stock combinations, depleted crossing edges) but assert against domain records instead of `LowStockEvent` / `StockDepletedEvent`. New tests added at the slice level to assert that slices correctly map a returned crossing into the matching Contracts event.
- **Per-slice handler tests** — existing `Inventory.Tests/Api/*` tests migrate into `Inventory.Tests/Features/<Slice>/` without behavioral changes. They continue to use `InventoryWebApplicationFactory` and `IntegrationTestBase`. Mappings:
  - `InventoryApiTests.cs` + `InventoryListApiTests.cs` → `Features/ListStockItems/` + `Features/GetStockItem/`
  - `MovementsApiTests.cs` → `Features/GetStockMovements/`
  - `RestockApiTests.cs` → `Features/Restock/`
  - `ThresholdApiTests.cs` → `Features/SetThreshold/`
  - `ReserveApiTests.cs` → `Features/ReserveByHttp/`
  - `BackorderApiTests.cs` → `Features/CreateBackorder/`
  - `ReserveStockCommandHandlerTests.cs` → `Features/ReserveStock/`
  - `CommitStockCommandHandlerTests.cs` → `Features/CommitStock/`
  - `ReleaseStockCommandHandlerTests.cs` → `Features/ReleaseStock/`
  - `InternalOutboxEndpointsTests.cs` → `Infrastructure/Outbox/` mirror folder under tests
  - `HealthChecksTests.cs`, `ObservabilityTests.cs` → top-level `Inventory.Tests` (cross-cutting, no slice)
- **`EfInventoryStore`** — covered indirectly by the existing integration tests via `WebApplicationFactory<Program>`. No new unit tests added in this pilot unless the split exposes a method that becomes testable in isolation (judgment call during Phase 4). Behavior-preservation is verified by the existing test suite.
- **`Inventory.Tests/Architecture/LayoutTests.cs`** — new NetArchTest rules tests that act as the executable specification of the boundary policy. Fail if any future contributor introduces a cross-boundary reference. Matches the Auth/Basket/Product/Order layout test file structure.
- **`Inventory.Tests/Architecture/LayoutAnalyzerTests.cs`** — new analyzer tests for the Roslyn `Inventory.Service.LayoutAnalyzer`, mirroring `Auth.Tests/Architecture/LayoutAnalyzerTests.cs`.
- **`MessagingProviderBootTests.cs`** — unchanged; verifies messaging provider switch boots both RabbitMQ + Azure Service Bus. Namespace touched only.

### Prior art in the codebase

- `Inventory.Tests/IntegrationTestBase.cs` + `Inventory.Tests/InventoryWebApplicationFactory.cs` — existing factory + base used by every current integration test. Preserved at the root of the tests project.
- `Inventory.Tests/Domain/StockItemTests.cs`, `StockReservationTests.cs`, `StockLevelMonitorTests.cs` — existing aggregate / helper unit tests; `Given_When_Then` underscored display names preserved (`CA1707` suppressed via `Directory.Build.props`).
- `auth-microservice/Auth.Tests/Architecture/LayoutTests.cs` + `LayoutAnalyzerTests.cs` — closest prior art for the architecture-test pair; copy the pattern verbatim and adapt namespaces.
- `order-microservice/Order.Tests/Features/` — closest prior art for per-slice test layout under `Tests/Features/<Slice>/`.
- Pre-commit hook (`dotnet husky run --group pre-commit`) enforces `dotnet format --verify-no-changes` and `dotnet build --no-restore` + Basket tests on every commit. Inventory tests are run manually per the root `CLAUDE.md` sandbox policy before pushing.

## Out of Scope

- Refactoring any other service (basket, product, auth, shipping, payment, saga, order, api-gateway). Propagation to remaining services is a follow-up ADR concern.
- Modifying `ECommerce.Shared`. The pilot composes existing shared extensions and `AuthorizationPolicies.RequireServicePolicy` only.
- Adding request validation (FluentValidation or DataAnnotations). Inline `BadRequest` validation in slice handlers preserved verbatim.
- Introducing MediatR or any mediator-style dispatcher.
- Splitting `Inventory.Service.csproj` into multiple projects.
- Introducing an `IIntegrationMap<,>` / `DomainEventOutboxInterceptor` seam (explicit Inventory divergence from Order — no DbContext-level translation switch exists to extract).
- Changing the `StockItem` / `StockLevel` / `StockMovement` / `StockReservation` / `Warehouse` / `BackorderRequest` database schema. No new EF migrations.
- Changing integration event payload contracts (`StockReservedEvent`, `StockCommittedEvent`, `StockReleasedEvent`, `StockReservationFailedEvent`, `StockAdjustedEvent`, `StockDepletedEvent`, `LowStockEvent`, `ProductCreatedEvent`). Only their location (folder + namespace) moves.
- Changing the outbox table, dispatcher, retry policy, or DLQ behavior in `ECommerce.Shared.Infrastructure.Outbox`.
- Changing public HTTP routes, response shapes, status codes, or `RequireAuthorization` / `RequireAuthorization("Administrator")` requirements of `InventoryApiEndpoints`.
- Changing the QA seeding hook (`SeedQaData`, `QaSeedingExtensions.IsQaSeedingEnabled`) or `InventoryContextSeed` behavior.
- Changing CI/CD pipelines, Docker Compose, Kubernetes manifests, or Azure pipelines.
- Performance optimization. The CQRS-lite read-path decision (read slices project directly from EF) is structural, not performance-driven.
- Extracting a shared `ReservedEventBuilder` helper between `ReserveByHttp` and `ReserveStock` slices. Duplication is intentional under "rule of three".

## Further Notes

- Inventory is the right fifth pilot because it stresses two dimensions Order/Product/Basket/Auth did not exercise together: (a) saga *participant* (inbound commands + correlated event responses with `CausationId` / `SagaId` propagation) and (b) a pre-existing Domain → Contracts boundary violation (`StockLevelMonitor`) that the pilot must resolve as part of moving to the layout. If the layout works here, it will generalize to shipping and payment (also saga participants).
- The `StockLevelMonitor` relocation + return-type refactor is the deepest module change in the pilot; everything else is relocation + namespace renames + the `InventoryContext` / `EfInventoryStore` split.
- The `InventoryContext` → `InventoryContext` + `EfInventoryStore` split is the same shape used in Order. It is mechanical but touches every store method. Phase 4 is the largest single phase; suggest a dedicated commit and a tight diff review.
- NetArchTest + Roslyn analyzer redundancy is intentional and matches prior pilots. NetArchTest fires only during `dotnet test`; banned-symbol analyzers fire during build for fast in-editor feedback. The "belt + suspenders" choice is justified by the AI-assisted contribution model.
- The "duplicate first, extract on third" rule is load-bearing. `ReserveByHttp` and `ReserveStock` are explicit duplicates in this pilot. The NetArchTest slice-to-slice rule mechanically enforces no premature shared helper.
- Composes ADR 0011 by reference (no new ADR), matching how Product, Basket, and Auth landed. Root `CLAUDE.md` gets one new "Inventory service exception" paragraph listing Inventory's specific divergences from Order: no event-translation interceptor seam; `IInventoryStore` split from `DbContext`; `StockLevelMonitor` returns domain-typed crossings; saga commands consumed from shared lib, not owned in `Contracts/Integration/`.
- Behavioral guidance from root `CLAUDE.md` and `.claude/CLAUDE.md` applies: surgical changes only, no improving adjacent code, match existing style, push back on over-engineering. The pilot is large in line count but mechanical in intent.
- After this pilot lands, a separate ADR may propose propagation to remaining services. Candidate next pilots if approved: shipping (saga participant, similar shape) → payment (saga participant, has its own provider/HTTP integrations) → saga (orchestrator, different shape, last to validate the layout generalizes to non-CRUD coordination services).
