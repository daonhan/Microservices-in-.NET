# Plan: StockItem aggregate owns the stock-reservation lifecycle

> Source PRD: [PRD-StockItem-Aggregate.md](../prd/PRD-StockItem-Aggregate.md) — GitHub issue [#55](https://github.com/daonhan/Microservices-in-.NET/issues/55)

## Architectural decisions

Durable decisions that apply across all phases:

- **Aggregate root**: `StockItem`. `StockLevel` and `StockReservation` are members of the aggregate. `StockMovement` is recorded by orchestration, not by the aggregate.
- **Public seam (unchanged)**: `IInventoryStore` interface, all `*Result` and `*Line` records, the `AlreadyProcessed` semantics, and the events the Inventory service publishes via the outbox.
- **API surface (unchanged)**: `InventoryApiEndpoints` routes, request/response shapes.
- **EF schema (unchanged)**: no migrations; `DbSet` shapes preserved. Aggregate loading reshapes the queries inside `InventoryContext`, not the schema.
- **Cross-service contract (unchanged)**: the Order service's saga handlers are untouched. Inventory continues to publish `StockReservedEvent` / `StockReservationFailedEvent` from the orchestration layer in the same place as today.
- **Time injection**: aggregate methods take a timestamp as a parameter. Orchestration calls `DateTime.UtcNow` and passes it down. The aggregate has no clock dependency.
- **Visibility**: aggregate methods are `internal`, matching the existing convention. Tests use `InternalsVisibleTo`.
- **Out of scope across all phases**: `Restock`, `SetThreshold`, `CreateBackorder`, `ProvisionStockItem`, the Order provisioning saga, domain events on the aggregate, cross-service contract sharing.

---

## Phase 1: `StockItem` aggregate owns Hold (Reserve path)

**User stories**: 1, 2, 3, 4, 5, 7, 8, 9, 10, 14, 16, 17

### What to build

The Reserve path moves end-to-end onto a rich `StockItem` aggregate. Orchestration loads the `StockItem` together with the relevant `StockLevel` rows and any existing `StockReservation` rows for the order in question, then calls a single Hold method on the aggregate. The aggregate enforces the invariants (cannot hold more than `Available`; existing reservations for the same order short-circuit with the existing `AlreadyProcessed` semantics) and produces the new `StockReservation` plus the `StockMovement` records the orchestration layer should persist. Orchestration persists movements and saves changes, then returns the existing `ReserveResult` shape unchanged.

`Commit` and `Release` are untouched in this phase. The Reserve API path and its existing tests (`ReserveApiTests`) continue to pass without modification — that is the regression net for this slice.

### Acceptance criteria

- [ ] `StockItem` exposes a Hold method that takes an order identifier, the lines to reserve, and a timestamp, and returns the data the orchestration layer needs to satisfy the existing `ReserveResult` contract (including the `AlreadyProcessed` short-circuit).
- [ ] `StockItem` enforces the "cannot hold more than `Available`" invariant inside the aggregate, not in the persistence layer.
- [ ] `StockItem.TotalReserved` and the per-warehouse `StockLevel.Reserved` are mutated together inside the aggregate; orchestration cannot mutate either independently.
- [ ] `InventoryContext.Reserve` shrinks to thin orchestration: load aggregate, call Hold, persist returned movements, save.
- [ ] `IInventoryStore`, `ReserveResult`, `ReservedLine`, `FailedReserveLine`, the EF schema, and the API surface are unchanged.
- [ ] New unit tests cover the Hold method directly without spinning up a database (hold within available, hold beyond available rejected, duplicate-order short-circuit).
- [ ] All existing `Inventory.Tests/Api/ReserveApiTests` continue to pass unchanged.
- [ ] All existing `Inventory.Tests/Api/ReleaseReservationsTests` continue to pass unchanged (Commit/Release paths are still routed through the legacy code in this phase).
- [ ] No EF migration is generated.

---

## Phase 2: `StockItem` aggregate owns Commit

**User stories**: 1, 2, 3, 6, 7, 8, 9, 11, 12, 14, 15

### What to build

The Commit path moves onto the aggregate. Orchestration loads the `StockItem` together with its held reservations for the order, calls a Commit method on the aggregate, and persists the returned movements. The aggregate enforces "double-commit is idempotent" and "cannot commit a reservation that is not held" inside the aggregate, replacing the current ad-hoc status checks in `InventoryContext.CommitReservations`. The orchestration layer continues to return the existing `CommitResult` shape with its `AlreadyProcessed` semantics intact.

This phase also delivers the previously missing reserve→commit end-to-end test, exercising the full happy path through the Inventory API and acting as the regression net for the orchestration changes.

`Release` is untouched in this phase.

### Acceptance criteria

- [ ] `StockItem` exposes a Commit method that takes an order identifier and a timestamp, and returns the data needed to satisfy the existing `CommitResult` contract (including the `AlreadyProcessed` short-circuit when nothing is in `Held` state).
- [ ] Commit on an aggregate with no held reservations for the order is idempotent and returns the existing-result shape.
- [ ] `StockItem.TotalReserved`, `StockItem.TotalOnHand`, the per-warehouse `StockLevel.Reserved`, and `StockLevel.OnHand` are decremented together inside the aggregate.
- [ ] `InventoryContext.CommitReservations` shrinks to thin orchestration: load aggregate, call Commit, persist returned movements, save.
- [ ] `IInventoryStore.CommitReservations`, `CommitResult`, `CommittedLine`, the EF schema, and the API surface are unchanged.
- [ ] New unit tests cover the Commit method directly: commit a held reservation, double-commit idempotency, commit with no held reservations.
- [ ] A new end-to-end API test covers the full reserve→commit happy path (closes the gap identified in the PRD).
- [ ] All existing Inventory tests continue to pass unchanged.
- [ ] No EF migration is generated.

---

## Phase 3: `StockItem` aggregate owns Release; `StockReservation` transitions are guarded

**User stories**: 1, 2, 6, 7, 8, 9, 13, 14, 15

### What to build

The Release path moves onto the aggregate. The aggregate's Release method handles both branches that today live in `InventoryContext.ReleaseReservations`: releasing a `Held` reservation only decrements the reserved counters, while releasing a `Committed` reservation also restores the on-hand counters. Mixed-state releases (some held, some committed) are handled in one aggregate call. Double-release is idempotent.

This phase also closes the `StockReservation` seam: its public `Status` setter is removed, and status transitions happen only through guarded methods invoked by `StockItem`. After this phase, illegal status transitions are impossible to express through the public API of either type — the rules of the reservation lifecycle are entirely contained inside the aggregate.

### Acceptance criteria

- [ ] `StockItem` exposes a Release method that takes an order identifier and a timestamp, handles mixed held/committed states, and returns the data needed to satisfy the existing `ReleaseResult` contract.
- [ ] Releasing a `Held` reservation decrements only the reserved counters; releasing a `Committed` reservation also restores the on-hand counters. Both rules live on the aggregate, not in orchestration.
- [ ] Double-release is idempotent and returns the existing `AlreadyProcessed` result shape.
- [ ] `StockReservation`'s `Status` setter is no longer publicly assignable from outside the aggregate; transitions go through guarded methods.
- [ ] `InventoryContext.ReleaseReservations` shrinks to thin orchestration: load aggregate, call Release, persist returned movements, save.
- [ ] `IInventoryStore.ReleaseReservations`, `ReleaseResult`, `ReleasedLine`, the EF schema, and the API surface are unchanged.
- [ ] New unit tests cover the Release method directly: release-from-held, release-from-committed (with on-hand restoration), mixed-state release, double-release idempotency.
- [ ] New unit tests cover `StockReservation` illegal-transition guards.
- [ ] All existing `Inventory.Tests/Api/ReleaseReservationsTests` continue to pass unchanged.
- [ ] No EF migration is generated.
- [ ] After this phase, the three lifecycle methods on `InventoryContext` (Reserve, CommitReservations, ReleaseReservations) are each visibly thin orchestration with no inline state-machine logic.
