# Order Service

Order lifecycle service. Persists orders to SQL Server, emits domain events via the Outbox, and participates in the Order↔Inventory saga.

| | |
|---|---|
| **Port** | 8001 |
| **Datastore** | SQL Server (database: `Order`) |
| **Source** | [`order-microservice/Order.Service/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/order-microservice/Order.Service) |
| **Tests** | [`order-microservice/Order.Tests/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/order-microservice/Order.Tests) |
| **Publishes** | `OrderCreatedEvent`, `OrderConfirmedEvent`, `OrderCancelledEvent` |
| **Subscribes** | `StockReservedEvent`, `StockReservationFailedEvent` |

## Responsibilities

- Accept an order, persist it with its line items, and emit `OrderCreatedEvent` transactionally via the Outbox.
- React to Inventory's reservation outcome and transition the order to `Confirmed` or `Cancelled`.
- Expose read access to the order.

## HTTP endpoints

All endpoints require a valid JWT.

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/order/{customerId}` | Create a new order |
| `GET` | `/order/{customerId}/{orderId}` | Get a specific order |

Implementation: `Endpoints/OrderApiEndpoint.cs`.

## Saga participation

See the sequence diagram in [Architecture](Architecture#saga-order--inventory). Summary:

- On `POST`, Order writes order rows + outbox row in one transaction.
- Outbox background service publishes `OrderCreatedEvent`.
- `StockReservedEventHandler` → order becomes `Confirmed`, emits `OrderConfirmedEvent`.
- `StockReservationFailedEventHandler` → order becomes `Cancelled`, emits `OrderCancelledEvent`.

## Migrations

- `20260414084245_Initial`
- `20260420120000_AddOrderStatus`

Located under `Order.Service/Migrations/`. Run via `dotnet ef database update` from the service folder, or apply on startup in dev.

## Structure

```
Order.Service/
├── Program.cs
├── Endpoints/OrderApiEndpoint.cs
├── ApiModels/
├── Models/                 # Order, OrderItem, OrderStatus
├── Infrastructure/Data/    # EF Core DbContext
├── IntegrationEvents/      # published + subscribed
└── Migrations/
```
