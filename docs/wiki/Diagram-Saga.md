# Saga overview — order fulfillment

Choreography (no orchestrator). Each service reacts to upstream events. See [ADR-0008](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/adr/0008-saga-choreography-no-central-orchestrator.md). Detailed alt-branch sequence: [Integration-Events](Integration-Events#saga-and-fulfillment-sequence).

## Happy path (event hops)

```mermaid
flowchart LR
    O([Order]) -- OrderCreated --> I([Inventory])
    I -- StockReserved --> P([Payment])
    P -- PaymentAuthorized --> O
    O -- OrderConfirmed --> I
    I -- StockCommitted --> S([Shipping])
    S -- ShipmentDispatched --> P
    P -- PaymentCaptured --> X((done))
    S -- ShipmentDelivered --> X

    classDef svc fill:#1e293b,stroke:#0ea5e9,color:#fff,stroke-width:2px
    classDef done fill:#16a34a,stroke:#16a34a,color:#fff
    class O,I,P,S svc
    class X done
```

## Compensations

```mermaid
flowchart LR
    subgraph stockfail [Insufficient stock]
        I1([Inventory]) -- StockReservationFailed --> O1([Order])
        O1 -- OrderCancelled --> I1
        I1 -- StockReleased --> X1((cancelled))
    end

    subgraph paymentfail [Payment declined]
        P2([Payment]) -- PaymentFailed --> O2([Order])
        O2 -- OrderCancelled --> I2([Inventory])
        I2 -- StockReleased --> X2((cancelled))
        O2 -- OrderCancelled --> S2([Shipping])
    end

    classDef svc fill:#7f1d1d,stroke:#dc2626,color:#fff
    classDef done fill:#475569,stroke:#475569,color:#fff
    class O1,I1,P2,O2,I2,S2 svc
    class X1,X2 done
```

## End-to-end sequence (happy path)

```mermaid
sequenceDiagram
    autonumber
    participant O as Order
    participant I as Inventory
    participant P as Payment
    participant S as Shipping
    O->>I: OrderCreatedEvent
    I-->>P: StockReservedEvent
    P-->>O: PaymentAuthorizedEvent
    O->>I: OrderConfirmedEvent
    I-->>S: StockCommittedEvent
    S-->>P: ShipmentDispatchedEvent
    P-->>O: PaymentCapturedEvent
    S-->>O: ShipmentDeliveredEvent
```

> Bus omitted for clarity. All hops go through RabbitMQ fanout `ecommerce-exchange`. See [ADR-0004](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/adr/0004-rabbitmq-fanout-with-dlq-and-operator-api.md).
