# PRD — Deepen the shared Outbox unit-of-work seam

> Status: draft, 2026-05-15.
> Companion / dependent: [PRD-Payment-Depth-Outbox-UoW.md](PRD-Payment-Depth-Outbox-UoW.md). Phases 4–6 of that PRD depend on this one shipping first.

## Problem Statement

The shared `IOutboxUnitOfWork` shipped on the Payment branch (`ECommerce.Shared` 2.15.0) only owns *part* of the transactional-publishing seam. It runs a caller-supplied delegate inside an EF execution strategy + ambient `TransactionScope` and enqueues the events returned by the delegate. It does not own:

- the `DbContext` reference used inside the unit of work,
- the EF Core `SaveChangesAsync(acceptAllChangesOnSuccess: false)` call,
- the `ChangeTracker`-based domain-event dequeue used by aggregates,
- the post-commit `ChangeTracker.AcceptAllChanges()` step.

As a result:

1. `OrderContext.ExecuteAsync` (`order-microservice/Order.Service/Infrastructure/Data/EntityFramework/OrderContext.cs:54`) still hand-rolls the full pattern — `Database.CreateExecutionStrategy()`, `TransactionScope`, ChangeTracker dequeue, `SaveChangesAsync`, `AddOutboxEvent`, `AcceptAllChanges`, `scope.Complete()` — and does **not** call the shared seam.
2. `PaymentContext.ExecuteAsync` does call the shared seam, but re-implements ChangeTracker dequeue + `SaveChangesAsync` above it. The shared seam alone is not enough for an aggregate-with-domain-events caller.
3. Inventory and Shipping handlers/endpoints have no aggregate-with-domain-events pattern at all (their stores expose `Reserve/Release/CreateShipmentsForOrder/...` directly), so they would adopt a seam shape that *is* enough for them but inconsistent with how Order and Payment use it.

The deletion test: if you delete `IOutboxUnitOfWork` today, only the Payment Capture/Refund/Authorize/Fail/Void path scatters. Order is unaffected — the seam was never deep enough to absorb Order's pattern. That asymmetry is the bug this PRD fixes.

## Solution

Make `IOutboxUnitOfWork` deep enough that **every** transactional-publishing caller in the repo can call it without re-implementing EF dequeue, `SaveChanges`, or `AcceptAllChanges`. Keep the existing shallow overload for non-aggregate callers (Inventory, Shipping handlers/endpoints), but add a deeper overload that takes the `DbContext` and the aggregate-entity base type and owns the dequeue + persist + accept-changes contract end-to-end.

From a developer's perspective, after this PRD ships:

- A service with an aggregate-and-domain-events pattern (Order, Payment) calls `outboxUoW.ExecuteAsync(myContext, () => { aggregate.Mutate(...); return Task.CompletedTask; }, translator)` and the seam handles everything inside the transaction.
- A service without that pattern (Inventory, Shipping endpoints/handlers) calls `outboxUoW.ExecuteAsync(strategy, () => { ... do work ...; return events; })` exactly as today.
- `OrderContext.ExecuteAsync` becomes a thin wrapper that delegates to the deep overload with its translation table; the hand-rolled transaction code is deleted.
- `PaymentContext.ExecuteAsync` collapses to the same thin shape as `OrderContext.ExecuteAsync`.
- The next aggregate-bearing service (Shipping, when it gets the deeper refactor — out of scope here) inherits the same shape without writing any transaction code.

## User Stories

1. As a developer working on `ECommerce.Shared`, I want `IOutboxUnitOfWork` to expose a deep overload that takes a `DbContext`, a unit-of-work delegate, and a domain-event translator, so that aggregate-bearing services do not re-implement EF dequeue or `SaveChanges` semantics.
2. As a developer working on `ECommerce.Shared`, I want the existing shallow overload (strategy + delegate returning events) to remain unchanged, so that Inventory and Shipping handler/endpoint adoption is not blocked on adopting domain events.
3. As an Order service maintainer, I want `OrderContext.ExecuteAsync` to be a thin wrapper around the deep overload with Order's translation table, so that the saga linchpin shares one seam with Payment.
4. As a Payment service maintainer, I want `PaymentContext.ExecuteAsync` to collapse to the same thin wrapper shape, so that Order and Payment look like siblings rather than two divergent implementations.
5. As a developer adopting the seam in a service without an aggregate, I want the shallow overload to keep working with no breaking changes, so that Inventory and Shipping adoption is purely additive.
6. As an operator, I want both overloads to emit the same OTEL span and the same outbox-transaction outcome metric, so that observability is uniform across aggregate and non-aggregate callers.
7. As a maintainer of `ECommerce.Shared`, I want the deepened seam shipped as a single shared-package version bump, so that consumer migration in the dependent PRD happens against one stable contract.
8. As a developer writing tests, I want shared-library tests to cover the deep overload's atomic commit, atomic rollback, ChangeTracker dequeue, and `AcceptAllChanges` semantics, so that Order's and Payment's correctness regression-tests live in one place.
9. As a developer writing tests, I want Order's existing `OrderContext.ExecuteAsync` integration tests to keep passing after `OrderContext` migrates to the deep overload, so that the migration is observably behaviour-preserving.
10. As a developer writing tests, I want Payment's existing `PaymentContext.ExecuteAsync` integration tests to keep passing after `PaymentContext` migrates to the deep overload, so that the migration is observably behaviour-preserving.
11. As a CI maintainer, I want `Directory.Build.props` `TreatWarningsAsErrors` to keep passing across `ECommerce.Shared`, Order, Payment, and `ECommerce.Shared.Tests`, so that the deeper interface introduces no new `NoWarn` exemptions.
12. As a developer running either supported broker provider, I want the deepened seam to remain transport-neutral, so that `Messaging:Provider` switching keeps working unchanged.

## Implementation Decisions

### Modules

**Modified — `ECommerce.Shared` Outbox seam (`shared-libs/ECommerce.Shared/Infrastructure/Outbox`)**
- `IOutboxUnitOfWork` keeps the existing `ExecuteAsync(IExecutionStrategy, Func<Task<IReadOnlyList<Event>>>)` overload for non-aggregate callers.
- `IOutboxUnitOfWork` gains a new overload for aggregate-bearing callers that takes (1) a `DbContext` whose `Database.CreateExecutionStrategy()` is used, (2) a `Func<Task>` unit of work that mutates aggregates tracked by the context, and (3) a translator from the caller's domain-event base interface to `Event`. The implementation owns the `TransactionScope`, calls `unitOfWork()`, dequeues domain events from `ChangeTracker.Entries<TEntity>()`, calls `SaveChangesAsync(acceptAllChangesOnSuccess: false)`, enqueues translated events via `IOutboxStore.AddOutboxEvent`, calls `ChangeTracker.AcceptAllChanges()`, then `scope.Complete()`.
- The entity base type (today `Order.Service.Models.Entity` and `Payment.Service.Models.Entity`) is a generic parameter so each caller's existing base survives. If those two `Entity` types are byte-identical and a shared base in `ECommerce.Shared` is justified, that promotion is a separate decision and is **out of scope** here.
- OTEL span and outcome metric are emitted by both overloads through a shared helper so observability is uniform.

**Modified — `Order.Service` `OrderContext`**
- `OrderContext.ExecuteAsync` becomes: `return _outboxUoW.ExecuteAsync<Models.Entity>(this, unitOfWork, Translate);` — no more local `CreateExecutionStrategy`, no more `TransactionScope`, no more manual `SaveChangesAsync` / `AcceptAllChanges`. The translation table (`OrderCreatedDomainEvent → OrderCreatedEvent`, etc.) stays in `OrderContext`.
- The `OrderContext` design-time constructor that omits `IOutboxStore`/`IOutboxUnitOfWork` is preserved (EF Core tooling needs it). Calling `ExecuteAsync` without the runtime constructor still throws as today.

**Modified — `Payment.Service` `PaymentContext`**
- `PaymentContext.ExecuteAsync` is rewritten the same way against the deep overload. Payment's translation table for `PaymentAuthorizedDomainEvent`, `PaymentCapturedDomainEvent`, `PaymentFailedDomainEvent`, `PaymentRefundedDomainEvent` stays in `PaymentContext`.

**Unchanged — Inventory, Shipping**
- No changes in this PRD. They continue to call `IOutboxStore` directly today and migrate to the shallow `IOutboxUnitOfWork` overload in the companion Payment PRD (Phases 4–6 there).

### Architectural decisions

- **No removal of the shallow overload.** Inventory and Shipping (no aggregate yet) need it. Removal would force adoption of a domain-event base they do not have.
- **Translator is per-context.** Domain-event-to-Integration-event mapping stays in each service's context. Shared library has no knowledge of Order, Payment, or any future service's event types.
- **No new persistent schema.** Outbox tables (ADR-0002) unchanged.
- **Provider-neutral.** Outbox rows remain provider-neutral; `OutboxBackgroundService` publishes via `IEventBus`. `Messaging:Provider` (RabbitMQ default, Azure Service Bus when selected) is unaffected.
- **`ECommerce.Shared` version is bumped from `2.15.0` to `2.16.0`** when the deep overload ships. Per ADR-0005, consumers (Order, Payment) upgrade `<PackageReference>` explicitly and a fresh `.nupkg` is pushed to `local-nuget-packages/`. Inventory and Shipping do not need the upgrade until the companion Payment PRD migrates them.
- **DLQ behaviour is unchanged in scope here.** Subscriber-pipeline exceptions inside handlers using the shallow overload bubble out exactly as they do today; the deep overload is invoked from contexts called by HTTP endpoints, where exceptions return errors to the caller rather than dead-letter. The interaction between handler exceptions and the recently-landed provider-agnostic DLQ capture is owned by the companion Payment PRD's DLQ contract section.

### API contracts

- No public HTTP route changes.
- Order, Payment Integration Event payloads unchanged.

### Schema changes

- None.

## Testing Decisions

`Given_When_Then` naming.

### Modules to test

**`OutboxUnitOfWork` deep overload (`ECommerce.Shared.Tests`)**
- New: with a representative `DbContext` containing an entity that raises domain events, assert atomic commit (state + translated events together), atomic rollback when the unit of work throws, atomic rollback when `SaveChangesAsync` throws, `AcceptAllChanges` is called only on the success path, and translated events match the translator output.
- Prior art: existing `OutboxUnitOfWorkTests` in `shared-libs/ECommerce.Shared.Tests/`.

**`OutboxUnitOfWork` shallow overload (`ECommerce.Shared.Tests`)**
- Regression: existing tests must keep passing unchanged.

**`OrderContext.ExecuteAsync` (Order integration tests)**
- Regression: existing Order context tests in `order-microservice/Order.Tests/` must keep passing without modification. Any failure indicates the deep overload diverges from the hand-rolled pattern.

**`PaymentContext.ExecuteAsync` (Payment integration tests)**
- Regression: existing Payment context tests in `payment-microservice/Payment.Tests/` must keep passing without modification.

### What we are NOT testing

- We are not retesting EF execution strategy or `TransactionScope`.
- We are not adding broker-delivery tests; that path is identical to today.
- We are not testing the Inventory/Shipping shallow-overload adoption (that lives in the companion Payment PRD).

## Out of Scope

- **Migrating Inventory or Shipping to either overload.** Owned by the companion Payment PRD (Phases 4–6 after this seam ships).
- **Promoting `Entity` / `IDomainEvent` from Order/Payment into `ECommerce.Shared`.** Separate decision; the generic parameter on the deep overload avoids forcing it now.
- **DLQ / subscriber-pipeline exception contract.** Owned by the companion Payment PRD.
- **OTEL span attribute schema** beyond "uniform across overloads" — exact attribute names are an instrumentation refinement decided during implementation.

## Further Notes

- Sequencing: this PRD must ship before the companion Payment PRD's Phase 4 starts. Phase 4 of that PRD migrates Inventory and Shipping using the **shallow** overload, which is unchanged here; but Phase 5 (observability) of that PRD piggybacks on the uniform OTEL helper introduced here.
- Pre-commit only runs Basket tests. After the package version bump, Order, Payment, and `ECommerce.Shared.Tests` must be run manually before pushing.
- `Directory.Build.props` enforces `TreatWarningsAsErrors`; no new `NoWarn` is acceptable.
