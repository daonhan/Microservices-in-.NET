# Plan: Provider-agnostic dead-letter capture and replay

> Source PRD: `docs/prd/PRD-Messaging-DLQ-Provider-Abstraction.md`  
> GitHub issue: https://github.com/daonhan/Microservices-in-.NET/issues/81  
> Depends on: `docs/plans/messaging-adopt-addplatform.md`  
> Benefits from: `docs/plans/messaging-asb-emulator-local.md`

## Architectural decisions

Durable decisions that apply across all phases:

- **Provider switch**: `Messaging:Provider` selects the dead-letter capture and replay publisher. Missing or blank values keep RabbitMQ as the default; unknown values continue to fail fast.
- **Single active capture path**: exactly one dead-letter capture implementation is active at runtime. RabbitMQ and Azure Service Bus capture must never run together in the same gateway process.
- **Operator API contract**: `/operator/api/failures*` routes, response shapes, authorization, replay semantics, batch replay semantics, and discard semantics stay unchanged.
- **Store schema**: `dead_letter_messages` and `DeadLetterMessage` stay unchanged. Provider is an observability dimension, not a persisted column in this PRD.
- **Replay contract**: `DeadLetterReplayer` continues to depend on `IDeadLetterPublisher`; replay requests still carry `OriginalQueue`, `EventType`, `Payload`, `CorrelationId`, and `FailureId`.
- **RabbitMQ behavior**: current RabbitMQ DLX capture and default-exchange replay behavior remain the baseline regression path.
- **Azure Service Bus topology**: ASB capture reads dead-letter subqueues for the topic subscriptions that are actually part of the service topology. The durable subscription names are the same values used by subscriber services in `EventBus:QueueName`, such as `basket-microservice`, `order-microservice`, `inventory-microservice`, `payment-microservice`, and `shipping-microservice`.
- **Publisher-only services**: publisher-only services do not create ASB DLQ capture processors unless they also own a topic subscription.
- **Header normalization**: ASB failure metadata is normalized from `ApplicationProperties` using stable keys: `original_queue`, `event_type`, `service`, `failure_reason`, `attempts`, `failed_at`, `correlation_id`, and `stack_trace` when available.
- **Correlation**: `dlq.replay` traces continue to tag the original correlation id for RabbitMQ and ASB replay.
- **Metrics**: `dlq_messages_total`, `dlq_replays_total`, and `dlq_discards_total` all include a `provider` tag while preserving existing `service`, `event_type`, and `outcome` tags.
- **Local ASB emulator resilience**: when the gateway is configured for ASB but the emulator or a configured subscription DLQ is unavailable, the operator API should still start. Capture should log and retry or no-op cleanly instead of crashing the gateway.
- **Shared package propagation**: changes in `ECommerce.Shared` require a package version bump, local pack/push, and consumer package updates so the gateway and services actually consume the new DLQ behavior.

---

## Phase 1: Provider-selected DLQ Shell

**User stories**: 6, 7, 8

### What to build

Introduce the dead-letter provider boundary without changing broker behavior. `AddDeadLetter` should resolve the same `Messaging:Provider` value used by platform messaging and register one capture implementation plus one replay publisher implementation. RabbitMQ remains the default path, and the ASB path can initially be a safe no-op publisher/capture placeholder only long enough to prove registration and startup behavior.

### Acceptance criteria

- [ ] Missing or blank `Messaging:Provider` registers the RabbitMQ capture and RabbitMQ dead-letter publisher.
- [ ] `Messaging:Provider=RabbitMq` registers the RabbitMQ capture and RabbitMQ dead-letter publisher.
- [ ] `Messaging:Provider=AzureServiceBus` does not register RabbitMQ-only DLQ capture or RabbitMQ replay publisher dependencies.
- [ ] Unknown provider values fail during dead-letter service registration with the same valid provider names used by platform messaging.
- [ ] Gateway startup tests prove the operator module can register dead-letter services under both RabbitMQ and ASB provider settings.
- [ ] Tests prove only one capture implementation is registered for a provider.
- [ ] Existing RabbitMQ dead-letter replay, discard, origin filter, and operator endpoint tests keep passing.

---

## Phase 2: RabbitMQ Parity Behind the Abstraction

**User stories**: 1, 3, 4, 5, 8

### What to build

Move the existing RabbitMQ dead-letter capture path behind the provider abstraction and keep its external behavior identical. RabbitMQ should still consume the shared DLQ queue, normalize Rabbit-specific headers and `x-death` fallback data into `DeadLetterMessage`, persist via the existing store, ack only after persistence, and nack/requeue when persistence fails.

### Acceptance criteria

- [ ] A RabbitMQ dead-lettered message is captured into `dead_letter_messages` with the same event type, routing key, original queue, service, payload, failure reason, attempts, failed-at timestamp, and correlation id as before.
- [ ] RabbitMQ capture acks only after the store successfully persists the normalized message.
- [ ] RabbitMQ capture nacks with requeue when store persistence throws.
- [ ] RabbitMQ replay publishes to the original queue using the default exchange, preserving the current handler dispatch fallback through the event type header.
- [ ] `dlq_messages_total` increments for RabbitMQ capture with `provider=RabbitMq`, `service`, and `event_type` tags.
- [ ] `dlq_replays_total` and `dlq_discards_total` include `provider=RabbitMq` while preserving current outcome tags.
- [ ] Existing RabbitMQ integration tests and metric attribution tests pass after the class split.

---

## Phase 3: ASB Consumer Failure Metadata

**User stories**: 1, 4, 6, 9, 11

### What to build

Make the ASB subscriber failure path produce capturable dead-letter messages. When an event handler exhausts its retry budget, the ASB subscriber should dead-letter the message to its subscription DLQ with normalized metadata in `ApplicationProperties`. This is the producer-side half of ASB DLQ support and should be implemented before the gateway capture processor tries to read those messages.

### Acceptance criteria

- [ ] Handler failures on ASB are retried consistently with the configured platform retry policy before the message is dead-lettered.
- [ ] Dead-lettered ASB messages include `original_queue` set to the subscription name from `EventBus:QueueName`.
- [ ] Dead-lettered ASB messages include `event_type`, `service`, `failure_reason`, `attempts`, `failed_at`, and `correlation_id` when available.
- [ ] Failure reason and stack trace values are bounded so they cannot create oversized broker properties.
- [ ] Unknown or unhandled event subjects are handled according to the existing subscriber behavior and are not incorrectly stored as handler failures.
- [ ] Unit coverage proves ASB failure metadata is stamped without contacting Azure.
- [ ] Existing ASB publish/subscribe tests still pass for successful handler execution.

---

## Phase 4: ASB Capture Tracer

**User stories**: 1, 4, 5, 6, 11

### What to build

Build the first real ASB capture slice against one configured subscription. The gateway capture implementation should open a processor for that subscription's dead-letter subqueue, normalize a received message into the existing `DeadLetterMessage` shape, persist it, record metrics, and complete the subqueue message only after persistence succeeds.

### Acceptance criteria

- [ ] ASB capture reads from the configured topic and subscription dead-letter subqueue.
- [ ] A received ASB dead-letter message is persisted with `Origin=DeadLetter`.
- [ ] ASB `ApplicationProperties` are normalized into `DeadLetterMessage` without changing the store schema.
- [ ] Missing optional metadata falls back safely: event type from message subject, failed-at from capture time, attempts from delivery count or `1`, and correlation id from broker correlation id or a generated id.
- [ ] Capture completion happens only after `IDeadLetterStore.CaptureAsync` succeeds.
- [ ] Store failure does not silently complete the ASB subqueue message.
- [ ] `dlq_messages_total` increments with `provider=AzureServiceBus`, `service`, and `event_type`.
- [ ] Gateway startup under ASB does not crash when the ASB emulator is unavailable; it logs the capture unavailability and keeps the operator endpoints alive.
- [ ] Unit tests cover ASB message normalization using SDK factory or mock helpers.

---

## Phase 5: ASB Replay Publisher

**User stories**: 2, 3, 5, 10

### What to build

Add the ASB replay publisher behind `IDeadLetterPublisher`. Replaying a stored ASB failure should send the original payload back to the configured topic with `Subject` equal to the stored event type and correlation metadata tied to the original failure. The operator endpoint and batch replay flow should continue to call the existing replayer and should not branch by broker.

### Acceptance criteria

- [ ] `Messaging:Provider=AzureServiceBus` resolves the ASB dead-letter publisher.
- [ ] ASB replay sends to the configured ASB topic.
- [ ] ASB replay sets `Subject` to the stored `EventType`.
- [ ] ASB replay sets a new message id and preserves the original correlation id, falling back to the failure id when needed.
- [ ] ASB replay includes replay metadata such as the source failure id without changing `DeadLetterReplayRequest`.
- [ ] `/operator/api/failures/{id}/replay` returns the same status codes and response shape for ASB-backed replay as RabbitMQ-backed replay.
- [ ] `/operator/api/failures/replay-batch` reports per-id success, not found, conflict, and publish failure outcomes identically across providers.
- [ ] `dlq.replay` spans for ASB include the original `messaging.correlation_id`.
- [ ] `dlq_replays_total` increments with `provider=AzureServiceBus`, `service`, `event_type`, and `outcome`.

---

## Phase 6: Multi-subscription ASB Capture

**User stories**: 1, 4, 6, 7, 9, 11

### What to build

Expand ASB capture from one subscription to the complete configured service topology. The gateway should create one dead-letter subqueue processor per subscriber service, start the processors without double-registering capture, and keep per-subscription failures isolated so one broken subscription does not stop capture for all others.

### Acceptance criteria

- [ ] ASB capture starts one processor per configured subscriber subscription.
- [ ] The configured subscription names match the services' `EventBus:QueueName` values.
- [ ] Publisher-only and non-messaging services are not polled as ASB subscriptions.
- [ ] A failure captured from each configured subscription is persisted with the correct `OriginalQueue` and `Service`.
- [ ] Processor startup failure for one subscription is logged with topic, subscription, and provider details.
- [ ] Processor startup failure for one subscription does not disable already-started processors for other subscriptions.
- [ ] Stop/dispose shuts down all ASB processors cleanly.
- [ ] Tests prove the provider switch does not register both RabbitMQ and ASB capture implementations.

---

## Phase 7: Observability and Operator Regression

**User stories**: 3, 5, 8, 10

### What to build

Close the behavior loop around metrics, traces, and operator endpoints. This slice should prove that the operator API remains the stable broker-agnostic surface while dashboards can split RabbitMQ and ASB signals by provider.

### Acceptance criteria

- [ ] `dlq_messages_total` emits `provider`, `service`, and `event_type` tags for RabbitMQ and ASB captures.
- [ ] `dlq_replays_total` emits `provider`, `service`, `event_type`, and `outcome` tags for RabbitMQ and ASB replay attempts.
- [ ] `dlq_discards_total` emits `provider`, `service`, `event_type`, and `outcome` tags for RabbitMQ and ASB discard attempts.
- [ ] Existing dead-letter activity sources remain part of platform observability.
- [ ] Replay and discard activity tags continue to include failure id, event type, original queue, service, outcome, and correlation id when present.
- [ ] Operator list, detail, replay, batch replay, and discard endpoint tests pass without provider-specific endpoint branches.
- [ ] Existing origin filtering for `DeadLetter` vs `Outbox` rows still passes.

---

## Phase 8: Package Propagation, Docs, and Regression Gate

**User stories**: 6, 8, 9

### What to build

Propagate the shared library changes to the gateway and any services that need ASB failure metadata. Document the provider-specific DLQ behavior and run the regression set that proves RabbitMQ remains unchanged while ASB capture/replay is available when configured.

### Acceptance criteria

- [ ] `ECommerce.Shared` version is bumped, packed, and pushed to the local NuGet feed.
- [ ] API Gateway consumes the new shared package version.
- [ ] Services that rely on the ASB subscriber failure metadata consume the new shared package version.
- [ ] Repo docs explain that operator routes and `dead_letter_messages` are provider-agnostic and unchanged.
- [ ] Docs list the ASB subscriber subscriptions that the gateway should capture from, using the service `EventBus:QueueName` values.
- [ ] Docs explain how ASB capture behaves when the local emulator is unavailable.
- [ ] Shared library tests pass.
- [ ] API Gateway tests pass.
- [ ] RabbitMQ DLQ integration tests pass or are explicitly run under the existing integration-test gate.
- [ ] Optional ASB emulator verification captures a failed ASB message into the store and replays it through `/operator/api/failures/{id}/replay`.
- [ ] Final code search shows no RabbitMQ-only DLQ capture or publisher dependency is registered under `Messaging:Provider=AzureServiceBus`.

## Implementation notes

- The ASB capture path should use the SDK's dead-letter subqueue support rather than manually constructing `$DeadLetterQueue` entity paths.
- Keep the provider value available to capture, replay, and discard components through dependency injection so metric tags do not depend on parsing configuration in every class.
- The ASB capture implementation should treat broker connection failures differently from store failures: broker startup failures should not crash the operator API in local emulator scenarios, while store failures for a received subqueue message must not complete that message.
- If a central topology source for subscriber queue names is not already available to the gateway, add the smallest gateway-owned DLQ capture topology configuration whose values are the same `EventBus:QueueName` strings used by the services. Do not discover by scanning all broker subscriptions, because that would poll orphan subscriptions.
- The outbox failure poller remains a separate origin path. It should continue to persist failed outbox rows with `Origin=Outbox` and should not be merged with broker dead-letter capture.
