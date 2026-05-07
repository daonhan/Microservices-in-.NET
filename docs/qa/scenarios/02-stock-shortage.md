# Scenario 02: Stock Shortage

Reset the stack so the seeded zero-stock fixture starts deterministic:

```powershell
docker compose down -v
docker compose up --build
```

Use the Bruno collection in `qa/bruno/02-stock-shortage` with the `qa-local` environment.

## 1. Login as customer-cancel

HTTP: `POST http://localhost:8003/login` returns `200` with a `token` for `customer-cancel@qa.test`.

SQL:

```sql
SELECT Id, Username, Role FROM Auth.dbo.Users WHERE Username = 'customer-cancel@qa.test';
```

Event/log: Auth logs a `login-success` metric.

Jaeger: find an Auth span for `POST /login`.

## 2. Confirm seeded basket and zero-stock fixture

HTTP: `GET /{customerCancelId}` on Basket returns one `product-zero-stock` line; Inventory `GET /9003` returns `TotalOnHand = 0`.

SQL:

```sql
SELECT Id, Name, Price FROM Product.dbo.Products WHERE Id = 9003;
SELECT ProductId, TotalOnHand, TotalReserved FROM Inventory.dbo.StockItems WHERE ProductId = 9003;
SELECT ProductId, WarehouseId, OnHand, Reserved FROM Inventory.dbo.StockLevels WHERE ProductId = 9003;
```

Event/log: Basket startup logs should show no Redis seeder failure; Inventory should expose stock=0 with no reservation rows for product `9003`.

Jaeger: find spans for `GET /9003` on Inventory.

## 3. Place the stock-shortage order

HTTP: `POST http://localhost:8001/{customerCancelId}` with product `9003`, quantity `1`, returns `201` and a location containing the order id.

SQL:

```sql
SELECT OrderId, CustomerId, Status FROM [Order].dbo.Orders WHERE CustomerId = '00faac97-9ae4-4b7f-b8aa-00e7c569dd66' ORDER BY OrderId DESC;
SELECT OrderId, ProductId, Quantity FROM [Order].dbo.OrderProducts WHERE ProductId = '9003';
```

Event/log: RabbitMQ should receive `OrderCreatedEvent` and Inventory should publish `StockReservationFailedEvent`.

Jaeger: follow the trace from Order to Inventory; the Inventory span should record the failed reservation.

## 4. Wait for the order to land in `Cancelled`

HTTP: poll `GET http://localhost:8001/{customerCancelId}/{orderId}` until status is `Cancelled`. No shipment should ever be created.

SQL:

```sql
SELECT OrderId, Status FROM [Order].dbo.Orders WHERE OrderId = '<orderId>';
SELECT * FROM Inventory.dbo.StockReservations WHERE OrderId = '<orderId>';
SELECT ProductId, TotalOnHand, TotalReserved FROM Inventory.dbo.StockItems WHERE ProductId = 9003;
SELECT * FROM Shipping.dbo.Shipments WHERE OrderId = '<orderId>';
```

Event/log: observe `StockReservationFailedEvent` and `OrderCancelledEvent`. No `StockReservedEvent`, `PaymentAuthorizedEvent`, or `OrderConfirmedEvent` should appear for this order.

Jaeger: the order trace should terminate after Inventory's failed reservation, with no Payment or Shipping spans.
