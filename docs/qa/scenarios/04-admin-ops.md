# Scenario 04: Admin Ops

Reset the stack so the seeded admin-ops fixtures start deterministic:

```powershell
docker compose down -v
docker compose up --build
```

Use the Bruno collections in `qa/bruno/04-admin-ops/<area>` with the `qa-local` environment.

## Admin vs Customer

The admin-ops scenarios require the `Administrator` role. Use the `microservices@daonhan.com` login (Bruno step `01 Login admin`) to obtain `adminToken`. The customer JWT will receive a `403` from any of the endpoints below.

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

## Acceptance check

- `GET /9004` shows `TotalOnHand = 1`, `LowStockThreshold = 2` on a fresh boot, then `LowStockThreshold = 5` after step 4.
- `POST /9005/restock` raises stock from `0` to `10` and writes a `StockMovements` row.
- `POST /9004/reserve` succeeds against the seeded threshold-tripped product and is idempotent on the same `orderId`.
- `POST /9005/backorder` writes a `BackorderRequests` row and returns the generated id.
