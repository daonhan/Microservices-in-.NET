# PRD — Payment domain-event depth + shared Outbox unit-of-work

> Depends on: [PRD-Outbox-UoW-Deep-Seam.md](PRD-Outbox-UoW-Deep-Seam.md). Phases 4–6 below require that PRD's deep-overload seam (and its `ECommerce.Shared` 2.16.0 bump) to have shipped first; Phase 5 here also relies on its uniform OTEL helper.

## Problem Statement

As a developer working in this repo, I have to repeat the same transactional ceremony every time I publish an Integration Event. The PR branch has started removing this ceremony for Payment Capture, but the broader problem remains: Payment Refund, Payment event handlers, **Inventory** event handlers, and **Shipping** publishing endpoints still expose some combination of `TransactionScope`, EF Core execution strategy calls, manual Integration Event construction, `IOutboxStore.AddOutboxEvent`, and `scope.Complete()`.

This is a shallow seam in two ways:

1. **Payment is still shallow compared to Order.** The `Order` aggregate raises domain events and `OrderContext.ExecuteAsync` translates them into Integration Events on the Outbox in one transaction. Payment now has the first captured-payment domain event slice on the PR branch, but Authorize, Refund, Fail, and Void-style event emission are not yet all concentrated behind the aggregate/context translation module.
2. **The Outbox interface is too low-level.** `IOutboxStore` exposes the primitives (`CreateExecutionStrategy`, `AddOutboxEvent`) and forces every caller to learn EF execution strategies, ambient transactions, and event construction. Cross-cutting concerns that belong on this seam (OTEL spans for outbox writes, idempotency keys, retry policy) have no single home.

The deletion test confirms both:

- Delete the ceremony in any one remaining Payment publisher and the same shape reappears at the next state transition or handler. The Payment module is becoming deeper, but it is not complete until all Payment-originated Integration Events go through one aggregate/context translation path.
- Delete `IOutboxStore` and the work doesn't concentrate — it scatters into every publishing service. The Outbox Module is currently a primitive bag whose interface is nearly as wide as its implementation.

## Solution

Pull the Order pattern down into Payment, and at the same time deepen the shared Outbox seam so that every publishing service (Payment, Inventory, Shipping, future) gets the same atomic "do work + emit events" semantics.

From a developer's perspective:

- A Payment endpoint loads the `Payment` aggregate, calls `payment.Capture(...)` (or `Refund`, `Authorize`), and asks the `PaymentContext` to execute and persist. The Integration Event arrives in the Outbox automatically. The endpoint never sees `TransactionScope`, never constructs an Integration Event, never calls `AddOutboxEvent`.
- Inventory event handlers and Shipping endpoints publish through the same deepened Outbox seam: they describe the unit of work and the events that go with it; the service context supplies its EF execution strategy and persistence, while the Outbox module owns the ambient transaction and event enqueue.
- The seam is transport-neutral. It writes Integration Events to the Outbox and relies on the existing `IEventBus` publisher path, so the merged `Messaging:Provider` switch keeps working for RabbitMQ by default and Azure Service Bus when selected.
- Adding a new Payment state transition (partial refund, void) is one method on the `Payment` aggregate plus one new domain-event type plus one new translation entry. No new transactional plumbing.
- Tests for Payment state transitions are pure model tests with no EF, no outbox, and no broker adapter. Tests for the Payment context assert atomicity (work + events commit together; on failure both roll back). Endpoint tests shrink to thin smoke tests.

## User Stories

1. As a Payment service developer, I want the `Payment` aggregate to own Authorize / Capture / Refund state transitions, so that the rules of the state machine live in one place I can test without spinning up EF Core.
2. As a Payment service developer, I want the `Payment` aggregate to raise a domain event for each state transition, so that callers do not have to remember to construct an Integration Event after every transition.
3. As a Payment service developer, I want `PaymentContext.ExecuteAsync` to translate Payment domain events into Integration Events on the Outbox in the same transaction as the state change, so that I cannot accidentally publish without persisting or persist without publishing.
4. As a Payment service developer, I want the Capture endpoint to shrink to "load aggregate, call `Capture`, save through context", so that the endpoint contains zero transactional plumbing.
5. As a Payment service developer, I want the Refund endpoint to shrink in the same way, so that the two endpoints look like siblings rather than copy-pasted ceremony.
6. As a Payment service developer, I want partial-refund and other future transitions to be additive on the aggregate plus a new translation entry, so that I never copy a `TransactionScope` block again.
7. As a saga participant maintaining Payment, I want illegal state transitions (Capture before Authorize, Refund before Capture) to throw from the aggregate, so that the rules are enforced regardless of which endpoint or handler triggered them.
8. As a saga participant maintaining Payment, I want idempotent re-application of the same transition (Capture on an already-Captured payment) to behave consistently with how Order handles it, so that the saga's at-least-once delivery semantics do not corrupt state.
9. As a developer working on `ECommerce.Shared`, I want a deep "outbox unit-of-work" interface that takes a unit of work and a set of events and commits them atomically, so that publishing services stop hand-rolling `CreateExecutionStrategy + TransactionScope + AddOutboxEvent + Complete`.
10. As a developer working on `ECommerce.Shared`, I want the deepened Outbox interface to be the single place that coordinates the caller-supplied EF execution strategy with the ambient transaction, so that retry, tracing, and idempotency policy can be added once and apply everywhere.
11. As a developer working in Inventory, I want `OrderCreatedEventHandler` to express its work as "reserve stock and emit `StockReserved`" through the new Outbox seam, so that the handler is no longer responsible for transaction ceremony.
12. As a developer working in Shipping, I want shipment-mutation endpoints to use the new Outbox seam, so that Shipping reaches parity with Inventory and Payment on this concern.
13. As a developer reading the codebase, I want `IOutboxStore`'s low-level primitives (`CreateExecutionStrategy`, raw `AddOutboxEvent`) to remain available only for the new unit-of-work module's implementation, so that callers cannot accidentally bypass the deepened seam.
14. As an operator, I want OTEL spans for every outbox unit-of-work invocation, so that I can see "started → committed" or "started → rolled back" across every publishing service.
15. As an operator, I want the unit-of-work to surface a single metric for outbox transactional failures across all services, so that retry storms and broken aggregates are visible in Grafana without per-service instrumentation.
16. As a developer writing tests, I want to verify Payment state transitions without instantiating `PaymentContext`, `IOutboxStore`, or the payment gateway, so that the model tests run fast and stay focused on domain rules.
17. As a developer writing tests, I want to verify that `PaymentContext.ExecuteAsync` commits state and Integration Events together, and rolls both back on failure, so that the atomicity guarantee is regression-tested.
18. As a developer writing tests, I want to verify that the new Outbox unit-of-work behaves the same way (atomic commit, atomic rollback) when used directly against an arbitrary `DbContext`, so that adoption in Inventory and Shipping is safe.
19. As a developer writing tests, I want existing `WebApplicationFactory<Program>`-based Payment endpoint tests to continue passing with minimal changes, so that the refactor is observably behaviour-preserving at the HTTP boundary.
20. As a developer adopting the new pattern in another service, I want the Payment service to read like a worked example of "aggregate raises domain event → context translates → endpoint stays thin", so that I can copy the pattern into Shipping without re-deriving it.
21. As a maintainer of `ECommerce.Shared`, I want the package version to be bumped when the deepened Outbox seam is shipped, so that consumers explicitly opt into the new interface per the local-NuGet-feed workflow (ADR-0005).
22. As a CI maintainer, I want `dotnet format` and `TreatWarningsAsErrors` to keep passing, so that nothing in this refactor relies on style or warning exemptions beyond what the repo already documents.
23. As a developer running either supported broker provider, I want the Outbox unit-of-work to remain independent of RabbitMQ and Azure Service Bus adapter details, so that `Messaging:Provider` can change the delivery transport without changing domain or transactional publishing code.
24. As an Order service maintainer, I want `OrderContext.ExecuteAsync` to call the shared deep overload rather than re-implement transaction + dequeue + `SaveChanges` locally, so that Order and Payment share one transactional-publishing implementation and the "Order is the worked example" claim is true.
25. As an Inventory service maintainer, I want every ceremony site in Inventory (`OrderCreatedEventHandler`, `OrderCancelledEventHandler`, `OrderConfirmedEventHandler`, and the `restock`/`threshold`/`reserve` endpoints in `InventoryApiEndpoints`) to adopt the shallow overload, so that no `CreateExecutionStrategy + TransactionScope` block remains in Inventory.
26. As a Shipping service maintainer, I want every ceremony site in Shipping (`OrderCancelledEventHandler`, `OrderConfirmedEventHandler`, `StockCommittedEventHandler`, the `dispatch`/`webhook`/`ApplyTransitionAsync` paths in `ShippingApiEndpoints`, and `CarrierPollingService.PollOnceAsync`) plus the `CarrierStatusApplier` helper to adopt the shallow overload, so that no `CreateExecutionStrategy + TransactionScope` block remains in Shipping.
27. As a saga operator, I want every migrated subscriber handler to preserve the existing exception-propagation contract that the provider-agnostic DLQ capture relies on, so that retries and dead-letter routing continue to work for both RabbitMQ and Azure Service Bus after the migration.
28. As a QA owner, I want the saga happy-path and compensation smoke tests re-verified end-to-end after each migration phase, so that regressions across Order ↔ Inventory ↔ Payment ↔ Shipping are caught before the package version is bumped again.

## Implementation Decisions

### Modules

**Modified — `Payment` aggregate (Payment service `Models/`)**
- Owns Authorize / Capture / Refund state machine and any future transitions. State transitions become methods on the aggregate, not free-floating field assignments inside endpoints.
- Raises domain events for each transition: a `PaymentAuthorizedDomainEvent`, a `PaymentCapturedDomainEvent`, and a `PaymentRefundedDomainEvent`. Domain events are dequeued by the context after persistence, mirroring the `Order` pattern.
- Inherits or composes with the same `Entity` / `IDomainEvent` base used by `Order`. The shared base lives in `ECommerce.Shared` (or in Order today and gets promoted to shared if not already there).

**Modified — `PaymentContext` (Payment service `Infrastructure/Data/EntityFramework/`)**
- Gains an `ExecuteAsync(Func<Task>)` that mirrors `OrderContext.ExecuteAsync`. Internally it now goes through the new shared Outbox unit-of-work module rather than re-implementing the transaction scope and event enqueue locally.
- Supplies the EF Core execution strategy that belongs to the Payment `DbContext`; endpoint and handler call sites do not see execution strategies or transactions.
- Owns the translation table from Payment domain events to Payment Integration Events (`PaymentAuthorizedDomainEvent → PaymentAuthorizedEvent`, `PaymentCapturedDomainEvent → PaymentCapturedEvent`, `PaymentRefundedDomainEvent → PaymentRefundedEvent`).
- The translation table is the single place where Payment Integration Event shapes are constructed.

**New — Outbox unit-of-work module (`shared-libs/ECommerce.Shared/Infrastructure/Outbox`)**
- Deep seam: callers describe work to execute against their service `DbContext` and return the `Event`s to enqueue; the module commits both atomically under the supplied EF execution strategy.
- Owns the ambient `TransactionScope`, the calls to `AddOutboxEvent`, and the `scope.Complete()`. Service contexts remain responsible for `SaveChangesAsync` and domain-event translation inside the delegate.
- Owns OTEL instrumentation for outbox transactional work and the single metric for outbox-transaction outcomes.
- Surface is intentionally narrow: one operation that takes the unit of work and the events; an overload or shape that supports "events depend on the result of the unit of work" (so Order/Payment can dequeue domain events after `SaveChangesAsync`).

**Modified — `IOutboxStore` (`shared-libs/ECommerce.Shared/Infrastructure/Outbox`)**
- Stays as the lower-level primitive for adding events and creating execution strategies. The new unit-of-work module is built on top of it; `IOutboxStore` is no longer the recommended caller-facing seam for transactional publishing.
- No public method is removed in this PRD; deprecation of caller-facing usage is signalled by docs and by all in-repo callers migrating off it.

**Modified — Payment endpoints (`Endpoints/PaymentApiEndpoints.cs`)**
- Capture and Refund endpoints lose their `outboxStore.CreateExecutionStrategy().ExecuteAsync(...)` blocks and their direct `AddOutboxEvent` calls. They become: load aggregate → check state / call gateway → call `paymentContext.ExecuteAsync(() => { aggregate.Transition(...); return Task.CompletedTask; })` → record metrics → return response.
- Authorize is in scope to fit the same shape as Capture and Refund (today the Payment service may construct Payments outside the aggregate transition pattern; this PRD aligns it).

**Modified — Order `OrderContext.ExecuteAsync`**
- Today `OrderContext.ExecuteAsync` (`order-microservice/Order.Service/Infrastructure/Data/EntityFramework/OrderContext.cs:54`) hand-rolls the full ceremony in parallel with the shared seam. It migrates to the deep overload introduced by [PRD-Outbox-UoW-Deep-Seam.md](PRD-Outbox-UoW-Deep-Seam.md) so Order and Payment share one transactional-publishing implementation. Without this migration, the PRD claim that "Order is the worked example" is misleading — the two services would call two different code paths.

**Modified — Inventory ceremony sites (full list)**
All four files migrate to the shallow `IOutboxUnitOfWork` overload:
- `inventory-microservice/Inventory.Service/IntegrationEvents/EventHandlers/OrderCreatedEventHandler.cs`
- `inventory-microservice/Inventory.Service/IntegrationEvents/EventHandlers/OrderCancelledEventHandler.cs`
- `inventory-microservice/Inventory.Service/IntegrationEvents/EventHandlers/OrderConfirmedEventHandler.cs`
- `inventory-microservice/Inventory.Service/Endpoints/InventoryApiEndpoints.cs` — the `restock`, `threshold`, and `reserve` POST/PUT handlers each contain a `CreateExecutionStrategy + TransactionScope` block.

**Modified — Shipping ceremony sites (full list)**
All five files migrate to the shallow `IOutboxUnitOfWork` overload. Shipping does **not** get the aggregate-with-domain-events refactor in this PRD; it only adopts the seam:
- `shipping-microservice/Shipping.Service/IntegrationEvents/EventHandlers/OrderCancelledEventHandler.cs`
- `shipping-microservice/Shipping.Service/IntegrationEvents/EventHandlers/OrderConfirmedEventHandler.cs`
- `shipping-microservice/Shipping.Service/IntegrationEvents/EventHandlers/StockCommittedEventHandler.cs`
- `shipping-microservice/Shipping.Service/Endpoints/ShippingApiEndpoints.cs` — the `dispatch` and `webhooks/carrier/{carrierKey}` routes plus the `ApplyTransitionAsync` helper used by `pick`/`pack`/`cancel`/`deliver`/`fail`/`return`.
- `shipping-microservice/Shipping.Service/Carriers/CarrierPollingService.cs` — `PollOnceAsync` wraps a `CreateExecutionStrategy + TransactionScope` block.

**Modified — `CarrierStatusApplier` helper**
- `shipping-microservice/Shipping.Service/Carriers/CarrierStatusApplier.ApplyAsync` currently accepts `IOutboxStore` and enqueues directly. It is called from both `CarrierPollingService` and the carrier webhook route. The migration must either keep the helper outbox-aware via the deep seam's events-from-result shape, or change the helper to return the events to enqueue and let the caller pass them to `IOutboxUnitOfWork.ExecuteAsync`. The change to this helper is in-scope.

### Architectural decisions

- **No central orchestrator is introduced.** ADR-0008 (saga choreography) stands. Payment continues to react to events and emit events; this PRD only changes how Payment internally couples state changes to event publication.
- **No change to Integration Event shapes.** `PaymentCapturedEvent`, `PaymentRefundedEvent`, `PaymentAuthorizedEvent` keep their existing fields. Subscribers (Order, Shipping) see no contract change.
- **No change to provider-selected messaging topology.** RabbitMQ remains the default local provider with fanout exchange `ecommerce-exchange` and the existing DLQ contract (ADR-0004). Azure Service Bus remains selected through `Messaging:Provider=AzureServiceBus` and uses the same Integration Event contracts and Outbox publisher path.
- **No change to the database-per-service decision** (ADR-0007) or the JWT/JWKS model (ADR-0003).
- **`ECommerce.Shared` version is bumped from the merged `2.14.0` baseline to `2.15.0` for the first Outbox unit-of-work consumer**, per the local-NuGet-feed workflow (ADR-0005). Other consumers opt in by upgrading their `<PackageReference>` when they adopt the seam.
- **Domain events are an internal concern.** `IDomainEvent` and the dequeueing machinery are not exposed to event subscribers. Only Integration Events cross service boundaries.
- **Aggregate translation tables are per-service.** Each context (Order today, Payment now, Shipping later) owns its own domain-event-to-Integration-event translation; `ECommerce.Shared` provides the dequeue/translate plumbing but not the mapping.
- **Failure semantics are preserved.** If the gateway call (`gateway.CaptureAsync`, `gateway.RefundAsync`) is currently outside the transaction, it stays outside. The unit-of-work covers the database state change and the outbox enqueue; gateway side effects remain the endpoint's responsibility to sequence.
- **Subscriber-pipeline exception contract is preserved.** Inventory and Shipping event handlers are invoked by the platform subscriber under the recently-landed provider-agnostic DLQ capture (RabbitMQ + Azure Service Bus). Migrating those handlers from `outboxStore.CreateExecutionStrategy().ExecuteAsync(... TransactionScope ...)` to `IOutboxUnitOfWork.ExecuteAsync(strategy, work)` must preserve the existing exception-propagation behaviour: an exception thrown inside the unit of work rolls back the transaction and bubbles to the subscriber, which then drives retry → DLQ exactly as today. Wrapping or swallowing those exceptions in the seam is **not** allowed. A regression test per migrated handler asserts that a thrown exception inside the work delegate produces the same outward observable behaviour (DB rollback, no outbox row, exception visible to the subscriber) as the pre-migration code.
- **Order is part of this PRD's scope.** Phase 4 migrates `OrderContext.ExecuteAsync` to the deep overload from [PRD-Outbox-UoW-Deep-Seam.md](PRD-Outbox-UoW-Deep-Seam.md) so Order and Payment converge on one implementation. This is a behaviour-preserving refactor with no Integration Event payload changes.

### API contracts

- Payment HTTP API is unchanged. Same routes, same request/response shapes, same status codes for success, conflict, and not-found.
- `PaymentCapturedEvent`, `PaymentRefundedEvent`, `PaymentAuthorizedEvent` Integration Event payloads are unchanged.

### Schema changes

- None expected. The aggregate-with-domain-events pattern stores events in memory on the entity and dequeues them in the context; it does not require new persistent tables. Outbox tables remain as defined by ADR-0002.

## Testing Decisions

A good test in this repo asserts on **external behaviour** through the seam the user crosses. It does not assert on private fields, EF Core change-tracker entries, the raw rows in the outbox table, or which method was called on which mock. `Given_When_Then` naming is mandatory (the `CA1707` exemption exists for this).

### Modules to test

**`Payment` aggregate (model tests)**
- New: pure model tests for state transitions and invariants. Authorize on a fresh payment succeeds; Capture before Authorize throws; Capture after Authorize succeeds and raises one `PaymentCapturedDomainEvent`; Refund before Capture throws; Refund after Capture succeeds and raises one `PaymentRefundedDomainEvent`; idempotent re-application matches the chosen Order semantics. These tests must not require EF Core, the Outbox, RabbitMQ, Azure Service Bus, or the payment gateway.
- Prior art: `Order` model tests in `order-microservice/Order.Tests/`.

**`PaymentContext.ExecuteAsync` (integration tests)**
- New: with a real provider (LocalDB or Testcontainers SQL Server, whichever the repo already uses for Order context tests), assert that committing a transition writes both the `Payment` row change and the corresponding outbox row in one transaction; that throwing inside the unit of work rolls both back; that no Integration Event reaches the outbox if `SaveChangesAsync` fails.
- Prior art: `OrderContext.ExecuteAsync` integration tests in `order-microservice/Order.Tests/`.

**Outbox unit-of-work module (`ECommerce.Shared` integration tests)**
- New: against a representative `DbContext`, assert atomic commit (state + events together), atomic rollback on a thrown exception, and that the OTEL span / metric is emitted on both success and failure paths.
- Prior art: existing Outbox tests in `shared-libs/ECommerce.Shared.Tests` (whichever project hosts them).

**Payment endpoints (`WebApplicationFactory<Program>` smoke)**
- Existing endpoint tests should continue to pass with minimal changes. Where tests previously asserted on `IOutboxStore` mock interactions, they should be rewritten to assert on the visible HTTP contract and on the Integration Events that landed in the outbox.
- Prior art: existing `PaymentApiEndpointTests` and the Order endpoint tests.

**Inventory `OrderCreatedEventHandler` and Shipping endpoints (regression)**
- Existing handler/endpoint tests are expected to keep passing after migration to the new Outbox unit-of-work, with no behaviour change at the seam they assert on. If a test fails because it was asserting on internal `TransactionScope` or `CreateExecutionStrategy` calls, that test was over-specifying and gets rewritten to assert on outcomes.

**Inventory `OrderCancelledEventHandler` / `OrderConfirmedEventHandler` (regression)**
- Same regression contract as `OrderCreatedEventHandler`: existing tests must keep passing or be rewritten to assert reservation/commit/release outcomes and resulting Integration Events.

**Inventory `InventoryApiEndpoints` (`restock` / `threshold` / `reserve`)**
- Existing endpoint tests covering low-stock and depleted crossing events must keep passing. Migration must not change which events fire under which threshold transitions.

**Shipping `OrderCancelledEventHandler` / `OrderConfirmedEventHandler` / `StockCommittedEventHandler` (regression)**
- Existing handler tests must keep passing. `StockCommittedEventHandler`'s "OrderConfirmed-must-precede" guard is preserved by the migration.

**Shipping `CarrierPollingService.PollOnceAsync` and `CarrierStatusApplier`**
- New: assert that migrating the helper does not change which milestone events (`ShipmentDelivered`, `ShipmentFailed`) and which `ShipmentStatusChangedEvent` are emitted for each `CarrierStatus` code. Test from both call sites (polling background service and webhook endpoint) to cover the helper's two adopters.

**Subscriber-pipeline DLQ regression**
- For each migrated subscriber handler, a single test asserts that throwing inside the work delegate (e.g. `_inventoryStore.Reserve` throws) results in: no Outbox row enqueued, DB state unchanged, and the exception observable to the subscriber (so DLQ capture continues to work). This proves the seam preserves the exception contract the provider-agnostic DLQ poller relies on.

**Saga smoke-test re-verification**
- After each phase that migrates a subscriber handler or publishing endpoint, the existing saga happy-path and compensation smoke tests are re-run end-to-end (locally, against the docker-compose stack or the equivalent in-repo harness). The migration is not behaviour-preserving on paper alone.

### What we are NOT testing

- We are not testing EF Core's execution strategy or `TransactionScope` itself.
- We are not testing broker delivery in this PRD. RabbitMQ and Azure Service Bus adapter behavior is covered by the messaging/provider tests; this PRD only needs to prove that transactional publishing writes provider-neutral Outbox rows.
- We are not adding new tests for `RedisProductPriceProvider`, gateway/DLQ behaviour, or unrelated services.

## Out of Scope

- **The deep overload of `IOutboxUnitOfWork` itself.** Designing and shipping the deep (DbContext + dequeue + SaveChanges) overload is owned by [PRD-Outbox-UoW-Deep-Seam.md](PRD-Outbox-UoW-Deep-Seam.md). This PRD only consumes that overload (in `OrderContext` and `PaymentContext`) and the existing shallow overload (in Inventory and Shipping).
- **Promoting `Entity` / `IDomainEvent` into `ECommerce.Shared`.** If duplicated between Order and Payment, that consolidation is a separate decision.
- **Shipping `IShipmentStore` deepening to saga-aware operations.** Item #3 from the architecture review is deliberately deferred. Shipping adopts the new Outbox seam in this PRD but does not get its own aggregate-with-domain-events refactor.
- **A passive Saga state-machine module** (item #5 from the review). ADR-0008 stands; this PRD does not introduce any saga coordinator.
- **Removing or refactoring the API Gateway dual-provider abstraction.** ADR-0001 stands.
- **Splitting the Product price cache seam in Order.** Separate refactor.
- **Auth aggregate / token lifecycle module.** Separate refactor.
- **Changes to Integration Event payloads or provider messaging topology.** Out of scope.
- **Changes to schema migrations or outbox table layout.** Out of scope.
- **MediatR, AutoMapper, FluentValidation, Polly, Scrutor, Serilog adoption.** Explicitly forbidden by repo conventions.

## Further Notes

- The Order service is the worked example for the Payment-side change. Anywhere this PRD says "mirrors Order", the answer is "read `OrderContext.ExecuteAsync` and the `Order` aggregate, and bring Payment to the same shape."
- The deepened Outbox seam in `ECommerce.Shared` is the load-bearing unlock. Once it lands, future deepening (Shipping aggregate, additional saga participants) is additive: write the aggregate, write the translation table, the seam is already there.
- After merging `origin/main`, `ECommerce.Shared` entered the branch at `2.14.0` and all services have provider-aware messaging registration. The Outbox unit-of-work package bump is `2.15.0`; this PRD should not reintroduce RabbitMQ-only composition-root wiring.
- Per ADR-0005, after the `ECommerce.Shared` change: `dotnet pack -c Release` from `shared-libs/ECommerce.Shared`, push the resulting `.nupkg` to `local-nuget-packages/`, bump the shared package version from the current baseline, and update each consumer's `<PackageReference>`. Consumers do not see the deepened seam until they upgrade; Payment is the first consumer on `2.15.0`.
- `WebApplicationFactory<Program>` tests that assert Outbox state should keep disabling the Outbox poller, and broker boot tests should keep removing provider subscriber hosted services so background delivery cannot race assertions.
- Pre-commit (`.husky/task-runner.json`) only runs Basket tests. Payment, Inventory, Shipping, and `ECommerce.Shared` test suites must be run manually before pushing.
- `Directory.Build.props` enforces `TreatWarningsAsErrors`; the migration must not introduce new warnings or rely on new `NoWarn` exemptions.
