# Saga Service

Order and refund saga orchestrator. Persists saga state to SQL Server, dispatches commands through the platform event bus, and exposes operator APIs for retrying or aborting stuck workflows.

| | |
|---|---|
| **Port** | 8008 |
| **Datastore** | SQL Server (database: `Saga`) |
| **Source** | [`saga-microservice/Saga.Service/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/saga-microservice/Saga.Service) |
| **Tests** | [`saga-microservice/Saga.Tests/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/saga-microservice/Saga.Tests) |
| **Publishes** | Saga commands through `ECommerce.Shared.Contracts` |
| **Subscribes** | `OrderCreatedEvent`, `RefundRequestedEvent`, and participant reply events from Order, Inventory, Payment, and Shipping |
| **Layout** | Clean Architecture + Vertical Slices default ([ADR-0012](../adr/0012-clean-arch-vsa-default-service-shape.md)); saga triggers use the two-level `Features/<Saga>/<Trigger>/` shape. |

## Responsibility

Saga is the only service that knows the full order saga shape. Order starts the flow by publishing `OrderCreatedEvent`; Saga opens a persisted saga instance, emits the next command, and waits for a reply event with the same `SagaId` and the command's `CausationId`. Participant services do not coordinate with each other for saga steps. They execute commands, update their own datastore, and publish reply events.

The order saga happy path is:

`OrderCreatedEvent` -> `ReserveStockCommand` -> `StockReservedEvent` -> `AuthorizePaymentCommand` -> `PaymentAuthorizedEvent` -> `ConfirmOrderCommand` -> `OrderConfirmedEvent` -> `CommitStockCommand` -> `StockCommittedEvent` -> `CreateShipmentCommand` -> `ShipmentCreatedEvent` -> `Completed`.

The refund saga starts from `RefundRequestedEvent`, drives `RefundPaymentCommand`, optionally cancels shipment, and compensates by cancelling the order when shipment cancellation fails after money has already been returned.

## State model

Saga stores a generic header plus typed payload tables:

| Table | Purpose |
|---|---|
| `SagaInstances` | Generic saga header: `SagaId`, `SagaType`, `CurrentStep`, `Status`, `CorrelationId`, `Version`, timestamps, `NextTimeoutAt`, `RetryCount`, and `LastCommandId` |
| `OrderSagaStates` | Order-specific payload: `OrderId`, optional reservation/payment/shipment ids, amount, compensation origin, and last step result |
| `RefundSagaStates` | Refund-specific payload: `OrderId`, `PaymentId`, optional shipment id, refund amount, currency, and last step result |
| `SagaTransitions` | Audit trail for every transition: from/to step, timestamp, trigger message id, trigger kind, and optional error |

`SagaInstance.Version` is the optimistic concurrency token. `OrderSagaState.OrderId` is unique so replaying the same `OrderCreatedEvent` does not open a duplicate saga.

Statuses: `Running`, `Completed`, `Failed`, `Compensating`, `Compensated`, `Aborted`.

## Command catalog

| Participant | Command | Expected reply events |
|---|---|---|
| Order | `ConfirmOrderCommand` | `OrderConfirmedEvent` |
| Order | `CancelOrderCommand` | `OrderCancelledEvent` |
| Inventory | `ReserveStockCommand` | `StockReservedEvent`, `StockReservationFailedEvent` |
| Inventory | `CommitStockCommand` | `StockCommittedEvent` |
| Inventory | `ReleaseStockCommand` | `StockReleasedEvent` |
| Payment | `AuthorizePaymentCommand` | `PaymentAuthorizedEvent`, `PaymentFailedEvent` |
| Payment | `CapturePaymentCommand` | `PaymentCapturedEvent`, `PaymentFailedEvent` |
| Payment | `VoidPaymentCommand` | `PaymentVoidedEvent`, `PaymentFailedEvent` |
| Payment | `RefundPaymentCommand` | `PaymentRefundedEvent`, `PaymentFailedEvent` |
| Shipping | `CreateShipmentCommand` | `ShipmentCreatedEvent`, `ShipmentFailedEvent` |
| Shipping | `CancelShipmentCommand` | `ShipmentCancelledEvent`, `ShipmentFailedEvent` |

Every command inherits from `Command`, carries `SagaId` and `CausationId`, and flows through the same broker/outbox/DLQ path as normal integration events.

## Participant interaction

- Saga uses `AddPlatformEventBus`, `AddPlatformEventPublisher`, and `AddPlatformSubscriberService`, so RabbitMQ and Azure Service Bus share one command/event contract.
- State changes and outgoing commands are saved in one `IOutboxUnitOfWork.ExecuteAsync` envelope.
- Reply handlers route through `OrderSagaReplyProcessor` or `RefundSagaReplyProcessor`; redelivered replies no-op when the saga has already advanced past the expected step.
- Compensation is explicit. Depending on the last completed step, Saga issues `ReleaseStockCommand`, `VoidPaymentCommand`, `RefundPaymentCommand`, `CancelShipmentCommand`, and/or `CancelOrderCommand`.

## Timeout and reaper behavior

`SagaReaperService` runs on a `PeriodicTimer`. It scans `Running` order sagas where `NextTimeoutAt <= now`.

- If the retry budget remains, it requeues `LastCommandId` from the outbox and moves `NextTimeoutAt` forward.
- If retries are exhausted, it starts compensation from the last completed step.
- If the in-flight command cannot be found or the current step cannot be parsed, it parks the saga as `Failed`.

Metrics and spans are emitted through `SagaTelemetry`: started/completed/failed counters, compensation and overdue counters, step duration histogram, and transition spans with saga tags.

## Operator API

All operator routes require Bearer auth with the existing `RequireService` policy.

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/operator/api/sagas` | List saga instances; filters include `type`, `status`, and `overdue` |
| `GET` | `/operator/api/sagas/{id}` | Read saga detail, order payload, and transition history |
| `POST` | `/operator/api/sagas/{id}/retry` | Requeue the current in-flight command |
| `POST` | `/operator/api/sagas/{id}/abort` | Force a running order saga into compensation |
| `GET` | `/internal/outbox/failed` | Expose failed saga outbox rows to the gateway DLQ poller |

## Related docs

- [PRD-Saga-Orchestrator](../prd/PRD-Saga-Orchestrator.md)
- [Saga orchestrator strangler runbook](../runbooks/saga-orchestrator-strangler.md)
- [ADR-0010 — Saga orchestrator (supersedes ADR-0008)](../adr/0010-saga-orchestrator-supersedes-choreography.md)
- [ADR-0012 — Clean Architecture + Vertical Slices default](../adr/0012-clean-arch-vsa-default-service-shape.md)
- [Integration events](Integration-Events)
