# Integration Events Catalog

All cross-service communication happens through events published to a single RabbitMQ fanout exchange (`ecommerce-exchange`). Each subscribing service binds its own queue, so every subscriber receives every event and can filter by type.

## Event ⇄ service matrix

| Event | Publisher | Subscribers |
|---|---|---|
| `ProductCreatedEvent` | [Product](Service-Product) | [Inventory](Service-Inventory) |
| `ProductPriceUpdatedEvent` | Product | [Basket](Service-Basket) |
| `OrderCreatedEvent` | [Order](Service-Order) | [Basket](Service-Basket), [Inventory](Service-Inventory) |
| `OrderConfirmedEvent` | Order | Inventory |
| `OrderCancelledEvent` | Order | Inventory, [Shipping](Service-Shipping) |
| `StockReservedEvent` | Inventory | Order |
| `StockReservationFailedEvent` | Inventory | Order |
| `StockCommittedEvent` | Inventory | [Shipping](Service-Shipping) |
| `StockReleasedEvent` | Inventory | — (ops/audit) |
| `StockAdjustedEvent` | Inventory | — (ops/audit) |
| `StockDepletedEvent` | Inventory | — (ops/audit) |
| `LowStockEvent` | Inventory | — (ops/audit) |
| `ShipmentCreatedEvent` | [Shipping](Service-Shipping) | — (ops/audit) |
| `ShipmentDispatchedEvent` | Shipping | — (ops/audit) |
| `ShipmentDeliveredEvent` | Shipping | — (ops/audit) |
| `ShipmentCancelledEvent` | Shipping | — (ops/audit) |
| `ShipmentFailedEvent` | Shipping | — (ops/audit) |
| `ShipmentReturnedEvent` | Shipping | — (ops/audit) |
| `ShipmentStatusChangedEvent` | Shipping | — (ops/audit) |


## Saga and fulfillment sequence

```mermaid
sequenceDiagram
    participant Order
    participant Bus as RabbitMQ
    participant Inventory
    participant Shipping

    Order-->>Bus: OrderCreatedEvent
    Bus-->>Inventory: OrderCreatedEvent
    alt stock available
        Inventory-->>Bus: StockReservedEvent
        Bus-->>Order: StockReservedEvent
        Order-->>Bus: OrderConfirmedEvent
        Bus-->>Inventory: OrderConfirmedEvent
        Inventory-->>Bus: StockCommittedEvent
        Bus-->>Shipping: StockCommittedEvent
        Shipping-->>Bus: ShipmentCreatedEvent
        ... Shipping transitions ...
        Shipping-->>Bus: ShipmentDispatchedEvent
        Shipping-->>Bus: ShipmentDeliveredEvent
    else insufficient stock
        Inventory-->>Bus: StockReservationFailedEvent
        Bus-->>Order: StockReservationFailedEvent
        Order-->>Bus: OrderCancelledEvent
        Bus-->>Inventory: OrderCancelledEvent
        Inventory-->>Bus: StockReleasedEvent
        Bus-->>Shipping: OrderCancelledEvent
        Shipping-->>Bus: ShipmentCancelledEvent
    end
```

## Payload conventions

All events derive from the shared `Event` base class (see [Shared-Library](Shared-Library)) which carries:

- `Id` — a unique identifier used for idempotency on the subscriber side
- `OccurredAt` — UTC timestamp


Concrete payloads (product id, order id, quantities, prices, shipment id, carrier info, etc.) live alongside the event type in each service's `IntegrationEvents/` folder — that folder is the authoritative schema. Link targets per event:

- Product events: [`product-microservice/Product.Service/IntegrationEvents/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/product-microservice/Product.Service/IntegrationEvents)
- Order events: [`order-microservice/Order.Service/IntegrationEvents/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/order-microservice/Order.Service/IntegrationEvents)
- Inventory events: [`inventory-microservice/Inventory.Service/IntegrationEvents/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/inventory-microservice/Inventory.Service/IntegrationEvents)
- Shipping events: [`shipping-microservice/Shipping.Service/IntegrationEvents/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/shipping-microservice/Shipping.Service/IntegrationEvents)

## Delivery semantics

- **At-least-once publish** via the [Transactional Outbox](Shared-Library#transactional-outbox--why-it-matters).
- **Idempotent handlers** — subscribers use `Event.Id` (or the business key) to deduplicate.
- **Span context propagation** — traces carry across the bus via the shared observability layer, so a single Jaeger trace spans the full saga.
