# PRD: Provider-agnostic dead-letter capture and replay (Rabbit + ASB)

> GitHub issue: [#81](https://github.com/daonhan/Microservices-in-.NET/issues/81)
> Part of the RabbitMQ → Azure Service Bus local-dev migration. **Depends on PRD A** (`PRD-Messaging-Adopt-AddPlatform.md`); benefits from PRD B (`PRD-Messaging-AsbEmulator-Local.md`).

## Problem Statement

The DLQ pipeline (`DeadLetterHostedService` in `shared-libs/ECommerce.Shared/Infrastructure/DeadLetter/`) is hardcoded against RabbitMQ — it consumes the `ecommerce-dlq` fanout exchange via `IRabbitMqConnection`, `IModel`, and `EventingBasicConsumer`, and parses Rabbit-specific headers (`x-death`, `x-original-queue`). When the platform runs on Azure Service Bus, every subscription has its own `$DeadLetterQueue` subqueue, the headers differ, and Rabbit-only code cannot capture them. The operator API at `/operator/api/failures*` would silently miss every ASB failure.

## Solution

Introduce an `IDeadLetterCapture` provider abstraction. Keep the existing Rabbit poller as the Rabbit implementation. Add an ASB implementation that opens a `ServiceBusProcessor` per per-service subscription's dead-letter subqueue, normalizes the message into the shared `DeadLetterMessage` shape, and persists via the existing `IDeadLetterStore`. The replayer (`DeadLetterReplayer`) already depends only on `IDeadLetterPublisher`, so it stays unchanged; add an ASB `IDeadLetterPublisher` that re-sends to the original topic.

## User Stories

1. As an operator, I want failed messages to land in `dead_letter_messages` regardless of broker, so that the operator UI is the single source of truth for failures.
2. As an operator, I want to replay a failed ASB message via `/operator/api/failures/{id}/replay`, so that I do not need broker-specific tooling.
3. As an operator, I want batch replay and discard endpoints to behave identically across providers, so that runbooks remain stable.
4. As a developer, I want headers like correlation id, original queue/subscription, attempt count, and failure reason captured consistently, so that triage queries do not need broker-specific branches.
5. As an SRE, I want `dlq_messages_total`, `dlq_replays_total`, and `dlq_discards_total` counters tagged with provider, so that dashboards split by Rabbit vs ASB.
6. As a developer running locally on the ASB Emulator, I want the gateway DLQ pipeline to start (or no-op cleanly), so that the operator UI does not crash on a missing broker.
7. As a maintainer, I want exactly one capture implementation active at runtime, so that we do not double-store messages.
8. As a developer, I want the existing Rabbit-based test coverage (`DeadLetterReplayerTests`, `DeadLetterDbContextOriginFilterTests`, `DeadLetterPlatformObservabilityTests`) to keep passing, so that the abstraction does not regress current behavior.
9. As a platform engineer, I want the ASB capture path to subscribe only to subscriptions actually wired up by services in the topology, so that orphan subscriptions are not polled.
10. As an operator, I want `dlq.replay` traces on ASB to carry the original `CorrelationId`, so that distributed traces link replay to the original failure.
11. As a developer, I want capture failures (e.g., DB write fails) to abandon/dead-letter the subqueue message rather than silently complete, so that failures are not lost.

## Implementation Decisions

- New abstraction `IDeadLetterCapture` registered behind the messaging provider switch. The existing Rabbit-specific class becomes `RabbitMqDeadLetterCapture`. New `AzureServiceBusDeadLetterCapture` implements the same contract.
- `IDeadLetterStore`, `DeadLetterReplayer`, `DeadLetterDiscarder`, and the `DeadLetterMessage` schema stay unchanged.
- `IDeadLetterPublisher` gets a Rabbit and an ASB implementation. Provider chosen via `Messaging:Provider`.
- Header normalization on ASB: read `ApplicationProperties` for `original_queue`, `event_type`, `service`, `failure_reason`, `attempts`, `failed_at`, `correlation_id` (same names emitted by the producer-side dead-letter path). Where producers do not yet stamp these on ASB, add them in the consumer error branch.
- `DeadLetterStartupExtensions` uses `MessagingOptions.Provider` to register exactly one capture and one publisher implementation.
- Add a `provider` tag to the three Prometheus counters.
- ASB capture discovers per-service subscriptions from `EventBus:QueueName` (matches the subscription-naming convention from `AzureServiceBusHostedService`).
- Replay on ASB re-publishes to the same topic with `Subject` set to the original `EventType`. The replayer interface does not change.

## Testing Decisions

- Good tests verify external behavior — message ends up in store, replay re-publishes on the correct broker, counters increment with the right tags — not which SDK calls were made.
- Reuse the prior-art tests:
  - `DeadLetterReplayerTests` already exercises replay logic with a fake publisher; add a parameterized variant asserting the resolved publisher matches the configured provider for both `RabbitMq` and `AzureServiceBus`.
  - `DeadLetterDbContextOriginFilterTests` and `DeadLetterPlatformObservabilityTests` continue to gate capture-side observability — add provider-tag assertions.
  - `MessagingProviderSwitchTests` extended with cases asserting the dead-letter capture and publisher resolve to the matching provider's implementation.
- Add a unit test for the ASB capture's header normalization with sample `ServiceBusReceivedMessage` shapes (use the SDK's mock/factory helpers).
- An end-to-end test against the ASB Emulator (introduced in PRD B) is optional and gated behind the same env var as the PRD-B integration test.

## Out of Scope

- A polished Operator UI for ASB-specific affordances.
- Reprocessing already-captured Rabbit failures onto ASB or vice versa.
- Cross-provider DLQ migration tooling.

## Further Notes

- Depends on PRD A; benefits from PRD B for local exercise.
- Operator endpoints (`/operator/api/failures*`) and DB schema do not change.
