# Saga Orchestrator Strangler Runbook

This runbook covers the period where the Saga service can orchestrate selected new orders while the existing Order, Inventory, Payment, and Shipping choreography remains active for every non-selected order.

## Ownership Rule

Each order must be owned by exactly one path for its lifetime.

- Choreography owner: the order is not selected by `Saga:Orchestrator:*`; existing participant event handlers drive the flow.
- Orchestrator owner: the Saga service opens an `Order` saga from `OrderCreatedEvent` and drives participants with commands.
- In-flight choreography orders are not migrated into the orchestrator.

The Saga service decides once, when it handles `OrderCreatedEvent`:

- `Saga:Orchestrator:Enabled=false`: no new orders are orchestrated.
- `Saga:Orchestrator:Enabled=true` with `Percentage=0` and no allowlist match: choreography fallback.
- `AllowList` match or deterministic percentage match: orchestrator path.

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

## Half-On-Flag Failure Mode

This means a new order is being processed by choreography while the Saga service also opened a saga for the same `OrderId`. Treat this as a rollout incident because duplicate commands can cause compensation or duplicate side effects.

Immediate actions:

1. Stop selecting new orders for orchestration by setting `Saga:Orchestrator:Percentage=0` and clearing the allowlist, or set `Saga:Orchestrator:Enabled=false`.
2. Do not replay saga DLQ messages or use saga retry until ownership is confirmed.
3. Query the saga row and participant state for the order.

Saga lookup:

```sql
SELECT si.SagaId, si.CurrentStep, si.Status, si.LastCommandId, si.CreatedAt, si.UpdatedAt
FROM Saga.dbo.SagaInstances si
JOIN Saga.dbo.OrderSagaStates os ON os.SagaId = si.SagaId
WHERE os.OrderId = '<order-id>';
```

Participant checks:

- Order: current `OrderStatus` and whether `OrderConfirmedEvent` / `OrderCancelledEvent` outbox rows exist.
- Inventory: reservation status for the `OrderId`.
- Payment: payment status for the `OrderId`.
- Shipping: shipment status for the `OrderId`.
- Gateway DLQ: pending rows where `CorrelationId` or payload contains the `OrderId`.

Resolution guidance:

- If choreography already reached a terminal business state, do not blindly abort the saga. Saga abort starts compensation and can change customer-visible state. Escalate with the state snapshot and choose an explicit corrective action.
- If the Saga service owns the order and choreography has not advanced it, use the saga operator retry or DLQ replay path for the failed step.
- If both paths emitted side effects, freeze automated replay and reconcile from the participant stores first. Prefer idempotent no-op replays only after confirming the current step still expects that message.

## Choreography Fallback Smoke

Before any rollout and before handler removal, prove non-selected orders still use choreography. Run with `Saga:Orchestrator:Enabled=true`, `Saga:Orchestrator:Percentage=0`, and no allowlist entry for the QA order ids:

```powershell
pwsh scripts/local-smoke-test.ps1 -Scenario happy
pwsh scripts/local-smoke-test.ps1 -Scenario stock-out
pwsh scripts/local-smoke-test.ps1 -Scenario decline
```

Coverage:

- `happy` exercises `PaymentAuthorizedEventHandler`.
- `stock-out` exercises `StockReservationFailedEventHandler`.
- `decline` exercises `PaymentFailedEventHandler`.

Expected result: orders reach the same terminal states as the pre-orchestrator choreography path, and no `Saga.dbo.OrderSagaStates` row exists for those `OrderId` values.

## Cutover Criteria

Do not remove choreography handlers until all of the following are true:

- `Saga:Orchestrator:Enabled=true` and `Saga:Orchestrator:Percentage=100` have handled all new orders for two continuous weeks.
- Zero manual operator interventions were attributable to the orchestrator path during that window.
- Operator dashboard snapshots or incident logs document the two-week window.
- DLQ shows no unresolved orchestrator command or reply pattern.
- Reaper metrics show no persistent overdue-step backlog.
- Smoke scenarios pass with 100 percent orchestration: happy path, stock shortage, payment decline, shipment failure, overdue reaper compensation, operator abort compensation.
- Choreography fallback smoke above passed immediately before the cutover decision.

Record the cutover date in ADR-0010 or a follow-up ADR before removing choreography handlers.
