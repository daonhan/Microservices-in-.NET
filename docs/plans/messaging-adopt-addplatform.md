# Plan: Adopt provider-agnostic Messaging wiring

> Source PRD: `docs/prd/PRD-Messaging-Adopt-AddPlatform.md`
> GitHub issue: https://github.com/daonhan/Microservices-in-.NET/issues/79

## Architectural decisions

Durable decisions that apply across all phases:

- **Messaging provider switch**: `Messaging:Provider` selects `RabbitMq` or `AzureServiceBus`; missing or blank values default to `RabbitMq`.
- **Failure mode**: Unknown `Messaging:Provider` values fail fast during startup instead of silently falling back to RabbitMQ.
- **Composition roots**: Services that currently wire Rabbit directly move to `AddPlatformEventBus`, `AddPlatformEventPublisher`, and `AddPlatformSubscriberService` according to whether they publish, subscribe, or do both.
- **Default local behavior**: Existing Compose and local development behavior stays RabbitMQ-first.
- **Event contracts**: Event payloads, event handler registration, queue/subscription names, retry policy, and DLQ routing are unchanged in this PRD.
- **Outbox publishing**: `OutboxBackgroundService` continues to publish only through `IEventBus`, so outbox-driven events inherit the selected provider.
- **Gateway DLQ boundary**: Provider-agnostic DLQ capture/replay is out of scope and remains owned by PRD C. This plan preserves the RabbitMQ operator path and documents any ASB gateway limitation instead of introducing a partial DLQ abstraction here.
- **Auth service**: Auth currently has no messaging registration. This PRD should not add messaging to Auth unless a concrete publish/subscribe path is introduced separately.

---

## Phase 1: Shared Provider Contract

**User stories**: 5, 6, 10

### What to build

Make the shared platform messaging switch strict and visible before touching service composition roots. The selected provider is logged during platform registration, `RabbitMq` remains the default for missing configuration, `AzureServiceBus` selects the ASB adapter, and unknown values fail at boot. Convert the outbox audit into executable evidence that outbox publishing is resolved through `IEventBus` rather than a broker-specific dependency.

### Acceptance criteria

- [ ] Missing or blank `Messaging:Provider` resolves to `RabbitMq`.
- [ ] `Messaging:Provider=AzureServiceBus` resolves the ASB event bus adapter.
- [ ] Unknown provider values throw during startup or service registration.
- [ ] Boot logs include the selected messaging provider.
- [ ] Shared tests prove `OutboxBackgroundService` has no RabbitMQ-specific publish dependency.
- [ ] Existing shared Rabbit and ASB provider-switch tests still pass.

---

## Phase 2: Publisher-Only Service Tracer

**User stories**: 1, 2, 3, 4, 6, 8, 9

### What to build

Use Product as the first service-level tracer bullet because it publishes through the outbox but does not subscribe. Replace direct RabbitMQ publisher wiring with platform messaging wiring while keeping the Product API, outbox behavior, health checks, auth, and Rabbit default behavior unchanged.

### Acceptance criteria

- [ ] Product uses provider-agnostic event bus and publisher registration.
- [ ] Product still boots with the default RabbitMQ configuration.
- [ ] Product host-boot coverage proves `RabbitMq` resolves the Rabbit adapter.
- [ ] Product host-boot coverage proves `AzureServiceBus` resolves the ASB adapter.
- [ ] Existing Product endpoint and internal outbox tests still pass.
- [ ] No Product event payloads, outbox schema, queue names, or endpoint contracts change.

---

## Phase 3: Subscriber-Only Service Tracer

**User stories**: 1, 2, 3, 4, 8, 9

### What to build

Use Basket as the subscriber-only tracer bullet. Move Basket to provider-agnostic event bus and subscriber registration while keeping existing event handler registration and Redis-backed basket behavior unchanged.

### Acceptance criteria

- [ ] Basket uses provider-agnostic event bus and subscriber registration.
- [ ] Basket keeps the same event handlers for order-created and product-price-updated flows.
- [ ] Basket still boots with the default RabbitMQ configuration.
- [ ] Basket host-boot coverage proves `RabbitMq` resolves the Rabbit adapter.
- [ ] Basket host-boot coverage proves `AzureServiceBus` resolves the ASB adapter.
- [ ] Existing Basket API and domain tests still pass.

---

## Phase 4: Saga Pub/Sub Rollout

**User stories**: 1, 2, 3, 4, 7, 9

### What to build

Apply the same platform messaging pattern to the saga services that both publish and subscribe: Order, Inventory, Payment, and Shipping. Each service keeps its existing event handlers, outbox usage, internal outbox endpoint, health checks, authentication, and saga behavior while gaining provider-selected event bus wiring.

### Acceptance criteria

- [ ] Order uses provider-agnostic bus, publisher, and subscriber registration.
- [ ] Inventory uses provider-agnostic bus, publisher, and subscriber registration.
- [ ] Payment uses provider-agnostic bus, publisher, and subscriber registration.
- [ ] Shipping uses provider-agnostic bus, publisher, and subscriber registration.
- [ ] Each service has RabbitMQ host-boot coverage proving the Rabbit adapter resolves by default.
- [ ] Each service has ASB host-boot coverage proving the ASB adapter resolves when configured.
- [ ] Existing saga event handler tests and API tests still pass on RabbitMQ.
- [ ] No saga event payloads, handler registrations, queue names, retry policy, or DLQ routing change.

---

## Phase 5: Gateway Operator Boundary

**User stories**: 1, 2, 4, 7

### What to build

Move the gateway's event bus registration to the provider-aware platform path without expanding the DLQ implementation. The operator API and RabbitMQ DLQ capture/replay behavior stay intact. Any ASB limitation in the gateway operator path is documented as deferred to PRD C rather than hidden behind partial behavior.

Implementation note: this phase only moves `OperatorModule.AddServices` onto `AddPlatformEventBus`. Gateway DLQ capture/replay still uses the RabbitMQ-specific `DeadLetterHostedService` and `RabbitMqDeadLetterPublisher`; provider-agnostic DLQ capture/replay remains deferred to PRD C.

### Acceptance criteria

- [ ] Gateway event bus registration uses the platform provider switch.
- [ ] RabbitMQ operator failure list, detail, replay, batch replay, and discard behavior remain unchanged.
- [ ] Gateway tests cover default RabbitMQ boot behavior for the operator module.
- [ ] The plan or follow-up documentation clearly states that provider-agnostic DLQ capture/replay is deferred to PRD C.
- [ ] No `dead_letter_messages` schema, operator route, metric name, or replay payload contract changes in this PRD.

---

## Phase 6: Canonical Wiring and Regression Gate

**User stories**: 3, 4, 7, 8, 9

### What to build

Make the provider-aware messaging snippet the canonical pattern for future services and run the relevant regression gates. The final state should make `AddPlatform*` the obvious copy path while preserving RabbitMQ as the local and Compose default.

### Acceptance criteria

- [ ] Repo guidance no longer presents direct RabbitMQ registration as the default copy pattern for new services.
- [ ] All changed service tests pass.
- [ ] Shared library tests pass.
- [ ] Phase-4 RabbitMQ smoke tests remain the regression gate for unchanged local behavior.
- [ ] Code search shows no direct RabbitMQ messaging registration in service composition roots except where intentionally retained for Rabbit-specific health checks or deferred DLQ implementation.
- [ ] The implementation notes identify PRD C as the owner of ASB DLQ capture/replay.

### Implementation notes

- Canonical service wiring is `AddPlatformEventBus(builder.Configuration)` plus `AddPlatformEventPublisher(builder.Configuration)` for publishers and `AddPlatformSubscriberService(builder.Configuration)` for subscribers.
- RabbitMQ remains the default local and Compose provider via `Messaging:Provider=RabbitMq` or missing provider config.
- RabbitMQ-specific readiness probes remain acceptable where the local health model still checks RabbitMQ directly.
- Gateway DLQ capture/replay remains RabbitMQ-specific in this PRD. Provider-agnostic ASB DLQ capture/replay belongs to PRD C, `PRD-Messaging-DLQ-Provider-Abstraction.md`.
