# Inventory Service

Stock ledger. Tracks stock levels, reservations, movements, and backorders, and is the single largest event participant in the system.

| | |
|---|---|
| **Port** | 8005 |
| **Datastore** | SQL Server (database: `Inventory`) |
| **Source** | [`inventory-microservice/Inventory.Service/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/inventory-microservice/Inventory.Service) |
| **Tests** | [`inventory-microservice/Inventory.Tests/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/inventory-microservice/Inventory.Tests) |
| **Publishes** | `StockReservedEvent`, `StockReservationFailedEvent`, `StockCommittedEvent`, `StockReleasedEvent`, `StockAdjustedEvent`, `StockDepletedEvent`, `LowStockEvent` |
| **Subscribes** | `ProductCreatedEvent`, `OrderCreatedEvent`, `OrderConfirmedEvent`, `OrderCancelledEvent` |

## Responsibilities

- Maintain stock rows keyed by `ProductId` (created on `ProductCreatedEvent`).
- Reserve stock on `OrderCreatedEvent`; commit on `OrderConfirmedEvent`; release on `OrderCancelledEvent`.
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

Implementation: `Endpoints/InventoryApiEndpoints.cs`.

## Migrations

- `20260419120000_InitialCreate`
- `20260419130000_AddStockMovements`
- `20260420130000_AddStockReservations`
- `20260420140000_Phase5Sync`
- `20260421120000_AddBackorderRequests`

## Saga participation

See [Architecture § Saga](Architecture#saga-order--inventory).

## Structure

```
Inventory.Service/
├── Program.cs
├── Endpoints/InventoryApiEndpoints.cs
├── ApiModels/
├── Models/                 # StockItem, StockMovement, StockReservation, BackorderRequest
├── Infrastructure/Data/
├── IntegrationEvents/      # published + subscribed handlers
└── Migrations/
```

## Related PRD and plan

- [`docs/prd/PRD-Inventory.md`](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/prd/PRD-Inventory.md)
- [`docs/plans/inventory.md`](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/plans/inventory.md)
