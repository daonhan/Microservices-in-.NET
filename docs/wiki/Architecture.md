# Architecture

The platform decomposes an e-commerce domain into eight independently deployable business services. Each service owns its data, communicates with the outside world through the API Gateway, and with other services through asynchronous events on the provider-selected broker. RabbitMQ is the default local provider; Azure Service Bus uses the same event and operator contracts when `Messaging:Provider=AzureServiceBus`. For local broker selection, use [docs/local-dev/messaging.md](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/local-dev/messaging.md).

## High-level topology

```mermaid
flowchart LR
    Client([Client / Web Browser]) -->|HTTP / JSON| GW

    subgraph Edge ["1. Edge & Ingress Layer"]
        GW["<strong>API Gateway</strong><br/>YARP / Ocelot · :8004<br/>JWT Auth · Routing · DLQ API"]
    end

    subgraph PublicServices ["2. Public API Services (Gateway-Routed)"]
        direction TB
        Auth["<strong>Auth Service</strong><br/>:8003 · JWT/JWKS"] --- DB_Auth[("Auth DB<br/>SQL Server")]
        Product["<strong>Product Service</strong><br/>:8002 · Catalog"] --- DB_Product[("Product DB<br/>SQL Server")]
        Basket["<strong>Basket Service</strong><br/>:8000 · Cart"] --- DB_Basket[("Basket Cache<br/>Redis")]
        Order["<strong>Order Service</strong><br/>:8001 · Sales"] --- DB_Order[("Order DB & Cache<br/>SQL + Redis")]
        Inventory["<strong>Inventory Service</strong><br/>:8005 · Stock"] --- DB_Inventory[("Inventory DB<br/>SQL Server")]
        Shipping["<strong>Shipping Service</strong><br/>:8006 · Logistics"] --- DB_Shipping[("Shipping DB<br/>SQL Server")]
    end

    subgraph InternalServices ["3. Internal-Only Services (Broker-Driven)"]
        direction TB
        Saga["<strong>Saga Orchestrator</strong><br/>:8008 · State Machine"] --- DB_Saga[("Saga DB<br/>SQL Server")]
        Payment["<strong>Payment Service</strong><br/>:8007 · Transactions"] --- DB_Payment[("Payment DB<br/>SQL Server")]
    end

    subgraph EventBackbone ["4. Async Event Backbone"]
        Broker{{"<strong>Message Broker</strong><br/>RabbitMQ (Exchange) or Azure Service Bus (Topic)"}}
    end

    %% Gateway Routing
    GW -->|Route| Auth
    GW -->|Route| Product
    GW -->|Route| Basket
    GW -->|Route| Order
    GW -->|Route| Inventory
    GW -->|Route| Shipping

    %% Service Integration via Broker
    Product -.->|Publish Catalog Events| Broker
    Broker -.->|Subscribe| Basket
    
    %% Saga Orchestration Loop (Logical Flow through Broker)
    Saga -.->|Publish Commands| Broker
    Order -.->|Pub/Sub| Broker
    Inventory -.->|Pub/Sub| Broker
    Payment -.->|Pub/Sub| Broker
    Shipping -.->|Pub/Sub| Broker
    Broker -.->|Deliver Cmds & Events| Saga

    %% Premium Aesthetics styling
    classDef client fill:#1e293b,stroke:#38bdf8,stroke-width:2px,color:#fff;
    classDef gateway fill:#0f172a,stroke:#0284c7,stroke-width:2px,color:#fff;
    classDef pubService fill:#1e293b,stroke:#475569,stroke-width:1.5px,color:#fff;
    classDef intService fill:#312e81,stroke:#4f46e5,stroke-width:1.5px,color:#fff;
    classDef database fill:#022c22,stroke:#059669,stroke-width:1px,color:#fff;
    classDef broker fill:#581c87,stroke:#c084fc,stroke-width:2px,color:#fff;

    class Client client;
    class GW gateway;
    class Auth,Product,Basket,Order,Inventory,Shipping pubService;
    class Saga,Payment intService;
    class DB_Auth,DB_Product,DB_Basket,DB_Order,DB_Inventory,DB_Shipping,DB_Saga,DB_Payment database;
    class Broker broker;
```


## Core design rules

| Rule | Why |
|---|---|
| **Per-service datastore** | Services deploy, scale, and evolve their schemas independently. No shared databases. |
| **Event-driven cross-service communication** | Services publish domain events to a fanout exchange; subscribers react. No synchronous service-to-service HTTP. |
| **Transactional Outbox** | Each service writes business state and the outbound event record in one DB transaction; a background service publishes from the outbox. This prevents the "event published but DB rolled back" or "DB committed but event lost" failure modes. |
| **API Gateway owns public auth** | JWT validation and role checks happen at the gateway. Downstream services still validate the token but trust the gateway for routing. |
| **Clean Architecture + Vertical Slices** | Default service layout is `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, and `Infrastructure/` ([ADR-0012](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/adr/0012-clean-arch-vsa-default-service-shape.md), [docs/PATTERNS.md](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/PATTERNS.md)). |
| **Shared cross-cutting packages** | Nine `ECommerce.Shared.*` capability packages centralize JWT, messaging, outbox, observability, health, contracts, testing helpers, and DLQ behavior; production services narrow-pin only what they use — see [Shared-Library](Shared-Library). |
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
    Svc["Any Service / Pod<br/>(e.g., Saga, Order, ...)"] -- OTLP traces & logs --> Collector[OTel Collector]
    Collector --> Jaeger[Jaeger · Traces]
    Collector --> Loki[Loki · Logs]
    Svc -- "/metrics scrape" --> Prom[Prometheus]
    Prom --> AM[Alertmanager · Alerts]
    Grafana[Grafana Dashboards] --- Prom
    Grafana --- Loki
    Grafana --- Jaeger

    %% Custom Styling classes
    classDef svc fill:#1e293b,stroke:#38bdf8,stroke-width:1.5px,color:#fff;
    classDef collector fill:#0f172a,stroke:#0ea5e9,stroke-width:2px,color:#fff;
    classDef backend fill:#312e81,stroke:#4f46e5,stroke-width:1.5px,color:#fff;
    classDef ui fill:#581c87,stroke:#c084fc,stroke-width:2px,color:#fff;

    class Svc svc;
    class Collector,Prom collector;
    class Jaeger,Loki,AM backend;
    class Grafana ui;
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
