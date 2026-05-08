# Scenario 04: Admin Ops

Reset the stack so the seeded admin-ops fixtures start deterministic:

```powershell
docker compose down -v
docker compose up --build
```

Use the Bruno collections in `qa/bruno/04-admin-ops/<area>` with the `qa-local` environment.

## Admin vs Customer

The admin-ops scenarios require the `Administrator` role. Use the `microservices@daonhan.com` login (Bruno step `01 Login admin`) to obtain `adminToken`. The customer JWT will receive a `403` from any of the endpoints below.

## Payment ops

These steps exercise `payment-microservice` and `order-microservice` admin endpoints against the seeded `OrderAuthorizedId` (`a0000...01`) and `OrderCapturedId` (`a0000...02`) fixtures. Run the requests in `qa/bruno/04-admin-ops/payment/`.

### 1. Capture authorized payment

HTTP: `POST http://localhost:8007/b0000000-0000-0000-0000-000000000001/capture` with the admin bearer returns `200` and flips status to `Captured`. The customer token returns `403`. A prior `GET` on `http://localhost:8007/by-order/a0000...01` would show the seeded `Authorized` state.

SQL:

```sql
SELECT PaymentId, OrderId, Status, ProviderReference FROM Payment.dbo.Payments WHERE OrderId = 'a0000000-0000-0000-0000-000000000001';
SELECT OrderId, CustomerId FROM Payment.dbo.OrderCustomers WHERE OrderId = 'a0000000-0000-0000-0000-000000000001';
```

Event/log: RabbitMQ observes a `PaymentCapturedEvent` for `OrderId=a...01`.

Jaeger: trace from Payment's `/capture` endpoint into the Outbox publish.

### 2. Refund captured payment

HTTP: `POST http://localhost:8007/b0000000-0000-0000-0000-000000000002/refund` with the admin bearer returns `200` and flips status to `Refunded`. The customer token returns `403`. A prior `GET` on `http://localhost:8007/by-order/a0000...02` would show the seeded `Captured` state.

SQL:

```sql
SELECT PaymentId, OrderId, Status, ProviderReference FROM Payment.dbo.Payments WHERE OrderId = 'a0000000-0000-0000-0000-000000000002';
```

Event/log: RabbitMQ observes a `PaymentRefundedEvent` for `OrderId=a...02`.

### 3. Cancel post-confirm order cascade

HTTP: `POST http://localhost:8001/5ff2d67e-c6b5-4870-911f-79393ed416fd/a0000000-0000-0000-0000-000000000002/cancel` with the admin bearer returns `200` and `Status=Cancelled`.

SQL:

```sql
SELECT OrderId, Status FROM Order.dbo.Orders WHERE OrderId = 'a0000000-0000-0000-0000-000000000002';
```

Event/log: RabbitMQ observes an `OrderCancelledEvent` cascade. This triggers the Payment service to consume it and emit a `PaymentRefundedEvent`, the Inventory service to release stock, and the Shipping service to cancel shipment (if present). All four events observable in RabbitMQ.

## Inventory ops

These steps exercise `inventory-microservice` admin endpoints against the seeded `product-low-stock` (`9004`, on-hand `1`, threshold `2`) and `product-restock-target` (`9005`, on-hand `0`) products. Run the requests in `qa/bruno/04-admin-ops/inventory/`.

### 1. Login as admin

HTTP: `POST http://localhost:8003/login` with admin credentials returns `200` and a `token` carrying `role=Administrator`.

SQL:

```sql
SELECT Id, Username, Role FROM Auth.dbo.Users WHERE Username = 'microservices@daonhan.com';
```

Event/log: Auth logs a `login-success` metric.

Jaeger: find an Auth span for `POST /login`.

### 2. Confirm the threshold-tripped state

HTTP: `GET http://localhost:8005/9004` returns `TotalOnHand = 1`, `LowStockThreshold = 2`. The seeded row already qualifies as low stock.

SQL:

```sql
SELECT Id, Name, Price FROM Product.dbo.Products WHERE Id IN (9004, 9005);
SELECT ProductId, TotalOnHand, TotalReserved, LowStockThreshold FROM Inventory.dbo.StockItems WHERE ProductId IN (9004, 9005);
SELECT ProductId, WarehouseId, OnHand, Reserved FROM Inventory.dbo.StockLevels WHERE ProductId IN (9004, 9005);
```

Event/log: nothing emitted yet — the seed migration writes the rows directly without raising stock events.

Jaeger: find spans for `GET /9004` and `GET /9005` on Inventory.

### 3. Restock `product-restock-target`

HTTP: `POST http://localhost:8005/9005/restock` with body `{ "quantity": 10 }` and the admin bearer returns `200` and `newOnHand = 10`. The customer token returns `403`.

SQL:

```sql
SELECT ProductId, TotalOnHand, TotalReserved FROM Inventory.dbo.StockItems WHERE ProductId = 9005;
SELECT ProductId, WarehouseId, OnHand, Reserved FROM Inventory.dbo.StockLevels WHERE ProductId = 9005;
SELECT TOP 5 ProductId, WarehouseId, Quantity, Type, Reason, OccurredAt FROM Inventory.dbo.StockMovements WHERE ProductId = 9005 ORDER BY Id DESC;
```

The movement row records the restock as the auditable trail.

Event/log: RabbitMQ should observe a `StockAdjustedEvent` for `productId=9005`, `quantity=10`. No low-stock crossing fires because the new threshold is `0`.

Jaeger: trace from Inventory's `/restock` endpoint into the Outbox publish.

### 4. Update threshold on `product-low-stock`

HTTP: `PUT http://localhost:8005/9004/threshold` with body `{ "threshold": 5 }` and the admin bearer returns `200` and `threshold = 5`. The customer token returns `403`.

SQL:

```sql
SELECT ProductId, LowStockThreshold FROM Inventory.dbo.StockItems WHERE ProductId = 9004;
```

A subsequent `GET /9004` reflects the change.

Event/log: a `LowStockEvent` for `productId=9004` may be republished if the threshold crossing rules trigger. No movement row is emitted because the on-hand count did not change.

Jaeger: span list shows the `PUT /threshold` request flowing through the Inventory unit-of-work.

### 5. Manual reserve against `product-low-stock`

HTTP: `POST http://localhost:8005/9004/reserve` with body `{ "orderId": "00000000-0000-0000-0000-000090040001", "quantity": 1 }` returns `200`. Re-running with the same `orderId` is idempotent and returns `200` without double-reserving.

SQL:

```sql
SELECT ProductId, TotalOnHand, TotalReserved FROM Inventory.dbo.StockItems WHERE ProductId = 9004;
SELECT OrderId, ProductId, WarehouseId, Quantity, Status FROM Inventory.dbo.StockReservations WHERE OrderId = '00000000-0000-0000-0000-000090040001';
```

Event/log: the first call emits one `StockReservedEvent`; subsequent calls are no-ops.

Jaeger: trace shows the reservation span and the resulting Outbox publish.

### 6. Back-order `product-restock-target`

HTTP: `POST http://localhost:8005/9005/backorder` with body `{ "customerId": "{{customerHappyId}}", "quantity": 3 }` returns `200` and a generated id. (This endpoint accepts any authenticated user; running with the customer token also succeeds.)

SQL:

```sql
SELECT TOP 5 Id, CustomerId, ProductId, Quantity, FulfilledAt, CreatedAt FROM Inventory.dbo.BackorderRequests WHERE ProductId = 9005 ORDER BY Id DESC;
```

Event/log: no integration event is published — the back-order endpoint persists the request only.

Jaeger: trace shows the Inventory back-order span.

## Shipping ops

These steps exercise `shipping-microservice` admin endpoints against the seeded shipments owned by `customer-happy`. Five fixtures, one per non-trivial status, sit in the database after a fresh `docker compose up`. Run the requests in `qa/bruno/04-admin-ops/shipping/`. **Every transition below is admin-only** — the customer JWT receives `403`.

| Shipment Id | Initial status | Order Id | Tests this transition |
|---|---|---|---|
| `c0000000-...01` | `Pending` | `d0000000-...01` | `pick` |
| `c0000000-...02` | `Picked` | `d0000000-...02` | `pack` |
| `c0000000-...03` | `Packed` | `d0000000-...03` | `dispatch` |
| `c0000000-...04` | `Shipped` | `d0000000-...04` | `deliver`, `fail`, `return`, carrier webhook |
| `c0000000-...05` | `Pending` | `d0000000-...05` | `cancel` |

Shipment `c0000000-...04` is pre-stamped with `CarrierKey=fake-ground`, `TrackingNumber=QA-TRACK-DISPATCHED-001`, `LabelRef=label://qa/...`, `QuotedPriceAmount=5.00 USD` so the carrier-webhook lookup resolves without first running `dispatch`.

### 1. Pick the pending shipment

HTTP: `POST http://localhost:8006/c0000000-0000-0000-0000-000000000001/pick` with the admin bearer returns `200` and `Status=Picked`.

SQL:

```sql
SELECT Id, Status, OrderId, CustomerId FROM Shipping.dbo.Shipments WHERE Id = 'c0000000-0000-0000-0000-000000000001';
SELECT TOP 5 ShipmentId, Status, Source, OccurredAt FROM Shipping.dbo.ShipmentStatusHistory WHERE ShipmentId = 'c0000000-0000-0000-0000-000000000001' ORDER BY Id DESC;
```

Event/log: RabbitMQ observes a `ShipmentStatusChangedEvent` with `FromStatus=Pending`, `ToStatus=Picked`. No milestone event for `pick`.

Jaeger: trace from Shipping's `/pick` endpoint into the Outbox publish.

### 2. Pack the picked shipment

HTTP: `POST http://localhost:8006/c0000000-0000-0000-0000-000000000002/pack` with the admin bearer returns `200` and `Status=Packed`.

SQL: same `Shipments` and `ShipmentStatusHistory` queries scoped to `c0000000-...02`.

Event/log: RabbitMQ observes a `ShipmentStatusChangedEvent` with `FromStatus=Picked`, `ToStatus=Packed`.

### 3. Dispatch the packed shipment

HTTP: `POST http://localhost:8006/c0000000-0000-0000-0000-000000000003/dispatch` with the body in `04-dispatch-packed.bru` (carrier `fake-ground`, sample shipping address) returns `200`, `Status=Shipped`, plus generated `carrierKey`, `trackingNumber`, `labelRef`, and `quotedPriceAmount`.

SQL:

```sql
SELECT Id, Status, CarrierKey, TrackingNumber, LabelRef, QuotedPriceAmount, QuotedPriceCurrency
FROM Shipping.dbo.Shipments WHERE Id = 'c0000000-0000-0000-0000-000000000003';
```

Event/log: RabbitMQ observes a `ShipmentDispatchedEvent` (with the populated tracking number and quoted price) and a `ShipmentStatusChangedEvent` with `FromStatus=Packed`, `ToStatus=Shipped`. The order's tracking fields update via the existing saga subscriber.

Jaeger: trace shows the `/dispatch` request, the carrier `DispatchAsync` span, and the Outbox publish.

### 4. Deliver, fail, or return the dispatched shipment

These three transitions all act on `c0000000-...04` and are mutually exclusive — pick one per fixture run.

- `POST http://localhost:8006/c0000000-...04/deliver` → `200`, `Status=Delivered`, emits `ShipmentDeliveredEvent`.
- `POST http://localhost:8006/c0000000-...04/fail` with `{ "reason": "..." }` → `200`, `Status=Failed`, emits `ShipmentFailedEvent`.
- `POST http://localhost:8006/c0000000-...04/return` with `{ "reason": "..." }` → `200`, `Status=Returned`, emits `ShipmentReturnedEvent`.

SQL: same `Shipments` query scoped to `c0000000-...04`. The status history row records the transition source as `Admin (1)`.

Event/log: each transition emits its named milestone event plus a `ShipmentStatusChangedEvent` carrying the from/to pair. To re-test another transition, run `docker compose down -v && up` to reset.

### 5. Cancel the cancellable pending shipment

HTTP: `POST http://localhost:8006/c0000000-0000-0000-0000-000000000005/cancel` with body `{ "reason": "QA admin-ops cancel path" }` returns `200` and `Status=Cancelled`.

SQL: same `Shipments` and `ShipmentStatusHistory` queries scoped to `c0000000-...05`.

Event/log: RabbitMQ observes a `ShipmentCancelledEvent` and a `ShipmentStatusChangedEvent`.

### 6. Carrier webhook ingestion

HTTP: `POST http://localhost:8006/webhooks/carrier/fake-ground` with header `X-Carrier-Secret: change-me-ground` and body:

```json
{
  "trackingNumber": "QA-TRACK-DISPATCHED-001",
  "statusCode": 2,
  "detail": "QA admin-ops webhook smoke"
}
```

returns `200` and `{ "shipmentId": "c0000000-...04", "status": "InTransit" }`. `statusCode=3` flips the shipment to `Delivered` (and emits `ShipmentDeliveredEvent`); `statusCode=4` flips it to `Failed` (and emits `ShipmentFailedEvent` using the request `detail` as the reason).

The shared secret comes from `CarrierWebhooks:SharedSecrets` in `shipping-microservice/Shipping.Service/appsettings.json` (default `change-me-ground`). Override `carrierGroundSecret` in `qa-local.bru` if your environment changes it.

SQL:

```sql
SELECT Id, Status FROM Shipping.dbo.Shipments WHERE Id = 'c0000000-0000-0000-0000-000000000004';
SELECT TOP 5 ShipmentId, Status, Source, OccurredAt
FROM Shipping.dbo.ShipmentStatusHistory WHERE ShipmentId = 'c0000000-0000-0000-0000-000000000004' ORDER BY Id DESC;
```

The history row has `Source=3` (`CarrierWebhook`).

Event/log: RabbitMQ observes a `ShipmentStatusChangedEvent` with the new from/to pair, plus a `ShipmentDeliveredEvent` or `ShipmentFailedEvent` when applicable.

Jaeger: trace begins at `/webhooks/carrier/fake-ground`, runs through `CarrierStatusApplier`, and ends at the Outbox publish.

## Acceptance check

- `GET /by-order/{authorizedOrderId}` returns `Authorized`; `POST /{id}/capture` flips it to `Captured` and emits `PaymentCapturedEvent`.
- `POST /{capturedPaymentId}/refund` flips to `Refunded` and emits `PaymentRefundedEvent`.
- Cancelling a confirmed order cascades `OrderCancelledEvent` → `PaymentRefundedEvent` + inventory release + shipment cancel.
- Runbook clearly outlines admin vs customer JWT requirements with 403 expectations.
- `GET /9004` shows `TotalOnHand = 1`, `LowStockThreshold = 2` on a fresh boot, then `LowStockThreshold = 5` after step 4.
- `POST /9005/restock` raises stock from `0` to `10` and writes a `StockMovements` row.
- `POST /9004/reserve` succeeds against the seeded threshold-tripped product and is idempotent on the same `orderId`.
- `POST /9005/backorder` writes a `BackorderRequests` row and returns the generated id.
- Each shipping transition (`pick`, `pack`, `dispatch`, `deliver`, `fail`, `return`, `cancel`) succeeds against its corresponding pre-seeded shipment without any prior walk-through.
- `POST /shipping/{packedId}/dispatch` emits `ShipmentDispatchedEvent` with the generated tracking number and quoted price.
- Carrier webhook (`POST /shipping/webhooks/carrier/fake-ground`) ingests the sample payload, returns `200`, and updates `c0000000-...04` to the requested non-terminal status.
- All shipping transitions return `403` for the customer JWT.
