# Scenario 01: Happy Path

Start from a clean stack:

```powershell
docker compose down -v
docker compose up --build
```

Use the Bruno collection in `qa/bruno/01-happy-path` with the `qa-local` environment. Keep the customer token and admin token from the login steps.

## 1. Login as customer-happy

HTTP: `POST http://localhost:8003/login` returns `200` with a `token` for `customer-happy@qa.test`.

SQL:

```sql
SELECT Id, Username, Role FROM Auth.dbo.Users WHERE Username = 'customer-happy@qa.test';
```

Event/log: Auth logs a `login-success` metric.

Jaeger: find an Auth span for `POST /login`.

## 2. Confirm seeded basket, product, and stock

HTTP: `GET /{customerHappyId}` on Basket returns one `product-happy` line; Product `GET /9001` returns price `10.00`; Inventory `GET /9001` returns available stock greater than the basket quantity.

SQL:

```sql
SELECT Id, Name, Price FROM Product.dbo.Products WHERE Id = 9001;
SELECT ProductId, TotalOnHand, TotalReserved FROM Inventory.dbo.StockItems WHERE ProductId = 9001;
SELECT ProductId, WarehouseId, OnHand, Reserved FROM Inventory.dbo.StockLevels WHERE ProductId = 9001;
```

Event/log: Basket startup logs should show no Redis seeder failure; Product and Inventory migrations should include `SeedQaData` in applied migrations.

Jaeger: find spans for `GET /9001` on Product and Inventory.

## 3. Place the order

HTTP: `POST http://localhost:8001/{customerHappyId}` with product `9001`, quantity `2`, returns `201` and a location containing the order id.

SQL:

```sql
SELECT OrderId, CustomerId, Status FROM [Order].dbo.Orders WHERE CustomerId = '5ff2d67e-c6b5-4870-911f-79393ed416fd';
SELECT OrderId, ProductId, Quantity FROM [Order].dbo.OrderProducts WHERE ProductId = '9001';
```

Event/log: RabbitMQ should receive `OrderCreatedEvent`; Order outbox should publish it.

Jaeger: follow the trace from Order to Inventory and Payment.

## 4. Wait for confirmation and shipment creation

HTTP: poll `GET http://localhost:8001/{customerHappyId}/{orderId}` until status is `Confirmed`; then `GET http://localhost:8006/by-order/{orderId}` returns at least one shipment.

SQL:

```sql
SELECT OrderId, Status FROM [Order].dbo.Orders WHERE OrderId = '<orderId>';
SELECT OrderId, Status FROM Shipping.dbo.Shipments WHERE OrderId = '<orderId>';
```

Event/log: observe `StockReservedEvent`, `PaymentAuthorizedEvent`, `OrderConfirmedEvent`, and `StockCommittedEvent`.

Jaeger: the order trace should show Inventory reservation, Payment authorization, and Shipping creation work.

## 5. Pick, pack, dispatch, and deliver

HTTP: use the admin token to call `POST /{shipmentId}/pick`, `POST /{shipmentId}/pack`, `POST /{shipmentId}/dispatch`, and `POST /{shipmentId}/deliver` on Shipping. Final response status is `Delivered`.

SQL:

```sql
SELECT Id, OrderId, Status, CarrierKey, TrackingNumber FROM Shipping.dbo.Shipments WHERE Id = '<shipmentId>';
SELECT ShipmentId, Status, Source FROM Shipping.dbo.ShipmentStatusHistory WHERE ShipmentId = '<shipmentId>' ORDER BY OccurredAt;
```

Event/log: observe `ShipmentDispatchedEvent`, `ShipmentDeliveredEvent`, and `ShipmentStatusChangedEvent` milestones.

Jaeger: Shipping spans should include pick, pack, dispatch, and deliver requests.
