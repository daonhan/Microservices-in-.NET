# PRD: StockItem aggregate owns the stock-reservation lifecycle

> Source: GitHub issue [#55](https://github.com/daonhan/Microservices-in-.NET/issues/55)

## Problem Statement

As a developer working on the Inventory service, I find that the rules for what can happen to stock — holding it for an order, committing the hold when an order is confirmed, releasing it when an order is cancelled — are not in any one place I can read, test, or reason about. They live as ~200 lines of orchestration spread across three methods on `InventoryContext` (the EF Core `DbContext`). When I want to know "can a `Committed` reservation be released?" or "what happens if `Reserve` is called twice for the same order?", I have to read the persistence layer to find out. When I want to add a test, I have to spin up an in-memory database. When the Order service exhibits a similar concept, the answer lives on a rich domain model (`Order.TryConfirm()`, `Order.TryCancel()`); on the Inventory side, the equivalent invariants are scattered across LINQ queries and ad-hoc status checks inside the DbContext.

The asymmetry slows me down: navigating Inventory takes longer than navigating Order, even though they encode the same kind of state machine. The lack of a domain-level test surface means current coverage relies entirely on API-level integration tests with a real database — fast feedback on invariant changes is not available.

## Solution

Pull the stock-reservation lifecycle out of the persistence layer and into the domain. The `StockItem` aggregate becomes the owner of its reservation rules: holding stock against an order, committing held stock, releasing reservations from either the held or committed state. `StockReservation` becomes a member of the `StockItem` aggregate with guarded state transitions instead of a public `Status` setter. `StockLevel` (the per-warehouse breakdown of on-hand and reserved counts) is also pulled inside the `StockItem` aggregate boundary, since the invariant "the sum of `StockLevel.Reserved` across warehouses equals `StockItem.TotalReserved`" is currently only enforced by the persistence orchestration code mutating both at once.

`InventoryContext`'s `Reserve`, `CommitReservations`, and `ReleaseReservations` methods stay as the public-facing persistence orchestration but become thin: load the aggregate (item + levels + reservations), call the appropriate aggregate method, persist any returned movements, save changes. The shape and behavior of the existing `IInventoryStore` interface — the contract endpoints depend on, including the `*Result` records and their `AlreadyProcessed` semantics — is preserved. No changes to API contracts. No changes to the EF schema. No changes to the events the Inventory service publishes. From the outside, this refactor is invisible.

The win is a domain-level seam where the rules of stock reservation can be read, tested, and changed in one place, and symmetry with how the Order service's `Order` aggregate already works.

## User Stories

1. As a developer adding a new rule about reservations (e.g. partial reservations, reservation expiry), I want the rule to live on the `StockItem` aggregate, so that I can read and change it without navigating the persistence layer.
2. As a developer fixing a bug in stock release behavior, I want the release logic to be in one method on the aggregate, so that I do not have to scan a `DbContext` method that mixes EF queries with state mutation.
3. As a developer writing a test for "you cannot commit a reservation that was never held", I want to write a unit test against `StockItem` directly, so that I do not need an in-memory database for invariant tests.
4. As a developer onboarding to the Inventory service, I want the lifecycle of a reservation to be readable in one file, so that I can understand the service in less time.
5. As a developer who already knows the Order service, I want Inventory's domain to mirror Order's "rich aggregate with guarded transitions" pattern, so that the two services feel symmetric.
6. As a developer adding a new reservation status in the future, I want illegal status transitions to be impossible to express by construction, so that I cannot accidentally introduce a regression by mutating `Status` directly.
7. As a developer reading `InventoryContext.Reserve`, I want the method to be short and obviously persistence-shaped, so that I can quickly see what hits the database without parsing domain logic.
8. As a developer changing the relationship between `StockItem.TotalReserved` and `StockLevel.Reserved`, I want both numbers to be mutated by the same piece of code, so that they cannot drift apart.
9. As a developer extending the API, I want the existing `ReserveResult`, `CommitResult`, and `ReleaseResult` shapes to remain unchanged, so that the endpoints, integration tests, and saga handlers in the Order service are untouched.
10. As an operator running the existing Inventory service, I want the database schema to be unchanged, so that no migration is required to deploy this refactor.
11. As a developer writing a regression test for reserve→commit, I want an end-to-end happy-path test (currently missing) so that future refactors of the persistence orchestration are caught if they break the flow.
12. As a developer triaging a production incident where a reservation was committed twice, I want the duplicate-commit guard to live on the aggregate with a unit test, so that I can verify the guard exists without reading EF code.
13. As a developer triaging a production incident where releasing a committed reservation did not restore on-hand stock, I want the asymmetric release-from-held vs release-from-committed logic to live in one obvious place on the aggregate, so that I can understand and verify the rule in one read.
14. As a developer running the test suite locally, I want aggregate unit tests that run in milliseconds, so that I can iterate on domain rules without paying for database setup.
15. As a developer of the Order service's saga handlers (`OrderConfirmedEventHandler`, `OrderCancelledEventHandler`), I want the `IInventoryStore` contract and its idempotency semantics to be unchanged, so that the cross-service saga continues to work without modification.
16. As a developer of the API endpoints in `InventoryApiEndpoints`, I want endpoint code to remain unchanged, so that this refactor does not couple to a separate API change.
17. As a developer following the existing convention that `Restock` and backorder fulfillment are separate concerns from the reservation lifecycle, I want `Restock` left alone, so that scope stays contained.

## Implementation Decisions

- **Aggregate root**: `StockItem`. The `StockItem` aggregate owns its `StockLevel` collection and its `StockReservation` collection (Option A from the design discussion). The aggregate boundary matches the invariants — paired mutation of `TotalReserved`/`StockLevel.Reserved` and paired mutation of `TotalOnHand`/`StockLevel.OnHand` are now enforced inside the aggregate rather than by `InventoryContext` orchestration.
- **`StockItem` gains behavior**: methods that hold stock against an order, commit a held reservation, and release reservations (handling both the held and committed cases). These methods enforce invariants such as "cannot hold more than is available", "cannot commit what is not held", "double-commit and double-release are no-ops", and the asymmetric mutation when releasing a `Committed` reservation versus a `Held` one. Each method returns the `StockMovement` records the persistence layer should record, so that movement creation does not happen inside the aggregate's clock-dependency-free core.
- **`StockReservation` becomes a member of the aggregate**: its `Status` setter is no longer public. Status transitions happen via guarded methods invoked by `StockItem`. Illegal transitions are no-ops or throw — the exact policy is decided during implementation but must be consistent with the existing `AlreadyProcessed` semantics in the `*Result` records.
- **`StockLevel` becomes a member of the aggregate**: its `Reserved` and `OnHand` fields are mutated only through aggregate methods, not externally.
- **`InventoryContext.Reserve`, `CommitReservations`, `ReleaseReservations` become thin orchestration**: load the aggregate, call the aggregate method, record returned movements, save. Each method shrinks from ~70 lines to a small persistence orchestrator. They continue to return the existing `*Result` records with the existing `AlreadyProcessed` semantics — that contract is part of the seam between Inventory and the Order saga and is preserved unchanged.
- **`IInventoryStore` interface is unchanged**. So are all the `*Result` and `*Line` records. So is the EF `DbSet` shape and schema. This is an internal refactor.
- **Time is injected into aggregate methods explicitly**, not read via `DateTime.UtcNow` inside the aggregate, so that aggregate tests can pin time deterministically. The orchestration layer continues to call `DateTime.UtcNow` and pass it down.
- **`Restock`, `SetThreshold`, `CreateBackorder`, `ProvisionStockItem`, and the various read methods are out of scope**. They do not participate in the reservation lifecycle.
- **Aggregate loading**: `InventoryContext` will load `StockItem` together with the `StockLevel` rows and the relevant `StockReservation` rows for an order in one place. The existing dictionary-based loading inside `Reserve`/`CommitReservations`/`ReleaseReservations` is replaced by an aggregate-shaped fetch.
- **No domain events introduced in this PRD**. The Inventory service's outbox-based integration events (`StockReservedEvent`, `StockReservationFailedEvent`) continue to be published by the orchestration layer in the same place they are today.
- **Keyword `internal` discipline**: aggregate methods stay `internal` to the Inventory service assembly, matching the existing convention. Tests reference internals via `InternalsVisibleTo`, matching the convention used elsewhere in the repo.

## Testing Decisions

A good test here exercises external behavior — what an aggregate does in response to a method call — and not the internal mechanism. For the aggregate, "external behavior" means: the new state of the aggregate, the movements the method returns, and whether an illegal call is rejected. Tests should not look at private fields or assume a specific internal data structure. For the orchestration tests, "external behavior" means the existing `*Result` payload returned to the API and the persisted state visible after `SaveChangesAsync`.

Modules to test:

- **`StockItem` aggregate** — new unit tests covering: hold within available, hold beyond available is rejected, commit a held reservation, commit when nothing is held is a no-op or rejected per the chosen policy, release a held reservation, release a committed reservation (with the asymmetric on-hand restoration), double-commit is idempotent, double-release is idempotent, mixed-state release where some reservations are held and some are committed.
- **`StockReservation`** — small unit tests covering illegal status transitions.
- **End-to-end reserve→commit happy path** — a new integration test through the existing Inventory API surface. Today the API tests cover reserve, reserve-with-insufficient-stock, and release; there is no test covering the full reserve-then-commit flow. This test closes that gap and acts as the regression net for the refactor.

Prior art:

- `Order.Tests/Domain/OrderTests.cs` — the rich-aggregate unit-test pattern that this refactor brings to Inventory. The new `StockItem` tests should look like these.
- `Inventory.Tests/Api/ReserveApiTests.cs` and `Inventory.Tests/Api/ReleaseReservationsTests.cs` — the existing API-level regression net. These continue to pass unchanged, by design. The new reserve→commit end-to-end test follows their shape.

Tests we are deliberately NOT writing:

- Tests for `InventoryContext.Reserve`/`CommitReservations`/`ReleaseReservations` themselves. These methods become thin orchestrators; the existing API integration tests already cover them end-to-end. Adding orchestration-level tests would test the implementation, not the behavior.
- Tests pinning specific persistence query shapes. The existing API tests are the contract.

## Out of Scope

- The Order provisioning saga refactor (the explicit `OrderProvisioningSaga` module, persistence of saga state, timeout monitor for stuck reservations). That is a follow-up PRD that will be combined with this one in a later integration step.
- Restock and backorder fulfillment logic — they have their own concerns and do not participate in the reservation lifecycle.
- Any change to the Inventory API surface, the events Inventory publishes, the `IInventoryStore` interface, or the EF schema.
- Any change to the Order service. The `IInventoryStore` contract and the `*Result` payloads it returns are preserved precisely so that the Order saga handlers stay untouched.
- Domain events on the Inventory aggregate. Today the Inventory service does not raise domain events from `StockItem`; this refactor does not introduce that pattern.
- Cross-service contract sharing of integration events (a separate candidate, not chosen here).

## Further Notes

The refactor is informed by the architectural depth-vs-shallowness review of the Inventory service. The current `InventoryContext` methods for the reservation lifecycle are the canonical example in this repo of a shallow persistence module hiding a deep state machine — the deletion test concentrates the complexity onto `StockItem`, where it belongs, rather than scattering it across N callers.

After this PRD is delivered, the natural follow-up is the Order provisioning saga (originally candidate #1 from the architecture review). The two refactors are designed to compose: with `StockItem` owning its lifecycle here, the saga will be free to reason about the Order side without reaching into Inventory's persistence shape.

There is no conflict with any existing ADR (the repository currently has no `docs/adr/` directory). If a future ADR formalizes "rich aggregates over anemic models" as a project-wide rule, this refactor is consistent with it; if a future ADR pushes the opposite direction, this refactor would be the kind of change it would govern, and that decision can be recorded then.
