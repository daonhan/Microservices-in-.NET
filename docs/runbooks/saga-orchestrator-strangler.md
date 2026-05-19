# Saga Orchestrator Runbook

The Saga service is the sole driver of the order and refund sagas. Choreography saga-step handlers were removed on 2026-05-18 (issue #132), and the strangler feature flag `Saga:Orchestrator:Enabled` / `Percentage` / `AllowList` was removed on 2026-05-19 (issue #136). Every `OrderCreatedEvent` opens an `Order` saga; every `RefundRequestedEvent` opens a `Refund` saga.

The pre-cutover choreography code is preserved on the [`saga-choreography`](https://github.com/daonhan/Microservices-in-.NET/tree/saga-choreography) branch. Use it as a read-only reference when investigating legacy traces or comparing handler behaviour; do not merge it back to `main`.

## Ownership Rule

The Saga service owns the saga state machine and dispatches every command. Participant services (Order, Inventory, Payment, Shipping) only respond to commands and publish reply events. There is no fallback path.

## Idempotency Assertion

`OrderCreatedEventHandler` must never open two sagas for the same `OrderId`.

The handler checks the `OrderSagaStates.OrderId` unique business key before inserting a saga, and the database model also has a unique index on `OrderSagaState.OrderId`. The regression test is:

```powershell
cd saga-microservice
dotnet test --filter "FullyQualifiedName~OrderCreatedEventHandlerTests"
```

Expected result: replaying `OrderCreatedEvent` with the same `OrderId` stores one saga, one transition, and one `ReserveStockCommand`.

## DLQ Verification

Saga commands and reply events are normal `Event` / `Command` messages on the existing broker topology. They use the same RabbitMQ path as other services:

1. Subscriber exhausts retry budget.
2. `RabbitMqHostedService` dead-letters to `ecommerce-dlq`.
3. API Gateway's DLQ capture worker persists the row in `dead_letter_messages` with `Origin=DeadLetter`.
4. Operator replay publishes the same payload back to `OriginalQueue`.
5. The subscriber dispatches by `x-event-type`, so saga commands replay to the same handler.

Regression test:

```powershell
cd shared-libs
dotnet test --filter "FullyQualifiedName~RabbitMqDeadLetterIntegrationTests.Given_saga_reserve_stock_command_dead_letters_When_gateway_capture_replays_Then_original_queue_resumes"
```

Expected row values:

- `EventType`: `ReserveStockCommand`
- `OriginalQueue`: the Inventory subscriber queue that failed
- `Service`: same Inventory subscriber queue
- `Origin`: `DeadLetter`
- `Status`: `Pending` before replay, `Replayed` after replay
- `CorrelationId`: preserved from the original saga command

## Saga Lookup

```sql
SELECT si.SagaId, si.CurrentStep, si.Status, si.LastCommandId, si.CreatedAt, si.UpdatedAt
FROM Saga.dbo.SagaInstances si
JOIN Saga.dbo.OrderSagaStates os ON os.SagaId = si.SagaId
WHERE os.OrderId = '<order-id>';
```

Participant cross-checks:

- Order: current `OrderStatus` and whether `OrderConfirmedEvent` / `OrderCancelledEvent` outbox rows exist.
- Inventory: reservation status for the `OrderId`.
- Payment: payment status for the `OrderId`.
- Shipping: shipment status for the `OrderId`.
- Gateway DLQ: pending rows where `CorrelationId` or payload contains the `OrderId`.

## Operator Recovery

For a stuck saga, prefer in order:

1. Use the saga operator retry endpoint to re-dispatch the in-flight command for the current step.
2. Replay the failed message from the gateway DLQ if the failure was message-level.
3. Use the saga operator abort endpoint to force compensation when the order cannot proceed.

Avoid blind aborts: aborting starts compensation and changes customer-visible state. Confirm the current step before invoking abort.

## Smoke

```powershell
pwsh scripts/local-smoke-test.ps1 -Scenario happy
pwsh scripts/local-smoke-test.ps1 -Scenario stock-out
pwsh scripts/local-smoke-test.ps1 -Scenario decline
pwsh scripts/local-smoke-test.ps1 -Scenario saga-happy-orchestrated
pwsh scripts/local-smoke-test.ps1 -Scenario saga-decline-orchestrated
```

Every scenario expects an orchestrator-owned saga row; choreography no longer runs.
