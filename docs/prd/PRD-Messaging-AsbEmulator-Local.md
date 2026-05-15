# PRD: ASB Emulator local-dev profile + emulator-aware topology auto-provision

> GitHub issue: [#80](https://github.com/daonhan/Microservices-in-.NET/issues/80)
> Part of the RabbitMQ → Azure Service Bus local-dev migration. **Depends on PRD A** (`PRD-Messaging-Adopt-AddPlatform.md`).

## Problem Statement

After PRD A, services can be configured to talk to Azure Service Bus, but a developer running locally still has nowhere to point at — there is no ASB broker in the Compose stack, no documented connection string for the Microsoft Service Bus Emulator, and no startup code that creates topics or subscriptions on first run. Without these, exercising the ASB code path locally requires a real Azure namespace (cost, secrets, latency).

## Solution

Add an opt-in Compose profile that runs the Microsoft Service Bus Emulator (and its SQL Edge sidecar) on `localhost`. The default Compose stack still runs Rabbit on `localhost` and remains the default for `docker compose up`. A new `Messaging:AzureServiceBus:AutoProvisionTopology` knob (`Auto` default) detects emulator connection strings (`UseDevelopmentEmulator=true`) and creates the configured topic plus the per-service subscription via `ServiceBusAdministrationClient` at boot. On real namespaces the default stays "do not auto-create" so cloud topology stays Bicep-managed.

## User Stories

1. As a developer, I want to start an ASB Emulator locally with a single `docker compose --profile asb up`, so that I do not need an Azure subscription to exercise the ASB code path.
2. As a developer running F5 outside Compose, I want a documented emulator connection string, so that my services can talk to the local broker.
3. As a developer, I want the default `docker compose up` to remain Rabbit-on-localhost, so that the existing Phase-4 smoke test path is untouched.
4. As a developer, I want topics and per-service subscriptions to be created automatically when I point at the emulator, so that I do not have to script them manually.
5. As a platform engineer, I want auto-provisioning to be off by default on real Azure namespaces, so that production topology stays under Bicep control.
6. As a developer, I want `Messaging:AzureServiceBus:AutoProvisionTopology` to support `Auto` (emulator-only), `Always`, and `Never`, so that I can opt in or out per environment.
7. As an SRE, I want auto-provision attempts to log clearly whether each topic and subscription was created or already existed, so that boot behavior is auditable.
8. As a developer, I want emulator boot to be reasonably fast on a typical laptop, so that the F5 inner loop stays usable.
9. As a maintainer, I want emulator config (including license-acceptance env vars) to live next to the Compose file, so that the setup is self-contained.
10. As a developer running tests against the emulator, I want a clean teardown procedure, so that subsequent runs start from a known state.
11. As a developer, I want the README and `docs/` to explain when to use Rabbit vs ASB locally, so that I do not accidentally choose the wrong one.
12. As an operator, I want Bicep/IaC to keep producing the same topology on real environments, so that `Always` auto-provision is not required in production.

## Implementation Decisions

- Add `servicebus-emulator` and `sqledge` services to `docker-compose.yaml` under `profiles: ["asb"]`. Default `docker compose up` does not start them.
- Add the emulator's required `Config.json` (or equivalent) under an `infra/local/asb-emulator/` folder — license-accepted, single-namespace, single-topic shape that mirrors the configured `Messaging:AzureServiceBus:TopicName`.
- Extend `AzureServiceBusOptions` with `AutoProvisionTopology` (`Auto` | `Always` | `Never`, default `Auto`).
- Introduce a topology-provisioner module wired into `AddAzureServiceBusEventBus` and `AddAzureServiceBusSubscriberService`. It runs as an ordered hosted-service step before the subscriber starts. It inspects the connection string for `UseDevelopmentEmulator=true`. Under `Auto` plus emulator, or `Always`, it calls `ServiceBusAdministrationClient.CreateTopicIfNotExistsAsync` and `CreateSubscriptionIfNotExistsAsync` for the per-service `EventBus:QueueName`. Idempotent.
- Provide documented connection-string examples for: emulator (`Endpoint=sb://localhost;SharedAccessKeyName=...;UseDevelopmentEmulator=true`), shared dev namespace, per-dev namespace.
- Boot log line per topic/subscription: `Topic '{topic}' ensured` / `Subscription '{topic}/{subscription}' ensured`.

## Testing Decisions

- Good tests verify external behavior (the topic/subscription exists, or the provisioner refused to act) rather than implementation details (which Azure SDK call was made).
- Unit tests for the provisioner's decision logic: `Auto` + emulator string → provision; `Auto` + cloud string → skip; `Always` → provision; `Never` → skip. Use a small abstraction over the admin client so cloud is not contacted.
- Opt-in integration test (gated by an env var) that spins the emulator profile and asserts a freshly-provisioned subscription receives a published event end-to-end. Not part of CI Phase-4 (smoke tests stay Rabbit-only).
- Manual local procedure documented under `docs/qa/` for running an ASB pass against the emulator.

## Out of Scope

- Replacing Rabbit in Compose, smoke tests, or CI gating (smoke tests stay Rabbit-only).
- Production topology management — remains Bicep-driven in `Infrastructure - Deployment/`.
- DLQ poller behavior on ASB (PRD C).

## Further Notes

- Depends on PRD A.
- The Microsoft Service Bus Emulator currently requires accepting the EULA via env var; ensure this lives in the Compose profile only, not the default stack.
- The emulator supports a subset of ASB features only — note feature-parity gaps in the local-dev guide (PRD D).
