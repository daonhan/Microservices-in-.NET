# Payment smoke runbook

> Run end-to-end manual checks for Payment saga.
> Phase 4 (`.99` decline) is mandatory smoke. `.00` happy + refund optional.

## Prereqs

- Docker Desktop running.
- Repo root: `D:/Preparing/Microservices in .NET/Nhamnhi`.
- Tools: `curl`, `jq` (optional but easier).
- Ports free: 8000–8007, 1433, 5672, 6379, 15672, 16686, 9090, 3000.

## Stack up

```bash
cd "D:/Preparing/Microservices in .NET/Nhamnhi"
docker compose up --build -d
```

Wait ~60s. Verify Payment ready:

```bash
curl http://localhost:8004/payment/health/ready
```

If existing Auth DB → wipe row (admin email rebrand to `daonhan.com`):

```sql
UPDATE Auth.dbo.Users SET Username = 'microservices@daonhan.com'
WHERE Id = 'd854813c-4a72-4afd-b431-878cba3ecf2a';
```

Or fresh sql container:

```bash
docker compose down
docker volume prune -f
docker compose up --build -d
```

## Admin JWT

Seeded admin: `microservices@daonhan.com` / `oKNrqkO7iC#G`.

```bash
TOKEN=$(curl -s -X POST http://localhost:8004/login \
  -H "Content-Type: application/json" \
  -d '{"Username":"microservices@daonhan.com","Password":"oKNrqkO7iC#G"}' \
  | jq -r '.token')
echo "$TOKEN"
```

No `jq`? Hand-pick `token` from response, set `TOKEN=...`.

## Decline rule (in-memory gateway)

`payment-microservice/Payment.Service/Infrastructure/Gateways/InMemoryPaymentGateway.cs`

| cents | outcome |
|-------|---------|
| `99`  | decline |
| any other | success |

So total `9.99` → fail. Total `9.00` → succeed.

---

## Path A — `.99` decline smoke (Phase 4)

Goal: Order=Cancelled, Payment=Failed, Inventory reserved=0.

### A1. Create product priced 9.99

```bash
curl -X POST http://localhost:8004/product/ \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"Name":"Decline Shoe","Price":9.99,"ProductTypeId":1,"Description":"smoke .99"}'
```

Response = product id. `PID=<id>`.

### A2. Restock (default zero stock)

```bash
curl -X POST http://localhost:8004/inventory/$PID/restock \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"Quantity":10}'
```

Confirm:

```bash
curl http://localhost:8004/inventory/$PID
# totalOnHand: 10, totalReserved: 0
```

### A3. Basket for fake customer

```bash
CUST="cust-decline-smoke"
curl -X POST http://localhost:8004/basket/$CUST \
  -H "Content-Type: application/json" \
  -d "{\"ProductId\":\"$PID\",\"ProductName\":\"Decline Shoe\"}"
```

### A4. Place order (qty 1, total 9.99)

```bash
curl -i -X POST http://localhost:8004/order/$CUST \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"OrderProducts\":[{\"ProductId\":\"$PID\",\"Quantity\":1}]}"
```

Grab `OrderId` from `Location` header (`{customerId}/{orderId}`). `OID=<orderId>`.

### A5. Wait ~5s. Saga path:

`OrderCreated → StockReserved → Payment.Authorize→decline → PaymentFailed → Order.Cancel → OrderCancelled → Payment.Void + Inventory.Release`

### A6. Verify three terminal facts

```bash
# Payment failed
curl http://localhost:8004/payment/by-order/$OID -H "Authorization: Bearer $TOKEN"
# expect "status":"Failed", amount 9.99

# Order cancelled
curl http://localhost:8004/order/$CUST/$OID -H "Authorization: Bearer $TOKEN"
# expect "status":"Cancelled"

# Stock released
curl http://localhost:8004/inventory/$PID
# expect totalReserved: 0, totalOnHand: 10
```

All three green = Phase 4 verified.

---

## Path B — `.00` happy smoke (Phase 3 + 5)

Goal: Order=Confirmed → Payment=Authorized → ship dispatch → Payment=Captured.

### B1. Product priced 10.00

```bash
curl -X POST http://localhost:8004/product/ \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"Name":"Happy Shoe","Price":10.00,"ProductTypeId":1}'
# PID2=<id>
```

### B2. Restock + basket + order

```bash
curl -X POST http://localhost:8004/inventory/$PID2/restock \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"Quantity":10}'

CUST2="cust-happy-smoke"
curl -X POST http://localhost:8004/basket/$CUST2 \
  -H "Content-Type: application/json" \
  -d "{\"ProductId\":\"$PID2\",\"ProductName\":\"Happy Shoe\"}"

curl -i -X POST http://localhost:8004/order/$CUST2 \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d "{\"OrderProducts\":[{\"ProductId\":\"$PID2\",\"Quantity\":1}]}"
# OID2=<from Location>
```

### B3. Verify Authorized + Confirmed

```bash
curl http://localhost:8004/payment/by-order/$OID2 -H "Authorization: Bearer $TOKEN"
# expect "status":"Authorized"

curl http://localhost:8004/order/$CUST2/$OID2 -H "Authorization: Bearer $TOKEN"
# expect "status":"Confirmed"
```

### B4. Dispatch shipment → Capture

Get shipment id by order:

```bash
curl http://localhost:8004/shipping/by-order/$OID2 -H "Authorization: Bearer $TOKEN"
# SID=<shipmentId>
```

Pick → pack → dispatch (admin):

```bash
curl -X POST http://localhost:8004/shipping/$SID/pick -H "Authorization: Bearer $TOKEN"
curl -X POST http://localhost:8004/shipping/$SID/pack -H "Authorization: Bearer $TOKEN"
curl -X POST http://localhost:8004/shipping/$SID/dispatch -H "Authorization: Bearer $TOKEN"
```

Wait ~3s.

```bash
curl http://localhost:8004/payment/by-order/$OID2 -H "Authorization: Bearer $TOKEN"
# expect "status":"Captured"
```

---

## Path C — Refund smoke (Phase 7)

Need captured payment from Path B. `PAY_ID=<paymentId from B>`.

```bash
curl -X POST http://localhost:8004/payment/$PAY_ID/refund \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{}'
# expect 200

curl http://localhost:8004/payment/by-order/$OID2 -H "Authorization: Bearer $TOKEN"
# expect "status":"Refunded"
```

---

## Observability spots

| What | URL |
|------|-----|
| RabbitMQ UI | http://localhost:15672 (guest/guest) |
| Jaeger | http://localhost:16686 |
| Prometheus | http://localhost:9090 |
| Grafana | http://localhost:3000 |
| Combined Swagger | http://localhost:8004/swagger |

Useful Prometheus queries:

```
payments_total
payment_authorize_latency_ms_bucket
```

Useful Jaeger filter: service=`payment`, operation=`StockReservedEventHandler.Handle`.

---

## Common traps

- **`curl` returns 404 on `/payment/by-order/{id}` first try** — `OrderCreatedEvent` race. Handler no-ops until OrderCreated observed (`StockReservedEventHandler.cs:39-45`). RabbitMQ redeliver. Wait + retry.
- **`Product price not found in cache`** — Basket Redis cache not yet hydrated by `ProductCreatedEvent`. Wait few sec after product POST before basket POST.
- **Stock reserve fail** — forgot restock. Default OnHand=0.
- **401 on POST /product or /inventory restock** — token expired or wrong env. Re-login.
- **Email login 401** — Auth DB still has old `code-maze.com` row. Run UPDATE sql or wipe sql volume.

## Cleanup

```bash
docker compose down
# nuke all volumes (fresh start next time):
docker compose down -v
```
