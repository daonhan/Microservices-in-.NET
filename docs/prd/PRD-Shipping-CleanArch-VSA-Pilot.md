# Shipping Service Clean Architecture + Vertical Slices Pilot PRD

> Tracking issue: [#209](https://github.com/daonhan/Microservices-in-.NET/issues/209). Modeled on epic [#152](https://github.com/daonhan/Microservices-in-.NET/issues/152) (Order pilot).

## Problem Statement

The `Shipping.Service` codebase is organized by technical type, like every pre-pilot service in this repo: all HTTP routes in one `Endpoints/ShippingApiEndpoints.cs` file (~400 lines, 11 routes), all DTOs in `ApiModels/`, all domain models in `Models/`, all integration-event consumers in `IntegrationEvents/EventHandlers/`, all carrier infrastructure in a top-level `Carriers/` folder, all persistence in `Infrastructure/Data/`. To understand or change one feature ("what happens when a shipment is dispatched?") a developer must hop across `Endpoints/`, `Models/Shipment.cs`, `Carriers/RateShoppingService.cs`, `Infrastructure/Data/EntityFramework/ShippingContext.cs`, and `IntegrationEvents/ShipmentDispatchedEvent.cs`, then reconstruct the feature mentally. The single `ShippingApiEndpoints.cs` file co-locates eight write routes and three read routes with shared private helpers, blurring the seam between the read path and the write path.

Boundaries between domain, application, and infrastructure exist only as conventions. Nothing prevents `Shipment.cs` from referencing EF Core or carrier gateways; nothing prevents a future contributor (human or AI) from adding a new endpoint inside `ShippingApiEndpoints.cs` and silently bypassing the carrier polling / outbox plumbing.

The team wants:

1. A codebase grouped by *what the application does* (one inbound trigger per folder), not by technical type.
2. Enforceable Clean Architecture boundaries: Domain has no infrastructure dependencies; Features depend on Domain + Contracts; Infrastructure implements abstractions declared in Domain.
3. A pattern consistent with the prior five pilots (Order, Product, Basket, Auth, Inventory) so the project's mental model stays uniform.

## Solution

Pilot Clean Architecture + Vertical Slice Architecture (VSA) on `Shipping.Service` only, with zero behavior change. Inside a single `Shipping.Service.csproj`, reorganize source into:

- `Features/<Slice>/` — one folder per inbound trigger (HTTP route or integration message). Each slice owns its endpoint or consumer, request/response DTOs, slice DI extension, and slice handler.
- `Domain/` — `Shipment` aggregate, `ShipmentLine`, `ShipmentStatus`, `ShipmentStatusHistoryEntry`, `ShipmentStatusSource`, `Money`, `OrderConfirmation`, `ShippingAddress`, `Warehouse`, and `Abstractions/IShipmentStore` + `Abstractions/ICarrierGateway` + `Abstractions/IRateShopper`. No EF, HTTP, or carrier-impl references.
- `Contracts/Integration/` — cross-service event and command payloads (`ShipmentCreatedEvent`, `ShipmentDispatchedEvent`, `ShipmentDeliveredEvent`, `ShipmentCancelledEvent`, `ShipmentFailedEvent`, `ShipmentReturnedEvent`, `ShipmentStatusChangedEvent`, inbound `OrderConfirmedEvent`).
- `Infrastructure/Data/EntityFramework/` — `ShippingContext`, `EfShipmentStore` (impl of `IShipmentStore`), EF configurations, `ShippingContextDesignTimeFactory`, `ShippingContextSeed`, `ShippingQaFixtures`.
- `Infrastructure/Carriers/` — `FakeExpressCarrierGateway`, `FakeGroundCarrierGateway`, `FakeCarrierDispatchRegistry`, `FakeCarrierWebhookParser`, `CarrierStatusApplier`, `CarrierPollingService`, `RateShoppingService`, `CarrierWebhookOptions`.
- `Infrastructure/Observability/` — `ShippingMetrics`.
- `Infrastructure/Outbox/` — `InternalOutboxEndpoints` (ops surface gated by `RequireService`).

Slice handlers are invoked through plain DI (constructor injection into the endpoint or event consumer). No MediatR, no in-house dispatcher. Read slices project directly from the EF context to response DTOs (CQRS-lite); write slices go through `IShipmentStore` and call methods on the `Shipment` aggregate. Integration events continue to be constructed inline by each write slice and persisted via `outboxUnitOfWork.ExecuteAsync(...)` + `AddOutboxEvent(...)` — **no `IIntegrationMap<,>` / `DomainEventOutboxInterceptor` seam is introduced**, because `ShippingContext` has no central translate switch to extract and the aggregate has no domain-event collection today. This matches the Inventory pilot's divergence from Order; documented in CLAUDE.md.

Boundaries enforced with both NetArchTest assertions (in `Shipping.Tests/Architecture/LayoutTests.cs`) and a Roslyn `Shipping.Service.LayoutAnalyzer`. Tests are reshaped to mirror slices, with aggregate-level unit tests kept separate under `Shipping.Tests/Domain/`. Namespaces are renamed to match the new folder layout so the architecture is grep-able and analyzer-targetable. The work lands as staged commits on a single branch and merges via one PR. The CLAUDE.md "Shipping service exception" entry composes ADR [0011](../adr/0011-order-cleanarch-vsa-pilot.md) by reference (no new ADR) and reuses the existing [adding-a-new-slice.md](../runbooks/adding-a-new-slice.md) runbook unchanged.

## User Stories

1. As a Shipping service developer, I want to open a single folder to see everything the "dispatch shipment" feature does, so that I do not have to reconstruct the feature from `ShippingApiEndpoints.cs`, `Shipment.cs`, `RateShoppingService.cs`, `ShippingContext.cs`, and `ShipmentDispatchedEvent.cs` separately.
2. As a Shipping service developer, I want each slice to register its own dependencies via an `AddXxxSlice()` extension, so that adding a new feature is a drop-in change and `Program.cs` reads like a manifest.
3. As a Shipping service developer, I want to add a new HTTP endpoint by creating one new `Features/<Name>/` folder, so that I never need to touch unrelated handlers or DTOs in the 400-line `ShippingApiEndpoints.cs`.
4. As a Shipping service developer, I want to add a new integration-event consumer (or saga command consumer) by creating one new `Features/<EventName>/` folder, so that event-driven features feel identical to HTTP features.
5. As a Shipping service developer, I want `Domain/Shipment.cs` to contain all business invariants (status state machine, terminal transitions, carrier assignment, status history append) and `Features/<Slice>/Handler.cs` to be thin orchestration only, so that business rules cannot be silently bypassed by a slice handler taking shortcuts.
6. As a Shipping service developer, I want read slices (`GetShipmentById`, `GetShipmentsByOrder`, `ListShipments`) to project directly from `ShippingContext` to `ShipmentResponse`, so that reads do not pay the cost of hydrating the `Shipment` aggregate and including `ShipmentLine` + `ShipmentStatusHistoryEntry` collections they don't strictly need.
7. As a Shipping service developer, I want write slices (`PickShipment`, `PackShipment`, `DispatchShipment`, `DeliverShipment`, `FailShipment`, `ReturnShipment`, `CancelShipment`) to load the aggregate through `IShipmentStore`, mutate it via domain methods, persist, and emit integration events through the outbox, so that the write path always enforces invariants.
8. As a Shipping service developer, I want each slice that emits an integration event to construct that event **inline within the slice handler** and persist via `outboxUnitOfWork.ExecuteAsync(...)` + `AddOutboxEvent(...)`, so that "what does this slice publish?" is answerable by reading the one handler file. (Diverges from Order; matches Inventory.)
9. As a Shipping service maintainer, I want `ShippingContext` to remain a single-purpose persistence module, so that no central translate switch grows over time.
10. As a Shipping service maintainer, I want `Carriers/` infrastructure (fake gateways, webhook parser, polling hosted service, status applier, dispatch registry, rate shopping service) under `Infrastructure/Carriers/`, so that the Domain layer stays free of carrier-impl references and the top-level folder count stays consistent with prior pilots.
11. As a Shipping service maintainer, I want `ICarrierGateway` and `IRateShopper` abstractions declared under `Domain/Abstractions/`, with implementations under `Infrastructure/Carriers/`, so that slices and tests can depend on the abstraction without pulling carrier impls into Domain.
12. As a Shipping service maintainer, I want the single `/webhooks/carrier/{carrierKey}` route to live in a `Features/ProcessCarrierWebhook/` slice that dispatches per-carrier internally via `FakeCarrierWebhookParser`, so that the webhook surface is one feature folder regardless of how many carriers it serves.
13. As a Shipping service maintainer, I want `CarrierPollingService` (the hosted background poller) to remain in `Infrastructure/Carriers/`, **not** a `Features/` slice, so that the slice manifest only contains externally-triggered features and the polling tick stays an internal pump.
14. As a Shipping service maintainer, I want `ShippingMetrics` moved from top-level `Observability/` to `Infrastructure/Observability/`, so that the top-level folder layout matches prior pilots exactly.
15. As a Shipping service maintainer, I want NetArchTest rules that fail the test suite if `Domain` references infrastructure, if any slice references another slice, or if infrastructure leaks past Domain + Contracts, so that boundary violations are caught in CI rather than in code review.
16. As a Shipping service maintainer, I want a Roslyn `Shipping.Service.LayoutAnalyzer` as a second guardrail beside NetArchTest, so that violations surface as compiler errors during development — not only when tests run.
17. As a Shipping service contributor, I want the cross-slice sharing rule documented as "duplicate first, extract on third" with a NetArchTest rule forbidding slice-to-slice references, so that I do not accidentally create hidden coupling between two slices.
18. As a Shipping service contributor, I want namespaces to match the new folder layout (`Shipping.Service.Domain`, `Shipping.Service.Features.DispatchShipment`, `Shipping.Service.Contracts.Integration`, `Shipping.Service.Infrastructure.Data.EntityFramework`, `Shipping.Service.Infrastructure.Carriers`, `Shipping.Service.Infrastructure.Observability`, `Shipping.Service.Infrastructure.Outbox`), so that I can grep for layer membership and analyzer rules can target namespaces.
19. As a Shipping service contributor, I want `Shipping.Tests` reshaped to mirror `Features/<Slice>/` while keeping `Shipping.Tests/Domain/` aggregate tests separate, so that feature tests and domain unit tests are each easy to locate.
20. As a Shipping service contributor, I want the HTTP `CancelShipment` slice (POST `/{id}/cancel`) and the saga `CancelShipmentCommand` consumer to be two distinct slices (`Features/CancelShipment/` + `Features/CancelShipmentCommand/`), mirroring Inventory's `ReserveByHttp` vs `ReserveStock` convention, so that "one inbound trigger = one slice" stays true.
21. As a Shipping service contributor, I want `InternalOutboxEndpoints` (the DLQ-poller ops surface) under `Infrastructure/Outbox/`, not under `Features/`, so that operational plumbing does not pollute the feature manifest.
22. As a reviewer, I want the pilot to land as staged commits on one branch and a single PR, with each commit building and tests passing, so that the refactor is bisectable and reviewable end-to-end.
23. As a reviewer, I want zero behavior change from the pilot — every existing `Shipping.Tests` test passes unchanged (modulo namespace updates), so that the layout migration cannot regress functional behavior. In particular, the carrier webhook signature verification, the polling tick that applies carrier status, the rate-shopping selection between Express and Ground, and every shipment lifecycle transition remain byte-for-byte identical.
24. As a release engineer, I want the pilot to leave `ECommerce.Shared` untouched (no nupkg version bump), so that other services are not forced to consume a new shared package version.
25. As a release engineer, I want the pre-commit hook (`dotnet format`, `dotnet build`, then Basket tests) to gate every commit on the refactor branch, so that the branch cannot accumulate partial-validation commits. Shipping tests run manually before pushing.
26. As an architect, I want a CLAUDE.md "Shipping service exception" entry that **composes ADR-0011 by reference** (no new ADR) and **reuses the existing adding-a-new-slice runbook unchanged**, so that documentation stays DRY across pilots.
27. As an architect, I want the CLAUDE.md entry to **explicitly call out shipping-specific divergences** vs Order (no `IIntegrationMap<,>` / `DomainEventOutboxInterceptor` seam; `Carriers/` consolidated under `Infrastructure/Carriers/` rather than peer-layer; `Observability/` collapsed into `Infrastructure/Observability/`; HTTP write endpoints split per transition rather than bundled), so that future contributors understand why shipping looks slightly different.
28. As an architect, I want the decision on whether to continue propagating to remaining services (payment, saga) to be a separate ADR after this pilot lands, so that propagation stays informed by pilot learnings.
29. As an AI-assisted contributor, I want layout, namespaces, and architecture rules self-describing and analyzer-enforced, so that AI edits cannot silently drift across boundaries.
30. As an operator, I want the DLQ poller's call to `/internal/outbox/failed` (gated by `RequireService`) to continue working after the refactor, so that DLQ ingestion is not interrupted.
31. As an operator, I want trace IDs and correlation IDs to propagate identically through HTTP/saga inbound → `Shipment` mutation → outbox `Shipment*` events after the refactor, so that observability dashboards do not break. `ShippingMetrics` counter names and tags stay identical.
32. As an operator, I want the `CarrierPollingService` background pump and its interval/`SharedSecrets` options binding (`CarrierWebhookOptions`) to behave identically after the refactor, so that webhook polling is not interrupted.

## Implementation Decisions

### Pilot scope

- Pilot is `Shipping.Service` only. No other service changes.
- Propagation to remaining services (payment, saga) handled by a follow-up ADR.

### Project shape

- Single `Shipping.Service.csproj`. No split into `Shipping.Domain` / `Shipping.Application` / `Shipping.Infrastructure` projects.
- Boundaries enforced by namespace conventions + analyzer rules + architecture tests, not csproj references.

### Folder topology

- `Features/<Slice>/` — one folder per inbound trigger. Final slice list (14):
  - **Read (3):** `GetShipmentsByOrder/`, `GetShipmentById/`, `ListShipments/`.
  - **HTTP write (8):** `PickShipment/`, `PackShipment/`, `DispatchShipment/`, `DeliverShipment/`, `FailShipment/`, `ReturnShipment/`, `CancelShipment/`, `ProcessCarrierWebhook/`.
  - **Event consumer (1):** `OrderConfirmed/`.
  - **Saga command consumers (2):** `CreateShipmentCommand/`, `CancelShipmentCommand/`.
- `Domain/` — `Shipment`, `ShipmentLine`, `ShipmentStatus`, `ShipmentStatusHistoryEntry`, `ShipmentStatusSource`, `Money`, `OrderConfirmation`, `ShippingAddress`, `Warehouse`, `Abstractions/IShipmentStore`, `Abstractions/ICarrierGateway`, `Abstractions/IRateShopper` (extracted from `RateShoppingService` if needed). No EF / HTTP / carrier-impl references.
- `Contracts/Integration/` — `ShipmentCreatedEvent`, `ShipmentDispatchedEvent`, `ShipmentDeliveredEvent`, `ShipmentCancelledEvent`, `ShipmentFailedEvent`, `ShipmentReturnedEvent`, `ShipmentStatusChangedEvent`, inbound `OrderConfirmedEvent`.
- `Infrastructure/Data/EntityFramework/` — `ShippingContext`, `EfShipmentStore` (impl), EF configurations, `ShippingContextDesignTimeFactory`, `ShippingContextSeed`, `ShippingQaFixtures`, `EntityFrameworkExtensions`.
- `Infrastructure/Carriers/` — `FakeExpressCarrierGateway`, `FakeGroundCarrierGateway`, `FakeCarrierDispatchRegistry`, `FakeCarrierWebhookParser`, `CarrierStatusApplier`, `CarrierPollingService`, `RateShoppingService` (impl), `CarrierWebhookOptions`.
- `Infrastructure/Observability/` — `ShippingMetrics`.
- `Infrastructure/Outbox/` — `InternalOutboxEndpoints` (`RequireService`-gated ops endpoint).
- `Migrations/` — unchanged; `generated_code = true`.

### Dispatch model

- No MediatR. No in-house mediator.
- Endpoints and integration-event consumers take their slice handler class via constructor injection and call `HandleAsync(...)` directly.
- Slice handler classes are `internal sealed` with one public async method.

### Domain richness rule

- Rich domain: `Shipment` aggregate owns invariants and state transitions (pick → pack → dispatch → deliver, plus terminal fail/return/cancel branches, plus carrier-status applications from webhooks/polling). Existing methods on `Shipment` are preserved.
- Write-slice handlers are orchestration only: load aggregate, call domain method, persist, optionally publish integration event via outbox.
- Read-slice handlers bypass the aggregate and project directly from `ShippingContext` to `ShipmentResponse`.
- Event/command consumers (`OrderConfirmed`, `CreateShipmentCommand`, `CancelShipmentCommand`) follow the write-slice rule.

### Persistence

- `IShipmentStore` already exists in `Infrastructure/Data/`. Move the abstraction to `Domain/Abstractions/IShipmentStore.cs`. EF implementation `EfShipmentStore` stays in `Infrastructure/Data/EntityFramework/`.
- `ShippingContext` remains persistence-only.

### Outbox seam — **diverges from Order**

- No `IIntegrationMap<TDomainEvent, TIntegrationEvent>` abstraction.
- No `DomainEventOutboxInterceptor`.
- No `Entity` base / `IDomainEvent` marker / `Shipment.DomainEvents` collection.
- Each write slice constructs its integration event inline and persists via `outboxUnitOfWork.ExecuteAsync(...)` + `AddOutboxEvent(...)`, matching Inventory.
- Justification: shipping has no `ShippingContext.Translate` switch to extract today; introducing the seam would require inventing domain-event scaffolding from nothing (~600 LOC) for no functional benefit. CLAUDE.md "Inventory service exception" entry already documents this divergence pattern; "Shipping service exception" will reuse the same wording.

### Slice DI

- Each slice exposes a static class with `AddXxxSlice(this IServiceCollection)` extension. The extension registers the handler, any slice-specific options, and (for event/command slices) calls `AddEventHandler<TEvent, THandler>` from `ECommerce.Shared.Infrastructure.EventBus`.
- `Program.cs` chains slice extensions in a fluent manifest. Per-handler `AddScoped` and per-event `AddEventHandler` calls move into slice extensions.

### Namespaces

- `Shipping.Service.Domain`, `Shipping.Service.Domain.Abstractions`
- `Shipping.Service.Features.<Slice>`
- `Shipping.Service.Contracts.Integration`
- `Shipping.Service.Infrastructure.Data.EntityFramework`, `Shipping.Service.Infrastructure.Carriers`, `Shipping.Service.Infrastructure.Observability`, `Shipping.Service.Infrastructure.Outbox`

### Cross-slice sharing rule

- Rule of three: duplicate freely between slices; extract to `Domain/` (behavioral) or `Features/Shared/` (helper) only on the third use.
- NetArchTest rule forbids `Shipping.Service.Features.<X>` referencing `Shipping.Service.Features.<Y>` for any `X != Y`.

### Boundary enforcement (belt + suspenders)

- **NetArchTest** in `Shipping.Tests/Architecture/LayoutTests.cs`. Four rules, all enabled:
  1. `Shipping.Service.Domain.*` may not reference `Shipping.Service.Infrastructure.*` or `Shipping.Service.Features.*`.
  2. `Shipping.Service.Features.<X>.*` may not reference `Shipping.Service.Features.<Y>.*` for distinct slices.
  3. `Shipping.Service.Infrastructure.*` may not reference `Shipping.Service.Features.*`.
  4. `Shipping.Service.Contracts.*` may not reference any other internal `Shipping.Service.*` namespace.
- **Roslyn `Shipping.Service.LayoutAnalyzer`** raises the same four rules as build-time compiler errors via `.editorconfig`.

### Internal ops endpoints

- `InternalOutboxEndpoints` moves from `Endpoints/` to `Infrastructure/Outbox/InternalOutboxEndpoints.cs`.
- Wiring in `Program.cs` after slice registration.
- `RequireService` policy gate preserved.

### Shared library

- `ECommerce.Shared` not modified. No nupkg version bump.

### Validation

- Out of scope. Existing absence of `FluentValidation` / `DataAnnotations` preserved. Listed as a follow-up in CLAUDE.md exception entry.

### Rollout

- Branch `refactor/shipping-vsa`.
- Staged commits land in this order, each green:
  1. Scaffold NetArchTest project dependency + `Shipping.Tests/Architecture/LayoutTests.cs` with rules initially skipped.
  2. Move domain types into `Domain/`; move `IShipmentStore` to `Domain/Abstractions/`; rename namespaces.
  3. Move integration-event payloads into `Contracts/Integration/`; rename namespaces.
  4. Move `Carriers/` → `Infrastructure/Carriers/`; move `Observability/ShippingMetrics` → `Infrastructure/Observability/`; rename namespaces. Extract `ICarrierGateway` to `Domain/Abstractions/ICarrierGateway.cs`.
  5. Extract slices one at a time, each a green commit, in order: read slices first (`GetShipmentsByOrder`, `GetShipmentById`, `ListShipments`), then HTTP write slices (`PickShipment`, `PackShipment`, `DispatchShipment`, `DeliverShipment`, `FailShipment`, `ReturnShipment`, `CancelShipment`), then `ProcessCarrierWebhook`, then event/command slices (`OrderConfirmed`, `CreateShipmentCommand`, `CancelShipmentCommand`).
  6. Move `InternalOutboxEndpoints` to `Infrastructure/Outbox/`.
  7. Reshape `Shipping.Tests` to mirror `Features/<Slice>/`; keep `Domain/ShipmentTests` and `Carriers/*Tests` separate (carrier tests move under `Infrastructure/Carriers/` mirror in the tests project).
  8. Unskip NetArchTest rules; ship Roslyn `Shipping.Service.LayoutAnalyzer` project + `.editorconfig` rules.
  9. Add CLAUDE.md "Shipping service exception" entry composing ADR-0011 by reference.
- Single PR for review. Pre-commit hook gates every commit (no `--no-verify`, no deferral, no partial validation).

## Testing Decisions

### Test philosophy

- A good test verifies external behavior of a module through its public interface, not internal implementation details.
- Refactor must produce zero behavior change. Every existing `Shipping.Tests` test continues to pass without modification beyond namespace updates required by the rename.
- New tests added only for the architecture rules themselves (`LayoutTests.cs`). No new tests for the outbox seam (no seam introduced).

### Modules to test

- **`Shipment` aggregate (unchanged tests)** — existing `Shipping.Tests/Domain/ShipmentTests.cs` and `QaSeedFixturesTests.cs` cover state transitions, terminal status enforcement, status-history append rules. Kept verbatim, namespace only.
- **Per-slice handler/endpoint tests** — existing `Shipping.Tests/Api/*` tests migrate into `Shipping.Tests/Features/<Slice>/` without behavioral changes. Continue to use `ShippingWebApplicationFactory` and `IntegrationTestBase`. Specifically:
  - `GetShipmentsByOrderTests` → `Features/GetShipmentsByOrder/`.
  - `ListShipmentsTests` → `Features/ListShipments/`.
  - `ShipmentDispatchEndpointsTests` → `Features/DispatchShipment/`.
  - `ShipmentTerminalTransitionTests`, `ShipmentTransitionEndpointsTests` → split across `Features/DeliverShipment/`, `Features/ReturnShipment/`, `Features/FailShipment/`, `Features/CancelShipment/`, `Features/PickShipment/`, `Features/PackShipment/` based on the transition each test exercises.
  - `ShipmentOwnershipTests` → split per slice that enforces ownership (typically the read slices + cancel).
  - `ShipmentWebhookTests` → `Features/ProcessCarrierWebhook/`.
  - `CancelShipmentCommandHandlerTests`, `CreateShipmentCommandHandlerTests` → `Features/CancelShipmentCommand/`, `Features/CreateShipmentCommand/`.
  - `InternalOutboxEndpointsTests` → stays under `Infrastructure/Outbox/` mirror in tests project.
  - `HealthChecksTests`, `Authentication/*` → unchanged location (cross-cutting).
- **Carrier infrastructure tests** — existing `Shipping.Tests/Carriers/*` tests (`CarrierGatewayContractTests`, `CarrierPollingServiceTests`, `CarrierStatusApplierTests`, `RateShoppingServiceTests`) move to `Shipping.Tests/Infrastructure/Carriers/`. Behavioral coverage unchanged.
- **`Shipping.Tests/IntegrationEvents/*`** — `EventStreamConsolidationTests` and `MessagingProviderBootTests` move to `Shipping.Tests/Infrastructure/Outbox/` or stay under a `Shipping.Tests/IntegrationEvents/` root (decision deferred to implementation; tests are about platform plumbing, not slices).
- **`Shipping.Tests/Architecture/LayoutTests.cs`** — new NetArchTest rules acting as executable specification of the boundary policy. Fails if any future contributor (human or AI) introduces a cross-boundary reference.
- **`EfShipmentStore`** — covered indirectly by integration tests through `WebApplicationFactory<Program>`. No new tests unless impl changes beyond the rename.

### Prior art in the codebase

- `Shipping.Tests/IntegrationTestBase.cs` + `Shipping.Tests/ShippingWebApplicationFactory.cs` — existing factory + base used by all current integration tests. Refactor preserves both at the root of the tests project.
- `Shipping.Tests/Domain/ShipmentTests.cs` — existing aggregate-level unit tests. `Given_When_Then` underscored display names preserved (`CA1707` suppressed via `Directory.Build.props`).
- `Inventory.Tests/Architecture/LayoutTests.cs` — closest prior-art layout-test file; copy structure and adapt namespaces.
- `Inventory.Service.LayoutAnalyzer` (and Order/Product/Basket/Auth equivalents) — closest prior-art Roslyn analyzer; copy structure and rename namespaces.
- `Inventory.Tests/Features/*` — closest prior-art reshape of feature tests; copy structure.
- Pre-commit hook (`dotnet husky run --group pre-commit`) enforces `dotnet format --verify-no-changes` + `dotnet build --no-restore` + Basket tests on every commit. Shipping tests run manually per root `CLAUDE.md` sandbox policy before pushing.

## Out of Scope

- Refactoring any other service (basket, product, auth, inventory, order, payment, saga, api-gateway). Propagation to payment/saga is a follow-up ADR.
- Modifying `ECommerce.Shared`. The pilot composes existing `AddSqlServerDatastore`, `AddOutbox`, `AddPlatformEventBus`, `AddEventHandler`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddPlatformOpenApi`, `AddJwtAuthentication`, `AddRequireServicePolicy`.
- Adding request validation (FluentValidation / DataAnnotations). Listed as follow-up in CLAUDE.md exception entry.
- Introducing MediatR or any mediator-style dispatcher.
- Splitting `Shipping.Service.csproj` into multiple projects.
- Changing the `Shipment` / `ShipmentLine` / `ShipmentStatusHistoryEntry` / `Warehouse` / `OrderConfirmation` database schema. No new EF migrations.
- Changing integration event payload contracts. Only their location (folder + namespace) moves.
- Changing the outbox table, dispatcher, or retry/DLQ behavior in `ECommerce.Shared.Infrastructure.Outbox`.
- Changing `ShippingApiEndpoints`'s public HTTP routes, response shapes, status codes, or auth requirements.
- Changing CI/CD pipelines, Docker Compose, Kubernetes manifests, or Azure pipelines.
- Performance optimization. CQRS-lite read-path decision is structural, not performance-driven.
- Introducing `IIntegrationMap<,>` / `DomainEventOutboxInterceptor` / domain-event scaffolding on `Shipment`. Explicit divergence from Order; matches Inventory.
- Promoting `CarrierPollingService` into a `Features/` slice. Stays in `Infrastructure/Carriers/`.
- Splitting the `/webhooks/carrier/{carrierKey}` route into per-carrier slices. Stays one `ProcessCarrierWebhook` slice.
- Writing a new ADR. CLAUDE.md "Shipping service exception" entry composes ADR-0011 by reference.
- Writing a new "adding-a-new-slice" runbook. The existing runbook is reused unchanged.

## Further Notes

- Shipping is the **sixth** pilot. Order/Product/Basket/Auth/Inventory pilots are landed. After shipping, only payment and saga remain (per ADR-0011's candidate propagation order, saga last because its orchestrator shape stresses the layout differently).
- The shipping refactor is **smaller in conceptual scope** than Order because:
  - No `Translate` switch to extract (Inventory-style outbox seam).
  - No Redis read-side to refactor.
  - No saga state machine.
  - But **larger in surface area** than Inventory because shipping has 14 inbound triggers vs Inventory's 11, plus a top-level `Carriers/` folder with 7+ types that needs relocation, plus a hosted background service (`CarrierPollingService`).
- The carrier abstractions (`ICarrierGateway`, rate shopping) are the most interesting design question. The pilot resolves it by: declaring `ICarrierGateway` in `Domain/Abstractions/`, keeping all implementations under `Infrastructure/Carriers/`, and letting slices inject the abstraction. `RateShoppingService` may grow an `IRateShopper` abstraction if any slice needs to mock it; otherwise it stays a concrete service injected from `Infrastructure/Carriers/` (decision deferred to slice extraction phase).
- The "duplicate first, extract on third" rule is load-bearing here because the 8 HTTP write slices share a lot of mechanical shape (load shipment → call transition method → persist → publish event). The slice-to-slice NetArchTest rule prevents premature `Features/Shared/` extraction. Expect the first extraction (if any) to be a shared `OutboxEnvelopeBuilder` helper at the third occurrence, not before.
- Behavioral guidance from root `CLAUDE.md` applies: surgical changes only, no improving adjacent code, match existing style, push back on over-engineering. The pilot is large in line count but mechanical in intent.
