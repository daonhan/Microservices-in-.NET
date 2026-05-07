# Scenario 03: Payment Decline

Reset the stack so seeded reservations and stock balances start deterministic:

```powershell
docker compose down -v
docker compose up --build
```

Use the Bruno collection in `qa/bruno/03-payment-decline` with the `qa-local` environment.

## 1. Login as customer-decline

HTTP: `POST http://localhost:8003/login` returns `200` with a `token` for `customer-decline@qa.test`.

SQL:

```sql
SELECT Id, Username, Role FROM Auth.dbo.Users WHERE Username = 'customer-decline@qa.test';
```

Event/log: Auth logs a `login-success` metric.

Jaeger: find an Auth span for `POST /login`.

## 2. Confirm seeded basket and decline-priced product

HTTP: `GET /{customerDeclineId}` on Basket returns one `product-decline` line; Product `GET /9002` returns price `9.99` (cents == 99 triggers `InMemoryPaymentGateway` decline).

SQL:

```sql
SELECT Id, Name, Price FROM Product.dbo.Products WHERE Id = 9002;
SELECT ProductId, TotalOnHand, TotalReserved FROM Inventory.dbo.StockItems WHERE ProductId = 9002;
SELECT ProductId, WarehouseId, OnHand, Reserved FROM Inventory.dbo.StockLevels WHERE ProductId = 9002;
```

Event/log: Basket startup logs should show no Redis seeder failure; Inventory should report sufficient stock for product `9002`.

Jaeger: find spans for `GET /9002` on Product and Inventory.

## 3. Place the decline-priced order

HTTP: `POST http://localhost:8001/{customerDeclineId}` with product `9002`, quantity `1`, returns `201` and a location containing the order id.

SQL:

```sql
SELECT OrderId, CustomerId, Status FROM [Order].dbo.Orders WHERE CustomerId = 'be0d0a1d-c8fe-4b17-bf6a-051e8c809aa6' ORDER BY OrderId DESC;
SELECT OrderId, ProductId, Quantity FROM [Order].dbo.OrderProducts WHERE ProductId = '9002';
```

Event/log: RabbitMQ should receive `OrderCreatedEvent`, then `StockReservedEvent` (Inventory holds 1 unit), then `PaymentFailedEvent` from Payment.

Jaeger: follow the trace from Order to Inventory (successful reservation) to Payment (failed authorize).

## 4. Wait for the order to land in `Cancelled` and stock to be released

HTTP: poll `GET http://localhost:8001/{customerDeclineId}/{orderId}` until status is `Cancelled`. Then `GET http://localhost:8005/9002` should show `TotalReserved = 0` again.

SQL:

```sql
SELECT OrderId, Status FROM [Order].dbo.Orders WHERE OrderId = '<orderId>';
SELECT PaymentId, OrderId, Status, FailureReason FROM Payment.dbo.Payments WHERE OrderId = '<orderId>';
SELECT * FROM Inventory.dbo.StockReservations WHERE OrderId = '<orderId>';
SELECT ProductId, TotalOnHand, TotalReserved FROM Inventory.dbo.StockItems WHERE ProductId = 9002;
SELECT * FROM Shipping.dbo.Shipments WHERE OrderId = '<orderId>';
```

Event/log: observe `StockReservedEvent`, `PaymentFailedEvent`, and `OrderCancelledEvent`. Inventory should release the reservation and `TotalReserved` returns to 0. No shipment should be created.

Jaeger: the trace should show Inventory reserve → Payment authorize-fail → Order cancel → Inventory release. No Shipping spans.
