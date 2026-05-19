# Architecture

The platform decomposes an e-commerce domain into eight independently deployable business services. Each service owns its data, communicates with the outside world through the API Gateway, and with other services through asynchronous events on the provider-selected broker. RabbitMQ is the default local provider; Azure Service Bus uses the same event and operator contracts when `Messaging:Provider=AzureServiceBus`. For local broker selection, use [docs/local-dev/messaging.md](../local-dev/messaging.md).

## High-level topology

```mermaid
graph TD
    Client([Client]) --> GW["API Gateway<br/>YARP · :8004"]
    GW --> Basket["Basket<br/>:8000"]
    GW --> Order["Order<br/>:8001"]
    GW --> Product["Product<br/>:8002"]
    GW --> Auth["Auth<br/>:8003"]
    GW --> Inventory["Inventory<br/>:8005"]
    GW --> Shipping["Shipping<br/>:8006"]
    GW --> Payment["Payment<br/>:8007"]
    GW --> Saga["Saga<br/>:8008"]

    Basket --- Redis[(Redis)]
    Order --- SQLOrder[(SQL · Order)]
    Product --- SQLProduct[(SQL · Product)]
    Auth --- SQLAuth[(SQL · Auth)]
    Inventory --- SQLInventory[(SQL · Inventory)]
    Shipping --- SQLShipping[(SQL · Shipping)]
    Payment --- SQLPayment[(SQL · Payment)]
    Saga --- SQLSaga[(SQL · Saga)]

    Order -- publishes --> Broker{{"Broker<br/>RabbitMQ exchange<br/>or ASB topic"}}
    Product -- publishes --> Broker
    Inventory -- publishes --> Broker
    Shipping -- publishes --> Broker
    Payment -- publishes --> Broker
    Saga -- publishes commands --> Broker
    Broker -- subscribes --> Basket
    Broker -- subscribes --> Order
    Broker -- subscribes --> Inventory
    Broker -- subscribes --> Shipping
    Broker -- subscribes --> Payment
    Broker -- subscribes --> Saga

    Saga -- commands --> Order
    Saga -- commands --> Inventory
    Saga -- commands --> Payment
    Saga -- commands --> Shipping
    Order -- reply events --> Saga
    Inventory -- reply events --> Saga
    Payment -- reply events --> Saga
    Shipping -- reply events --> Saga
```

## Core design rules

| Rule | Why |
|---|---|
| **Per-service datastore** | Services deploy, scale, and evolve their schemas independently. No shared databases. |
| **Event-driven cross-service communication** | Services publish domain events to a fanout exchange; subscribers react. No synchronous service-to-service HTTP. |
| **Transactional Outbox** | Each service writes business state and the outbound event record in one DB transaction; a background service publishes from the outbox. This prevents the "event published but DB rolled back" or "DB committed but event lost" failure modes. |
| **API Gateway owns public auth** | JWT validation and role checks happen at the gateway. Downstream services still validate the token but trust the gateway for routing. |
| **DTO vs Domain separation** | `ApiModels/` holds request/response DTOs; `Models/` holds internal domain entities. |
| **Shared cross-cutting library** | `ECommerce.Shared` centralizes JWT, EventBus, Outbox, Observability, Health — see [Shared-Library](Shared-Library). |
| **Pluggable messaging & telemetry providers** | `Messaging__Provider` switches between `RabbitMqEventBus` and `AzureServiceBusEventBus`; `OpenTelemetry__Exporter` switches between local OTLP and Application Insights. Same `IEventBus`, same handlers. See [Azure-Deployment](Azure-Deployment). |


## Saga: orchestrator coordinates Order, Inventory, Payment, Shipping

The [Saga service](Service-Saga) (`:8008`) owns the order saga end-to-end. It opens a persisted saga instance on `OrderCreatedEvent`, drives the four participants via commands, and advances on their reply events. Order's "confirm" edge is gated on `PaymentAuthorizedEvent`, so no unpaid order proceeds to shipment. Capture happens when goods physically dispatch. See [ADR-0010](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/adr/0010-saga-orchestrator-supersedes-choreography.md).

```mermaid
sequenceDiagram
    autonumber
    participant O as Order
    participant Sg as Saga
    participant I as Inventory
    participant P as Payment
    participant Sh as Shipping
    O-->>Sg: OrderCreatedEvent
    Sg->>I: ReserveStockCommand
    I-->>Sg: StockReservedEvent
    Sg->>P: AuthorizePaymentCommand
    P-->>Sg: PaymentAuthorizedEvent
    Sg->>O: ConfirmOrderCommand
    O-->>Sg: OrderConfirmedEvent
    Sg->>I: CommitStockCommand
    I-->>Sg: StockCommittedEvent
    Sg->>Sh: CreateShipmentCommand
    Sh-->>Sg: ShipmentCreatedEvent
    Sh-->>Sg: ShipmentDispatchedEvent
    Sg->>P: CapturePaymentCommand
    P-->>Sg: PaymentCapturedEvent
    Sh-->>Sg: ShipmentDeliveredEvent
```

Compensation (`ReleaseStockCommand`, `VoidPaymentCommand`, `RefundPaymentCommand`, `CancelShipmentCommand`, `CancelOrderCommand`) is issued by Saga depending on the last completed step. The full event catalog and saga command table live in [Integration-Events](Integration-Events); the dedicated saga diagrams live in [Diagram-Saga](Diagram-Saga).

## Observability flow

```mermaid
graph LR
    Svc[Any service] -- OTLP --> Collector[OTel Collector]
    Collector --> Jaeger[Jaeger · traces]
    Collector --> Loki[Loki · logs]
    Svc -- /metrics --> Prom[Prometheus]
    Prom --> AM[Alertmanager]
    Grafana --- Prom
    Grafana --- Loki
    Grafana --- Jaeger
```

See [Observability](Observability) for dashboards and alerts.

## Authentication flow

1. Client calls `POST /login` on the Gateway → proxied to Auth service.
2. Auth validates credentials against its SQL Server store and returns a JWT (HMAC-SHA256) with `user_role` claims.
3. Client includes `Authorization: Bearer <jwt>` on subsequent requests.
4. The Gateway validates the token and enforces role policies (e.g. `Administrator` for write ops on Product and Inventory).
5. Downstream services validate the token again via the shared `AddJwtAuthentication()` extension.

See [Service-Auth](Service-Auth) and [Service-API-Gateway](Service-API-Gateway).

## References

- Repo: [README](https://github.com/daonhan/Microservices-in-.NET#architecture)
- Existing PRDs: [`docs/prd/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/docs/prd)
- Implementation plans: [`docs/plans/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/docs/plans)
