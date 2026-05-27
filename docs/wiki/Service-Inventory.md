# Inventory Service

Stock ledger. Tracks stock levels, reservations, movements, and backorders, and is the single largest event participant in the system.

| | |
|---|---|
| **Port** | 8005 |
| **Datastore** | SQL Server (database: `Inventory`) |
| **Source** | [`inventory-microservice/Inventory.Service/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/inventory-microservice/Inventory.Service) |
| **Tests** | [`inventory-microservice/Inventory.Tests/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/inventory-microservice/Inventory.Tests) |
| **Publishes** | `StockReservedEvent`, `StockReservationFailedEvent`, `StockCommittedEvent`, `StockReleasedEvent`, `StockAdjustedEvent`, `StockDepletedEvent`, `LowStockEvent` |
| **Subscribes** | `ReserveStockCommand`, `CommitStockCommand`, `ReleaseStockCommand` (from Saga), `ProductCreatedEvent` |
| **Layout** | Clean Architecture + Vertical Slices default ([ADR-0012](../adr/0012-clean-arch-vsa-default-service-shape.md)); Inventory keeps command handlers inline with their slices. |

## Responsibilities

- Maintain stock rows keyed by `ProductId` (created on `ProductCreatedEvent`).
- Execute saga commands from the [Saga service](Service-Saga): reserve on `ReserveStockCommand`, commit on `CommitStockCommand`, release on `ReleaseStockCommand`. Publish the matching reply event (`StockReservedEvent` / `StockReservationFailedEvent` / `StockCommittedEvent` / `StockReleasedEvent`) for each.
- Record every stock change as a stock movement.
- Accept backorder requests when stock is insufficient.
- Emit low-stock and depleted signals for ops/alerting.

## HTTP endpoints

| Method | Route | Auth | Purpose |
|---|---|---|---|
| `GET` | `/inventory` | Bearer + `Administrator` | List all stock items |
| `GET` | `/inventory/{productId}` | public | Get one stock item |
| `GET` | `/inventory/{productId}/movements` | Bearer + `Administrator` | Movement history |
| `POST` | `/inventory/{productId}/restock` | Bearer + `Administrator` | Add stock |

Implementations live under `Features/`, including `ListStockItems/`, `GetStockMovements/`, `Restock/`, `ReserveByHttp/`, `CreateBackorder/`, `ReserveStock/`, `CommitStock/`, `ReleaseStock/`, and `ProductCreated/`.

## Migrations

- `20260419120000_InitialCreate`
- `20260419130000_AddStockMovements`
- `20260420130000_AddStockReservations`
- `20260420140000_Phase5Sync`
- `20260421120000_AddBackorderRequests`

## Domain model — the `StockItem` aggregate

The reservation lifecycle is owned by a rich `StockItem` aggregate, not by the persistence layer. `StockLevel` and `StockReservation` are members of the aggregate; `StockMovement` rows are recorded by orchestration from what the aggregate returns.

- **`StockItem.Hold`** — reserves stock for an order. Enforces the availability invariant (a hold can never exceed `Available = TotalOnHand - TotalReserved`) and short-circuits idempotently when the order already holds stock. `TotalReserved` and the per-warehouse `StockLevel.Reserved` are mutated together so they cannot drift.
- **`StockItem.Commit`** — converts an order's `Held` reservations into consumed stock; decrements the reserved and on-hand counters together. Skips non-`Held` reservations, so a double-commit is an idempotent no-op.
- **`StockItem.Release`** — returns an order's reservations: a `Held` release only frees reserved units; a `Committed` release also restores on-hand stock. Mixed held/committed orders are handled in one call; already-released reservations are skipped (double-release idempotent).
- **`StockReservation` guarded transitions** — `Status` is `init`-only. Once a reservation exists its state changes only through guarded `Commit()` / `Release()` methods (invoked by the aggregate) that throw on illegal source states. Illegal lifecycle transitions are unrepresentable through the public API.

Each method returns a `*ItemResult` value type (`HoldResult`, `CommitItemResult`, `ReleaseItemResult`) carrying the outcome plus the movements to persist. `InventoryContext`'s `Reserve` / `CommitReservations` / `ReleaseReservations` are thin orchestration: load aggregate, delegate, persist movements, save — no inline state-machine logic. The `IInventoryStore` seam, all `*Result` records, `AlreadyProcessed` semantics, the EF schema, and the published events are unchanged by this refactor.

See [`docs/prd/PRD-StockItem-Aggregate.md`](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/prd/PRD-StockItem-Aggregate.md) and [`docs/plans/stockitem-aggregate.md`](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/plans/stockitem-aggregate.md) ([#55](https://github.com/daonhan/Microservices-in-.NET/issues/55)).

## Saga participation

Inventory is a saga participant driven by the [Saga service](Service-Saga). The aggregate methods `Hold` / `Commit` / `Release` are invoked from `ReserveStockCommandHandler`, `CommitStockCommandHandler`, and `ReleaseStockCommandHandler`; each handler publishes the matching reply event back to Saga in the same outbox envelope. See the canonical sequence in [Diagram-Saga](Diagram-Saga).

## Structure

```
Inventory.Service/
├── Program.cs
├── Features/               # HTTP, command, and event slices
├── Domain/                 # StockItem aggregate (Hold/Commit/Release), StockLevel,
│                           # StockReservation (guarded), StockMovement, BackorderRequest,
│                           # Hold/Commit/Release ItemResult value types
├── Contracts/Integration/  # published + subscribed event contracts
├── Infrastructure/         # EF Core data and internal outbox endpoints
└── Migrations/
```

## Related PRD and plan

- [`docs/prd/PRD-Inventory.md`](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/prd/PRD-Inventory.md)
- [`docs/plans/inventory.md`](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/plans/inventory.md)
