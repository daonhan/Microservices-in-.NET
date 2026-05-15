# PRD: Adopt provider-agnostic Messaging wiring (AddPlatform*) across services

> GitHub issue: [#79](https://github.com/daonhan/Microservices-in-.NET/issues/79)
> Part of the RabbitMQ → Azure Service Bus local-dev migration. **Blocks PRDs B, C, D** (`PRD-Messaging-AsbEmulator-Local.md`, `PRD-Messaging-DLQ-Provider-Abstraction.md`, `PRD-Messaging-LocalDev-Docs.md`).

## Problem Statement

The platform has an `IEventBus` abstraction with two adapters — RabbitMQ and Azure Service Bus — and a `MessagingStartupExtensions` switch that resolves them via `Messaging:Provider`. All six services (`auth`, `basket`, `order`, `product`, `inventory`, `shipping`, `payment`) and the gateway still call the Rabbit-specific `AddRabbitMqEventBus` / `AddRabbitMqEventPublisher` / `AddRabbitMqSubscriberService` directly in `Program.cs`, so the Messaging switch is dead code from the caller side. Until callers move to `AddPlatform*`, no service can run on Azure Service Bus regardless of configuration.

## Solution

Replace direct Rabbit calls in every service composition root with the provider-agnostic `AddPlatformEventBus` / `AddPlatformEventPublisher` / `AddPlatformSubscriberService` extensions. With `Messaging:Provider=RabbitMq` (default) behavior is unchanged. Setting `Messaging:Provider=AzureServiceBus` switches the service to ASB without code changes. The Outbox `OutboxBackgroundService` is audited and confirmed to publish only via `IEventBus`, so it inherits the switch automatically.

## User Stories

1. As a backend developer, I want every service to honor `Messaging:Provider` config, so that flipping the env var moves the service from Rabbit to ASB without recompiling.
2. As a platform engineer, I want one wiring path per service composition root, so that I do not have to maintain parallel Rabbit-and-ASB wiring per service.
3. As a developer onboarding to the repo, I want `Program.cs` to read uniformly across services, so that the messaging boot sequence is obvious at a glance.
4. As a developer running the existing Compose stack, I want default behavior to stay on Rabbit, so that current smoke tests and Compose-based local runs are unaffected.
5. As an operator, I want the boot log to state which messaging provider was selected, so that I can confirm config in deployed environments.
6. As a maintainer of the outbox, I want `OutboxBackgroundService` to depend only on `IEventBus`, so that outbox-driven publishes follow the configured provider.
7. As a CI gatekeeper, I want the Phase-4 smoke tests to keep passing on Rabbit, so that the wiring change introduces no regression.
8. As a developer writing new services, I want a single canonical wiring snippet to copy, so that future services pick up the abstraction by default.
9. As an integration-test author, I want existing `WebApplicationFactory<Program>` tests to continue passing, so that bootstrapping does not regress.
10. As an SRE, I want `Messaging:Provider` to fail fast on an unknown value, so that typos surface at boot rather than silently falling back to Rabbit.

## Implementation Decisions

- Replace `AddRabbitMqEventBus` / `AddRabbitMqEventPublisher` / `AddRabbitMqSubscriberService` calls in all six services and the gateway with `AddPlatformEventBus` / `AddPlatformEventPublisher` / `AddPlatformSubscriberService`.
- Default config in each service's `appsettings.json`: `"Messaging": { "Provider": "RabbitMq" }`. Compose env stays Rabbit.
- `MessagingStartupExtensions.ResolveProvider` currently returns `RabbitMq` on missing/whitespace and silently falls through on unknown values. Change unknown values to throw at boot.
- Add a one-line boot log inside the platform registration extensions: `Messaging provider selected: {provider}`.
- Audit `OutboxBackgroundService` and confirm `IEventBus` is the sole publish dependency; remove any Rabbit-typed dependency if found.
- No change to event payloads, queue/subscription names, retry policy, or DLQ wiring under this PRD (deferred to PRDs C and D).

## Testing Decisions

- Good tests verify external behavior (DI resolution outcome, configured-provider routing) and not which extension method was called internally.
- Prior art: `shared-libs/ECommerce.Shared.Tests/MessagingProviderSwitchTests.cs` already exercises the switch in `MessagingStartupExtensions`. Extend or reuse.
- Add a per-service host-boot test (one per `Program.cs`) that boots with `Messaging:Provider=RabbitMq` and asserts `IEventBus` resolves to the Rabbit adapter; a parallel test asserts the ASB adapter resolves under `Messaging:Provider=AzureServiceBus`.
- Add a unit test asserting `OutboxBackgroundService` constructor depends only on `IEventBus`, replacing the audit note with executable evidence.
- Existing Phase-4 Compose smoke test remains the regression gate.

## Out of Scope

- ASB Emulator Compose profile, topology auto-provisioning, and local-dev docs (PRDs B and D).
- Dead-letter poller provider abstraction (PRD C).
- Bicep changes for ASB topics/subscriptions in real environments.
- Smoke-test workflow modifications to run against ASB.

## Further Notes

- Blocks PRDs B, C, and D.
- The provider switch and ASB adapter already live in `shared-libs/ECommerce.Shared@2.11.2`; no package version bump expected unless the audit changes the extension surface.
