# Saga overview — order fulfillment

The order saga is orchestrated by the [Saga service](Service-Saga) (`:8008`). Saga subscribes to `OrderCreatedEvent`, persists saga state, and drives Order, Inventory, Payment, and Shipping by sending commands. Participants execute the commands against their own datastore and publish reply integration events back to Saga, which carry `SagaId` and `CausationId`. See [ADR-0010](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/adr/0010-saga-orchestrator-supersedes-choreography.md) and the [strangler runbook](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/runbooks/saga-orchestrator-strangler.md).

## Happy path (canonical sequence)

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

## Compensation (orchestrator-issued reverse commands)

Saga drives the reverse path based on the last completed step. Each command targets the participant that owns the state being undone.

```mermaid
sequenceDiagram
    autonumber
    participant Sg as Saga
    participant I as Inventory
    participant P as Payment
    participant Sh as Shipping
    participant O as Order
    Note over Sg: Compensation depends on last completed step
    Sg->>Sh: CancelShipmentCommand
    Sh-->>Sg: ShipmentCancelledEvent
    Sg->>P: RefundPaymentCommand
    P-->>Sg: PaymentRefundedEvent
    Sg->>P: VoidPaymentCommand
    P-->>Sg: PaymentVoidedEvent
    Sg->>I: ReleaseStockCommand
    I-->>Sg: StockReleasedEvent
    Sg->>O: CancelOrderCommand
    O-->>Sg: OrderCancelledEvent
```

`RefundPaymentCommand` is issued when payment was already captured; `VoidPaymentCommand` is issued when payment was only authorized. `CancelShipmentCommand` is skipped if no shipment was created. `ReleaseStockCommand` is skipped if stock was never reserved.

## Notes

- All commands and reply events flow through the same broker path as integration events (RabbitMQ fanout `ecommerce-exchange` by default, Azure Service Bus when `Messaging:Provider=AzureServiceBus`). See [ADR-0004](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/adr/0004-rabbitmq-fanout-with-dlq-and-operator-api.md).
- Saga writes state changes and outgoing commands in the same `IOutboxUnitOfWork.ExecuteAsync` envelope (see [ADR-0002](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/adr/0002-transactional-outbox-per-publishing-service.md)).
- For the command catalog and event ⇄ service matrix, see [Integration-Events](Integration-Events).
