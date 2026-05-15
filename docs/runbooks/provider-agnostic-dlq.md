# Provider-Agnostic DLQ Capture and Replay

The API Gateway is the operator-facing surface for failed broker messages and failed outbox rows. The route contract and `dead_letter_messages` schema are intentionally broker-agnostic: operators use the same `/operator/api/failures*` endpoints whether `Messaging:Provider` is `RabbitMq` or `AzureServiceBus`.

## Operator Contract

| Method | Path | Behavior |
|---|---|---|
| `GET` | `/operator/api/failures` | Lists captured broker dead letters and failed outbox rows. |
| `GET` | `/operator/api/failures/{id}` | Returns payload, failure metadata, correlation id, and optional trace URL. |
| `POST` | `/operator/api/failures/{id}/replay` | Re-publishes a pending failure through the configured provider. |
| `POST` | `/operator/api/failures/{id}/discard` | Marks a pending failure discarded with an operator reason. |
| `POST` | `/operator/api/failures/replay-batch` | Replays many pending failures and reports per-id outcomes. |

`DeadLetterMessage` and `dead_letter_messages` are unchanged by the provider switch. Provider is an observability tag on `dlq_messages_total`, `dlq_replays_total`, and `dlq_discards_total`; it is not a persisted column.

## Provider Behavior

| Provider | Capture source | Replay target |
|---|---|---|
| `RabbitMq` | Shared DLQ queue bound to the platform dead-letter exchange. | The original queue via the default exchange. |
| `AzureServiceBus` | Dead-letter subqueue for each configured topic subscription. | The configured topic with `Subject` set to the stored event type. |

`Messaging:Provider` selects exactly one capture implementation and one replay publisher at gateway startup. Missing or blank values default to RabbitMQ; unknown values fail fast.

## ASB Capture Subscriptions

The gateway must poll only subscriptions that are owned by services in the topology. The configured values match each service's `EventBus:QueueName`:

| Subscription | Service role |
|---|---|
| `basket-microservice` | Basket subscriber |
| `order-microservice` | Order saga subscriber |
| `inventory-microservice` | Inventory saga subscriber |
| `payment-microservice` | Payment saga subscriber |
| `shipping-microservice` | Shipping saga subscriber |

Publisher-only and non-messaging services are intentionally absent. Product publishes catalog events but does not own a subscriber DLQ in this topology. Auth does not participate in broker messaging.

Gateway defaults live in `api-gateway/ApiGateway/appsettings.json` under `AzureServiceBus:DeadLetterCaptureSubscriptions`. Override with indexed environment variables only when the service topology changes, for example:

```powershell
$env:AzureServiceBus__DeadLetterCaptureSubscriptions__0 = "basket-microservice"
$env:AzureServiceBus__DeadLetterCaptureSubscriptions__1 = "order-microservice"
```

## Local ASB Emulator Behavior

When `Messaging:Provider=AzureServiceBus`, the gateway creates one dead-letter subqueue processor per configured subscription. If the emulator, namespace, topic, or one subscription is unavailable, startup logs a warning for that subscription and the gateway keeps the operator API alive. Already-started processors keep running; a broken subscription does not disable the others.

This is different from store failures after a message is received. If `IDeadLetterStore.CaptureAsync` fails, the ASB subqueue message is abandoned so the broker can redeliver it; it is not silently completed.

Use [docs/qa/asb-emulator-local.md](../qa/asb-emulator-local.md) for emulator connection strings, topology provisioning, and opt-in verification.

## Verification

Run the shared and gateway suites after changing this flow:

```powershell
cd shared-libs
dotnet test

cd ..\api-gateway
dotnet test
```

RabbitMQ DLQ integration coverage is under the existing Docker/Testcontainers gate (`DlqMetricAttributionTests`, `RabbitMqDeadLetterIntegrationTests`). ASB emulator verification is optional and remains opt-in via `ASB_EMULATOR_TESTS=true`.
