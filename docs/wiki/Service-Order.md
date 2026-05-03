# Order Service

Order lifecycle service. Persists orders to SQL Server, emits domain events via the Outbox, and participates in the Order↔Inventory saga.

| | |
|---|---|
| **Port** | 8001 |
| **Datastore** | SQL Server (database: `Order`) |
| **Source** | [`order-microservice/Order.Service/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/order-microservice/Order.Service) |
| **Tests** | [`order-microservice/Order.Tests/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/order-microservice/Order.Tests) |
| **Publishes** | `OrderCreatedEvent`, `OrderConfirmedEvent`, `OrderCancelledEvent` |
| **Subscribes** | `StockReservationFailedEvent`, `PaymentAuthorizedEvent`, `PaymentFailedEvent`, `ProductCreatedEvent` |

## Responsibilities

- Accept an order, persist it with its line items, and emit `OrderCreatedEvent` transactionally via the Outbox.
- React to Inventory's reservation outcome and transition the order to `Confirmed` or `Cancelled`.
- Expose read access to the order.
- Maintain a Redis-backed price cache used at order submission, primed by `ProductCreatedEvent` and refilled on miss via the Product service HTTP client.

## HTTP endpoints

All endpoints require a valid JWT.

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/order/{customerId}` | Create a new order |
| `GET` | `/order/{customerId}/{orderId}` | Get a specific order |

Implementation: `Endpoints/OrderApiEndpoint.cs`.

## Saga participation

See the sequence diagram in [Architecture](Architecture#saga-order--inventory--payment--shipping). Summary:

- On `POST`, Order writes order rows + outbox row in one transaction. `OrderCreatedEvent` carries `UnitPrice` per item and `Currency` so Payment can authorize without an extra round trip.
- Outbox background service publishes `OrderCreatedEvent`.
- `PaymentAuthorizedEventHandler` → order becomes `Confirmed`, emits `OrderConfirmedEvent`. (The pre-Payment behaviour where `StockReservedEvent` confirmed directly has been removed.)
- `PaymentFailedEventHandler` → order becomes `Cancelled`, emits `OrderCancelledEvent`. Inventory's existing handler releases the reservation from the same signal.
- `StockReservationFailedEventHandler` → order becomes `Cancelled`, emits `OrderCancelledEvent`.

## Persistence pattern: unit-of-work + domain events

The endpoints and event handlers do not own transactions or call the outbox directly. Instead:

- Aggregate methods on `Order` (`Submit`, `TryConfirm`, `TryCancel`) mutate state and `Raise` domain events (`OrderCreatedDomainEvent`, `OrderConfirmedDomainEvent`, `OrderCancelledDomainEvent`).
- `IOrderStore.ExecuteAsync(unitOfWork)` wraps the work in EF Core's execution strategy and a `TransactionScope`, calls `SaveChangesAsync`, then translates raised domain events into integration events (`OrderCreatedEvent`, `OrderConfirmedEvent`, `OrderCancelledEvent`) and writes them to the Outbox in the same transaction.
- The Outbox background service publishes the integration events to RabbitMQ asynchronously.

This is the pattern to follow when adding new order behaviour: change state on the aggregate, raise a domain event, and let `ExecuteAsync` handle persistence + publication.

## Price resolution

`OrderCreatedEvent` requires a unit price per line item. Order resolves prices through `IProductPriceProvider`:

- `RedisProductPriceProvider` reads from a distributed cache keyed by product id.
- `ProductCreatedEventHandler` writes new product prices into the cache as they are created upstream.
- On a cache miss, `HttpProductCatalogClient` fetches the price from the [Product](Service-Product) service and back-fills the cache.

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
├── Models/                    # Order aggregate + domain events (OrderCreated/Confirmed/Cancelled)
├── Infrastructure/
│   ├── Data/                  # EF Core DbContext, IOrderStore (unit-of-work + outbox dispatch)
│   └── Providers/             # IProductCatalogClient, RedisProductPriceProvider
├── IntegrationEvents/         # published + subscribed (incl. ProductCreatedEventHandler)
└── Migrations/
```
