# Plan: ASB Emulator local-dev profile and topology auto-provision

> Source PRD: `docs/prd/PRD-Messaging-AsbEmulator-Local.md`
> GitHub issue: https://github.com/daonhan/Microservices-in-.NET/issues/80
> External reference: Microsoft Learn, "Test locally by using the Azure Service Bus emulator"

## Architectural decisions

Durable decisions that apply across all phases:

- **Dependency**: This plan assumes the PRD A provider-agnostic messaging wiring is in place. Services select RabbitMQ or Azure Service Bus through `Messaging:Provider`.
- **Default local path**: `docker compose up` remains RabbitMQ-first. The Service Bus emulator and its SQL sidecar only start when the `asb` Compose profile is explicitly enabled.
- **Emulator boundary**: The emulator runs as local infrastructure, not as a replacement for RabbitMQ smoke tests or CI gates. Rabbit-based Phase-4 smoke behavior stays unchanged.
- **Compose topology**: The emulator profile owns isolated `servicebus-emulator` and SQL sidecar containers. It does not reuse the platform's application SQL Server container.
- **Emulator config**: Local emulator entity configuration lives under `infra/local/asb-emulator/` and mirrors the configured Azure Service Bus topic shape.
- **Configuration shape**: Keep the existing adapter configuration convention as the canonical surface: `AzureServiceBus:ConnectionString`, `AzureServiceBus:TopicName`, and `AzureServiceBus:AutoProvisionTopology`. If the PRD's `Messaging:AzureServiceBus:*` wording is implemented as an alias, it must remain backward-compatible and must not create a second divergent option model.
- **Topology model**: The ASB adapter uses one topic, defaulting to `ecommerce-topic`. Each subscribing service uses its `EventBus:QueueName` as the ASB subscription name.
- **Auto-provision policy**: `AutoProvisionTopology=Auto` is the default and provisions only emulator connection strings containing `UseDevelopmentEmulator=true`. `Always` provisions for any ASB connection string. `Never` skips provisioning.
- **Cloud ownership**: Real Azure namespaces stay Bicep-managed by default. This plan does not require `Always` in dev, staging, or production.
- **Provisioning order**: Topic and subscription provisioning must complete before an ASB subscriber starts processing messages. Publisher startup must also be able to ensure the topic before publishing.
- **Administration boundary**: The provisioner uses `ServiceBusAdministrationClient` behind a small abstraction so tests can prove behavior without contacting Azure. Emulator management endpoint handling must account for the emulator's management port separately from the data-plane connection if needed.
- **Logging**: Startup logs clearly state whether provisioning was skipped, created an entity, or found it already existed. Required entity logs include the topic and each `topic/subscription` pair.
- **Shared package propagation**: Changes in `ECommerce.Shared` require a package version bump, local pack/push, and consumer package updates so services actually pick up the new topology behavior.
- **Out of scope**: Provider-agnostic DLQ capture/replay remains PRD C. The broader local-dev guide remains PRD D, though this plan includes the minimum docs needed to use and tear down the emulator.

---

## Phase 1: Emulator Profile Boots

**User stories**: 1, 3, 8, 9, 10

### What to build

Add an opt-in local infrastructure slice that starts the Microsoft Service Bus emulator and its SQL sidecar from the repo's Compose stack without changing the default RabbitMQ path. The emulator should have a repo-owned config file, scoped license acceptance, exposed AMQP and health/management ports, and a clean teardown path.

### Acceptance criteria

- [ ] `docker compose up` still starts the existing RabbitMQ-backed stack and does not start the emulator or emulator SQL sidecar.
- [ ] `docker compose --profile asb up` starts the emulator profile alongside the existing default services.
- [ ] The emulator exposes AMQP on `localhost:5672` and health/management on `localhost:5300`, unless explicitly overridden.
- [ ] The emulator config is stored under `infra/local/asb-emulator/` and defines the local namespace plus the `ecommerce-topic` topic shape.
- [ ] Emulator and SQL EULA/license acceptance variables are scoped to the `asb` profile and are not required for default Compose usage.
- [ ] The emulator SQL sidecar uses its own container name and storage so it does not collide with the platform `sql` service.
- [ ] A documented teardown command removes emulator containers and volumes so repeated local runs start cleanly.

---

## Phase 2: Auto-provision Decision Contract

**User stories**: 4, 5, 6, 7, 12

### What to build

Introduce the configuration and decision layer for ASB topology provisioning before wiring it to real startup behavior. This slice proves the policy matrix: emulator auto-provisions by default, real Azure does not, `Always` opts in, and `Never` opts out.

### Acceptance criteria

- [ ] `AzureServiceBus:AutoProvisionTopology` accepts `Auto`, `Always`, and `Never`, with `Auto` as the default.
- [ ] Invalid `AutoProvisionTopology` values fail fast during service startup or options validation.
- [ ] `Auto` plus `UseDevelopmentEmulator=true` chooses to provision.
- [ ] `Auto` plus a real Azure namespace connection string chooses to skip.
- [ ] `Always` chooses to provision for emulator and real namespace connection strings.
- [ ] `Never` chooses to skip for emulator and real namespace connection strings.
- [ ] Unit tests cover the decision matrix without contacting the emulator or Azure.
- [ ] Skip logs explain the selected policy, whether the connection string was detected as emulator, and why provisioning did or did not run.

---

## Phase 3: Topic Provisioning Tracer

**User stories**: 2, 4, 5, 6, 7, 8

### What to build

Wire the provisioner into the ASB publisher path and prove a narrow end-to-end publisher slice against the emulator. A service configured with the ASB provider and emulator connection string should be able to boot, ensure the configured topic, and publish without a pre-created Azure namespace.

### Acceptance criteria

- [ ] ASB publisher startup ensures the configured topic when the policy chooses to provision.
- [ ] Topic provisioning is idempotent when the topic already exists.
- [ ] Logs distinguish topic creation from an already-existing topic.
- [ ] The provisioner can use the emulator administration endpoint without breaking the normal ASB data-plane connection.
- [ ] A publisher-only service can boot with `Messaging:Provider=AzureServiceBus` and the documented emulator connection string.
- [ ] `Auto` with a cloud connection string does not attempt to create cloud topology.
- [ ] Existing ASB publisher unit tests still pass.

---

## Phase 4: Subscription Provisioning Tracer

**User stories**: 4, 6, 7, 8

### What to build

Extend the same provisioning path to subscribers and prove that a per-service subscription is ready before the ASB processor starts. Use one subscriber path as the tracer bullet before relying on the pattern for the full saga.

### Acceptance criteria

- [ ] ASB subscriber startup ensures the configured topic and the subscription named by `EventBus:QueueName`.
- [ ] Subscription provisioning completes before message processing starts.
- [ ] Subscription provisioning is idempotent when the subscription already exists.
- [ ] Logs distinguish subscription creation from an already-existing `topic/subscription`.
- [ ] Missing or blank `EventBus:QueueName` fails clearly for ASB subscriber startup.
- [ ] A narrow publish/subscribe check against the emulator proves a published integration event reaches the expected subscription.
- [ ] The publish/subscribe check is opt-in and gated by an environment variable so normal CI remains RabbitMQ-only.

---

## Phase 5: Service Topology Rollout

**User stories**: 1, 2, 4, 7, 8

### What to build

Apply the topology-provisioning behavior across the services that participate in event flow. The goal is that each service using `Messaging:Provider=AzureServiceBus` can point at the emulator and ensure only the topic/subscription it needs, using the same queue/subscription naming convention already used by Rabbit and ASB host boot tests.

### Acceptance criteria

- [ ] Basket, Order, Inventory, Payment, and Shipping each ensure their ASB subscription when configured as subscribers.
- [ ] Product and other publisher-only paths ensure the ASB topic without creating unnecessary subscriptions.
- [ ] The subscription names match the durable `EventBus:QueueName` values used by the services.
- [ ] Existing event payloads, handler registrations, outbox behavior, retry behavior, and DLQ behavior do not change.
- [ ] The shared package version is bumped, packed, pushed to the local feed, and consumed by affected services.
- [ ] Host-boot tests for ASB provider selection continue to pass after package propagation.

---

## Phase 6: Docs, QA, and Regression Gate

**User stories**: 2, 3, 10, 11, 12

### What to build

Document the minimum local workflows needed by this PRD and close the regression loop. Developers should know when to use RabbitMQ, when to use the ASB emulator, how to configure F5 runs, how to tear down the emulator, and which tests remain Rabbit-only.

### Acceptance criteria

- [ ] README or docs point developers to the ASB emulator local-dev workflow.
- [ ] Documentation includes the F5 emulator connection string for host runs, including `UseDevelopmentEmulator=true`.
- [ ] Documentation includes the container-to-emulator connection string shape for services running from Compose.
- [ ] Documentation calls out the emulator administration port behavior for topology provisioning.
- [ ] Documentation explains that RabbitMQ remains the default for `docker compose up` and Phase-4 smoke tests.
- [ ] `docs/qa/` includes an opt-in ASB emulator verification procedure and clean teardown procedure.
- [ ] Real-environment docs state that Azure topology remains Bicep-owned and `Auto` will not create real namespace entities.
- [ ] Shared library tests pass.
- [ ] At least one affected service test suite passes after consuming the new shared package.
- [ ] A final code search confirms no default Compose env var flips services from RabbitMQ to ASB.

### Implementation notes

- The Microsoft emulator uses Docker containers and exposes a health endpoint at `http://localhost:5300/health` by default.
- The emulator data-plane connection string for host F5 runs uses `Endpoint=sb://localhost;...;UseDevelopmentEmulator=true;`.
- Administration operations against the emulator may require the management port in the endpoint. Keep that concern inside the topology provisioner or its options, not scattered through service composition roots.
- Do not add ASB emulator requirements to the pre-commit hook or default CI path.
