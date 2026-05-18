# Scenario 06: Customer-Initiated Refund Saga

Start from a clean stack with the orchestrator fully enabled:

```powershell
docker compose down -v
docker compose up --build
```

Set the Saga service environment to `SAGA_ORCHESTRATOR_ENABLED=true` and
`SAGA_ORCHESTRATOR_PERCENTAGE=100` before the run. The refund saga follows the
same allowlist/percentage scheme as the order saga, keyed by `OrderId`.

## 1. Place and fulfil an orchestrated order

HTTP: use the happy-path customer flow (Scenario 01) to create and fulfil an
order for product `9001` so it has an authorized payment and a created
shipment. Note the resulting `OrderId`, `PaymentId`, and `ShipmentId`.

SQL:

```sql
SELECT si.SagaId, si.SagaType, si.CurrentStep, si.Status
FROM Saga.dbo.SagaInstances si
WHERE si.SagaType = 'Order'
ORDER BY si.CreatedAt DESC;
```

## 2. Request a refund

HTTP: trigger the customer-initiated refund flow on Order for the `OrderId`.
Order publishes `RefundRequestedEvent` carrying `OrderId`, `PaymentId`,
`ShipmentId`, `RefundAmount`, and `Currency`.

Expected: the Saga service opens a `Refund` saga that parks at
`PaymentRefunding` and dispatches a `RefundPaymentCommand`.

SQL:

```sql
SELECT si.SagaId, si.SagaType, si.CurrentStep, si.Status,
       rs.OrderId, rs.PaymentId, rs.ShipmentId, rs.RefundAmount, rs.Currency
FROM Saga.dbo.SagaInstances si
JOIN Saga.dbo.RefundSagaStates rs ON rs.SagaId = si.SagaId
ORDER BY si.CreatedAt DESC;
```

Event/log: Saga logs `Refund saga {SagaId} opened at step PaymentRefunding`.

Jaeger: find the `saga.transition` span tagged `saga.type=Refund`.

## 3. Payment refunded, shipment cancelled (happy path)

Expected: Payment emits `PaymentRefundedEvent`; the saga advances to
`ShipmentCancellingOrReturning` and dispatches `CancelShipmentCommand`.
Shipping emits `ShipmentCancelledEvent`; the saga reaches `Completed`.

SQL:

```sql
SELECT FromStep, ToStep, TriggerKind, Error
FROM Saga.dbo.SagaTransitions
WHERE SagaId = '<sagaId>'
ORDER BY Timestamp, Id;
```

Expected terminal row: `Status = Completed`, `CurrentStep = Completed`,
three transitions (`Started -> PaymentRefunding`,
`PaymentRefunding -> ShipmentCancellingOrReturning`,
`ShipmentCancellingOrReturning -> Completed`).

## 4. Failure branches

- **Refund declined:** Payment emits `PaymentFailedEvent` while in
  `PaymentRefunding`. The saga parks at `Status = Failed` (nothing was changed
  downstream, no compensation required). `saga_failed_total{type="Refund"}`
  increments.
- **Shipment action fails after refund:** Shipping emits `ShipmentFailedEvent`
  while in `ShipmentCancellingOrReturning`. The money is already back with the
  customer, so the saga compensates by dispatching `CancelOrderCommand`, moves
  to `CancellingOrder` with `Status = Compensating`, and on
  `OrderCancelledEvent` reaches `Compensated`.
- **No shipment on the order:** if `ShipmentId` is null, the saga completes
  directly on `PaymentRefundedEvent` without a shipment action.

## 5. Cutover check

The refund saga reuses `saga_started_total`, `saga_completed_total`,
`saga_failed_total`, and `saga_compensation_total`, all tagged `type=Refund`,
so the cutover dashboards in
`docs/runbooks/saga-orchestrator-strangler.md` evaluate the refund path
alongside the order path.
