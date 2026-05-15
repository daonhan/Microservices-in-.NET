# Local Messaging Development

This is the canonical local-dev entry point for choosing the messaging provider. RabbitMQ is the default for `docker compose up`, `dotnet run`, and normal tests. Azure Service Bus is opt-in through `Messaging__Provider=AzureServiceBus`.

Use [ASB Emulator Local Profile](../qa/asb-emulator-local.md) for the emulator-only health check, opt-in adapter test, DLQ verification, and teardown procedure.

## Choose A Scenario

| Scenario | Use it when | Trade-off |
|---|---|---|
| Default Compose Rabbit | You want the full stack running with the default local broker. | Fastest path and matches local smoke expectations, but does not exercise the ASB adapter. |
| F5 + ASB emulator | You want to debug one or more services against ASB without an Azure subscription. | Offline and quick feedback, but emulator coverage is not full cloud Service Bus fidelity. |
| F5 + shared dev namespace | You need real ASB behavior, managed identities, or cloud networking behavior. | Highest fidelity, but it costs money, requires secrets, and topology should already exist. |
| Compose `--profile asb` | You want service containers to talk to the local emulator. | Closer to containerized local flow, but heavier than F5 and still requires explicit provider overrides. |

Queue names should match each service's `EventBus:QueueName` value. Current subscriber queue names are:

| Service | `EventBus__QueueName` |
|---|---|
| Basket | `basket-microservice` |
| Order | `order-microservice` |
| Inventory | `inventory-microservice` |
| Payment | `payment-microservice` |
| Shipping | `shipping-microservice` |

Product uses `product-microservice` when a queue name is needed for provider boot checks. Auth does not consume integration events.

## Scenario 1: Default Compose Rabbit

Choose this for day-to-day local development and the clean-clone smoke path.

```powershell
docker compose up --build
```

Provider settings are already supplied by committed `appsettings.json` defaults and Compose environment values:

```text
Messaging__Provider=RabbitMq
RabbitMq__HostName=host.docker.internal
EventBus__QueueName=<service queue name, for example order-microservice>
```

Do not set these ASB values for the Rabbit path:

```text
AzureServiceBus__ConnectionString=
AzureServiceBus__AdministrationConnectionString=
AzureServiceBus__TopicName=
AzureServiceBus__AutoProvisionTopology=
```

Minimum smoke from a clean clone:

```powershell
docker compose ps
Invoke-WebRequest http://localhost:8004/health/live -UseBasicParsing
Invoke-WebRequest http://localhost:8004/health/ready -UseBasicParsing
Invoke-WebRequest http://localhost:15672 -UseBasicParsing
```

Then open `http://localhost:8004/swagger` for the gateway-routed API surface and `http://localhost:15672` for RabbitMQ Management (`guest` / `guest`).

## Scenario 2: F5 + ASB Emulator

Choose this when you want to debug a service from the host while using the local ASB emulator.

Start the emulator infrastructure:

```powershell
docker compose --profile asb up -d servicebus-emulator servicebus-sql
```

Use these environment variables for each host-run service:

```text
Messaging__Provider=AzureServiceBus
AzureServiceBus__ConnectionString=Endpoint=sb://localhost:5673;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;
AzureServiceBus__AdministrationConnectionString=Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;
AzureServiceBus__TopicName=ecommerce-topic
AzureServiceBus__AutoProvisionTopology=Auto
EventBus__QueueName=order-microservice
```

For F5, the same values can live in a local-only `appsettings.Development.json` override:

```json
{
  "Messaging": {
    "Provider": "AzureServiceBus"
  },
  "AzureServiceBus": {
    "ConnectionString": "Endpoint=sb://localhost:5673;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;",
    "AdministrationConnectionString": "Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;",
    "TopicName": "ecommerce-topic",
    "AutoProvisionTopology": "Auto"
  },
  "EventBus": {
    "QueueName": "order-microservice"
  }
}
```

Replace `order-microservice` with the service you are debugging. `Auto` provisions the topic and that service's subscription only when the connection string contains `UseDevelopmentEmulator=true`.

The emulator QA guide has the host-port notes, health check, opt-in ASB adapter test, DLQ verification, and teardown: [docs/qa/asb-emulator-local.md](../qa/asb-emulator-local.md).

## Scenario 3: F5 + Shared Dev Namespace

Choose this when emulator behavior is not enough and you need a real Azure Service Bus namespace. Use repository or user secrets for actual connection strings; do not commit them.

Use these environment variables for each host-run service:

```text
Messaging__Provider=AzureServiceBus
AzureServiceBus__ConnectionString=Endpoint=sb://<namespace>.servicebus.windows.net/;SharedAccessKeyName=<data-key-name>;SharedAccessKey=<data-key>
AzureServiceBus__AdministrationConnectionString=Endpoint=sb://<namespace>.servicebus.windows.net/;SharedAccessKeyName=<manage-key-name>;SharedAccessKey=<manage-key>
AzureServiceBus__TopicName=ecommerce-topic
AzureServiceBus__AutoProvisionTopology=Never
EventBus__QueueName=order-microservice
```

Use this `appsettings.Development.json` shape only with local secret replacement:

```json
{
  "Messaging": {
    "Provider": "AzureServiceBus"
  },
  "AzureServiceBus": {
    "ConnectionString": "<secret data-plane connection string>",
    "AdministrationConnectionString": "<secret management connection string>",
    "TopicName": "ecommerce-topic",
    "AutoProvisionTopology": "Never"
  },
  "EventBus": {
    "QueueName": "order-microservice"
  }
}
```

`Never` is the safest default for a shared namespace because Azure topology is owned by infrastructure deployment. Use `Always` only for an explicit topology check against a namespace you are allowed to mutate. With `Auto`, non-emulator connection strings skip topology provisioning.

## Scenario 4: Compose `--profile asb`

Choose this when service containers, not host-run services, should talk to the ASB emulator. The profile starts the emulator and its SQL sidecar, but it does not switch application services away from RabbitMQ by itself.

Start the emulator beside the default stack:

```powershell
docker compose --profile asb up -d
```

For a temporary ASB container run, add provider overrides only to the service containers you are testing:

```yaml
services:
  order:
    environment:
      - "Messaging__Provider=AzureServiceBus"
      - "AzureServiceBus__ConnectionString=Endpoint=sb://servicebus-emulator;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
      - "AzureServiceBus__AdministrationConnectionString=Endpoint=sb://servicebus-emulator:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
      - "AzureServiceBus__TopicName=ecommerce-topic"
      - "AzureServiceBus__AutoProvisionTopology=Auto"
      - "EventBus__QueueName=order-microservice"
```

Inside the Compose network, `AzureServiceBus__ConnectionString` uses `servicebus-emulator` without a port because the emulator listens on AMQP `5672` in the container. The administration connection uses `servicebus-emulator:5300` for topology checks.

Keep these overrides local to the run. The committed default Compose environment stays RabbitMQ-first.

## Verify the saga

Run this checklist after any of the four scenarios above. The HTTP flow is the same for RabbitMQ and Azure Service Bus because the broker switch is below the service API. Start from a clean QA dataset when you need deterministic product, stock, and payment outcomes:

```powershell
docker compose down -v
docker compose up --build
```

Use the gateway when the full stack is running:

```powershell
$base = "http://localhost:8004"
$password = "oKNrqkO7iC#G"
$customerHappyId = "5ff2d67e-c6b5-4870-911f-79393ed416fd"
$customerCancelId = "00faac97-9ae4-4b7f-b8aa-00e7c569dd66"

$happyToken = (Invoke-RestMethod "$base/login" -Method Post -ContentType "application/json" `
  -Body (@{ username = "customer-happy@qa.test"; password = $password } | ConvertTo-Json)).token

$cancelToken = (Invoke-RestMethod "$base/login" -Method Post -ContentType "application/json" `
  -Body (@{ username = "customer-cancel@qa.test"; password = $password } | ConvertTo-Json)).token

$adminToken = (Invoke-RestMethod "$base/login" -Method Post -ContentType "application/json" `
  -Body (@{ username = "microservices@daonhan.com"; password = $password } | ConvertTo-Json)).token
```

If you are running a subset of services with F5, use that service's direct port instead of the gateway prefix: order `8001`, inventory `8005`, shipping `8006`, payment `8007`, and auth `8003`.

### RabbitMQ checklist

| Step | HTTP action | Expected event | Inspect |
|---|---|---|---|
| 1. Place the happy-path order | `POST /order/{customerHappyId}` with `{"orderProducts":[{"productId":"9001","quantity":2}]}` and the happy customer token. Capture the order id from the `Location` header. | `OrderCreatedEvent` | `[Order].dbo.Orders` has the order, `[Order].dbo.OrderProducts` has product `9001`, and RabbitMQ Management shows traffic through `ecommerce-exchange` to subscriber queues. |
| 2. Confirm inventory reservation | Poll the order or inspect Inventory after the order is placed. | `StockReservedEvent` | `Inventory.dbo.StockReservations` has held rows for the order, `Inventory.dbo.StockItems.TotalReserved` increased for product `9001`, and Inventory logs or outbox rows mention `StockReservedEvent`. |
| 3. Confirm payment authorization | Poll `GET /payment/by-order/{orderId}` with the happy customer token. | `PaymentAuthorizedEvent` | `Payment.dbo.Payments` has status `Authorized`, a provider reference starting with `INMEM-`, and Payment logs or outbox rows mention `PaymentAuthorizedEvent`. |
| 4. Confirm the order | Poll `GET /order/{customerHappyId}/{orderId}` with the happy customer token until `status` is `Confirmed`. | `OrderConfirmedEvent` | `[Order].dbo.Orders.Status` is `Confirmed`, and Order logs or outbox rows mention `OrderConfirmedEvent`. |
| 5. Confirm stock commit | Inspect Inventory after the order reaches `Confirmed`. | `StockCommittedEvent` | `Inventory.dbo.StockReservations` rows move to committed, on-hand stock is reduced for product `9001`, and Inventory logs or outbox rows mention `StockCommittedEvent`. |
| 6. Confirm shipment creation | Poll `GET /shipping/by-order/{orderId}` with the happy customer token until a shipment is returned. | `ShipmentCreatedEvent` | `Shipping.dbo.Shipments` has at least one row for the order, status starts at `Pending`, and Shipping logs or outbox rows mention `ShipmentCreatedEvent`. |
| 7. Drive capture | `POST /shipping/{shipmentId}/dispatch` with the admin token, carrier `fake-ground`, and a shipping address. | `PaymentCapturedEvent` or `PaymentFailedEvent` | `Payment.dbo.Payments.Status` becomes `Captured` for the happy path. If the payment branch fails, Order should emit `OrderCancelledEvent` and no new shipment should proceed. |

Dispatch body:

```json
{
  "carrierKey": "fake-ground",
  "shippingAddress": {
    "recipient": "QA Customer",
    "line1": "1 Local Dev Way",
    "line2": null,
    "city": "Austin",
    "state": "TX",
    "postalCode": "78701",
    "country": "US"
  },
  "overrideQuote": null
}
```

Compensation walkthrough:

| Step | HTTP action | Expected event | Inspect |
|---|---|---|---|
| 1. Place the zero-stock order | `POST /order/{customerCancelId}` with `{"orderProducts":[{"productId":"9003","quantity":1}]}` and the cancel customer token. Capture the order id from the `Location` header. | `OrderCreatedEvent` | `[Order].dbo.Orders` has the order, product `9003` has `TotalOnHand = 0`, and RabbitMQ Management shows the event routed to Inventory. |
| 2. Confirm reservation failure | Inspect Inventory after the order is placed. | `StockReservationFailedEvent` | `Inventory.dbo.StockReservations` has no held reservation for the order, `Inventory.dbo.StockItems.TotalReserved` remains `0` for product `9003`, and Inventory logs or outbox rows mention `StockReservationFailedEvent`. |
| 3. Confirm cancellation | Poll `GET /order/{customerCancelId}/{orderId}` with the cancel customer token until `status` is `Cancelled`. | `OrderCancelledEvent` | `[Order].dbo.Orders.Status` is `Cancelled`, Order logs or outbox rows mention `OrderCancelledEvent`, and `Shipping.dbo.Shipments` has no row for the order. |

### Azure Service Bus checklist

| Step | HTTP action | Expected event | Inspect |
|---|---|---|---|
| 1. Place the happy-path order | `POST /order/{customerHappyId}` with `{"orderProducts":[{"productId":"9001","quantity":2}]}` and the happy customer token. Capture the order id from the `Location` header. | `OrderCreatedEvent` | `[Order].dbo.Orders` has the order, `[Order].dbo.OrderProducts` has product `9001`, and the `ecommerce-topic` subscriptions for `inventory-microservice`, `payment-microservice`, and `basket-microservice` receive the message. |
| 2. Confirm inventory reservation | Poll the order or inspect Inventory after the order is placed. | `StockReservedEvent` | `Inventory.dbo.StockReservations` has held rows for the order, `Inventory.dbo.StockItems.TotalReserved` increased for product `9001`, and Inventory logs or outbox rows mention `StockReservedEvent`. |
| 3. Confirm payment authorization | Poll `GET /payment/by-order/{orderId}` with the happy customer token. | `PaymentAuthorizedEvent` | `Payment.dbo.Payments` has status `Authorized`, a provider reference starting with `INMEM-`, and Payment logs or outbox rows mention `PaymentAuthorizedEvent`. |
| 4. Confirm the order | Poll `GET /order/{customerHappyId}/{orderId}` with the happy customer token until `status` is `Confirmed`. | `OrderConfirmedEvent` | `[Order].dbo.Orders.Status` is `Confirmed`, and Order logs or outbox rows mention `OrderConfirmedEvent`. |
| 5. Confirm stock commit | Inspect Inventory after the order reaches `Confirmed`. | `StockCommittedEvent` | `Inventory.dbo.StockReservations` rows move to committed, on-hand stock is reduced for product `9001`, and Inventory logs or outbox rows mention `StockCommittedEvent`. |
| 6. Confirm shipment creation | Poll `GET /shipping/by-order/{orderId}` with the happy customer token until a shipment is returned. | `ShipmentCreatedEvent` | `Shipping.dbo.Shipments` has at least one row for the order, status starts at `Pending`, and Shipping logs or outbox rows mention `ShipmentCreatedEvent`. |
| 7. Drive capture | `POST /shipping/{shipmentId}/dispatch` with the admin token, carrier `fake-ground`, and a shipping address. | `PaymentCapturedEvent` or `PaymentFailedEvent` | `Payment.dbo.Payments.Status` becomes `Captured` for the happy path. If the payment branch fails, Order should emit `OrderCancelledEvent` and no new shipment should proceed. |

Compensation walkthrough:

| Step | HTTP action | Expected event | Inspect |
|---|---|---|---|
| 1. Place the zero-stock order | `POST /order/{customerCancelId}` with `{"orderProducts":[{"productId":"9003","quantity":1}]}` and the cancel customer token. Capture the order id from the `Location` header. | `OrderCreatedEvent` | `[Order].dbo.Orders` has the order, product `9003` has `TotalOnHand = 0`, and the `ecommerce-topic` subscription for `inventory-microservice` receives the message. |
| 2. Confirm reservation failure | Inspect Inventory after the order is placed. | `StockReservationFailedEvent` | `Inventory.dbo.StockReservations` has no held reservation for the order, `Inventory.dbo.StockItems.TotalReserved` remains `0` for product `9003`, and Inventory logs or outbox rows mention `StockReservationFailedEvent`. |
| 3. Confirm cancellation | Poll `GET /order/{customerCancelId}/{orderId}` with the cancel customer token until `status` is `Cancelled`. | `OrderCancelledEvent` | `[Order].dbo.Orders.Status` is `Cancelled`, Order logs or outbox rows mention `OrderCancelledEvent`, and `Shipping.dbo.Shipments` has no row for the order. |

### Failure capture signals

The compensation walkthrough is a successful business path, so it should not create DLQ rows. If a handler fails after the broker accepts a message, confirm the failure was captured through metrics or the operator API:

```powershell
Invoke-WebRequest "$base/metrics" -UseBasicParsing |
  Select-String "dlq_messages_total|dlq_replays_total"

Invoke-RestMethod "$base/operator/api/failures?status=Pending" `
  -Headers @{ Authorization = "Bearer <operator-token>" }
```

`dlq_messages_total` increasing means the broker failure was persisted into `dead_letter_messages`. `dlq_replays_total` increasing after `POST /operator/api/failures/{id}/replay` confirms an operator replay attempt. The operator API requires a Bearer token with the `Operator` claim.

## Troubleshooting

| Symptom | Likely cause | Resolution |
|---|---|---|
| `servicebus-emulator` or `servicebus-sql` exits immediately, or logs mention EULA / SQL license acceptance. | The ASB emulator and its SQL sidecar require acceptance env vars. A local override removed `ACCEPT_EULA=Y`, or the SQL password override is invalid. | Use the committed `docker compose --profile asb up -d servicebus-emulator servicebus-sql` shape, keep `ACCEPT_EULA=Y` on both containers, and set a strong `ASB_EMULATOR_SQL_PASSWORD` only if you also use it consistently for both services. |
| Services start with `AzureServiceBus__AutoProvisionTopology=Never`, but messages never reach subscribers or logs mention a missing topic/subscription. | `Never` disables topology creation. The `ecommerce-topic` or service subscriptions do not exist in the emulator or shared namespace. | For emulator work, use `AzureServiceBus__AutoProvisionTopology=Auto`. For shared Azure namespaces, keep `Never` and have Bicep or an operator create `ecommerce-topic` plus subscriptions named for each `EventBus__QueueName`. |
| ASB startup fails with connection refused, authentication errors, or a real namespace is contacted when you expected the emulator. | The connection string points at the wrong target for the run mode: host-run F5, Compose network, emulator, and cloud each use a different host shape. | Host-run emulator uses `Endpoint=sb://localhost:5673;...;UseDevelopmentEmulator=true;`. Compose containers use `Endpoint=sb://servicebus-emulator;...;UseDevelopmentEmulator=true;`. Cloud namespaces use `Endpoint=sb://<namespace>.servicebus.windows.net/;...` without `UseDevelopmentEmulator=true`. |
| The service fails fast before listening and logs `Unknown messaging provider`. | `Messaging__Provider` has a typo or unsupported value. | Set `Messaging__Provider=RabbitMq` or `Messaging__Provider=AzureServiceBus`. The resolver is intentionally strict so a misspelled provider does not silently fall back to RabbitMQ. |
| RabbitMQ or the ASB emulator cannot bind AMQP port `5672`, or only one broker is reachable. | RabbitMQ exposes host port `5672`. The ASB emulator listens on container port `5672` but should map to host `5673` by default. A local `ASB_EMULATOR_AMQP_PORT=5672` override or another broker process caused the collision. | Keep the default ASB host mapping at `5673`, stop the other local broker, or set `ASB_EMULATOR_AMQP_PORT` to an unused host port and update the host-run emulator connection string. |
| A saga message disappears from the happy path and the order stops progressing. | A consumer exhausted retries and the message was dead-lettered, or an outbox row failed after max publish attempts. | Check `GET /metrics` for `dlq_messages_total`, then query `GET /operator/api/failures?status=Pending&eventType=<EventName>` with an `Operator` token. Inspect the stored payload, stack trace, `originalQueue`, and `correlationId` before using the replay endpoint. |
