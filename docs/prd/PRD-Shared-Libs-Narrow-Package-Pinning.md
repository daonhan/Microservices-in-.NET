# PRD: Narrow `ECommerce.Shared` package pinning for service consumers

## Problem Statement

The shared library split has already produced eight capability packages plus the `ECommerce.Shared` umbrella metapackage, but every production consumer still references the umbrella. That keeps the migration simple, but it hides the coupling the split was meant to expose: lightweight services pull broker adapters, EF outbox infrastructure, dead-letter storage, contracts, and QA seeding code even when they only need one or two platform capabilities.

The most visible examples are Auth and Basket. Auth uses shared health checks, observability, OpenAPI, and QA seeding, but receives RabbitMQ, Azure Service Bus, DeadLetter, EventBus, Contracts, Redis, and broker retry dependencies through the umbrella. Basket is subscriber-only and Redis-backed, but still receives SQL/EF outbox infrastructure and dead-letter packages through the umbrella.

There is also one package-boundary issue that blocks clean narrow pinning: provider-aware messaging registration currently lives in the DeadLetter package. Any service that calls the provider switch would need to reference DeadLetter even when it has no DLQ responsibility. That makes a csproj-only repin possible but semantically wrong.

## Solution

Introduce a small messaging composition package that owns provider selection and the `AddPlatformEventBus`, `AddPlatformEventPublisher`, and `AddPlatformSubscriberService` registration surface. Move the provider resolver and provider-switch DI extensions out of DeadLetter into this package. DeadLetter should depend on that messaging package for provider selection, while normal services should depend on messaging without depending on DeadLetter.

After that package-boundary correction, repin service consumers from the umbrella to the smallest direct package set that matches their production code. Keep the umbrella as a compatibility and prototype package, not the default for optimized production consumers.

Recommended final ownership:

| Consumer | Recommended shared package references |
|---|---|
| API Gateway | `ECommerce.Shared.Platform`, `ECommerce.Shared.Messaging`, `ECommerce.Shared.DeadLetter` |
| Auth | `ECommerce.Shared.Platform`, `ECommerce.Shared.Testing.Qa` |
| Basket | `ECommerce.Shared.Platform`, `ECommerce.Shared.EventBus`, `ECommerce.Shared.Messaging`, `ECommerce.Shared.Testing.Qa` |
| Product | `ECommerce.Shared.Platform`, `ECommerce.Shared.EventBus`, `ECommerce.Shared.Messaging`, `ECommerce.Shared.Testing.Qa` |
| Order | `ECommerce.Shared.Platform`, `ECommerce.Shared.EventBus`, `ECommerce.Shared.Messaging`, `ECommerce.Shared.Contracts`, `ECommerce.Shared.Testing.Qa` |
| Inventory | `ECommerce.Shared.Platform`, `ECommerce.Shared.EventBus`, `ECommerce.Shared.Messaging`, `ECommerce.Shared.Contracts`, `ECommerce.Shared.Testing.Qa` |
| Payment | `ECommerce.Shared.Platform`, `ECommerce.Shared.EventBus`, `ECommerce.Shared.Messaging`, `ECommerce.Shared.Contracts`, `ECommerce.Shared.Testing.Qa` |
| Shipping | `ECommerce.Shared.Platform`, `ECommerce.Shared.EventBus`, `ECommerce.Shared.Messaging`, `ECommerce.Shared.Contracts`, `ECommerce.Shared.Testing.Qa` |
| Saga | `ECommerce.Shared.Platform`, `ECommerce.Shared.EventBus`, `ECommerce.Shared.Messaging`, `ECommerce.Shared.Contracts`, `ECommerce.Shared.Testing.Qa` |

No current production consumer should directly reference `ECommerce.Shared.RabbitMq` or `ECommerce.Shared.AzureServiceBus`; the messaging package owns provider selection. No current service except API Gateway should reference `ECommerce.Shared.DeadLetter`.

## User Stories

1. As a maintainer of Auth, I want Auth to reference only platform and QA seeding packages, so that it does not carry broker, outbox, dead-letter, or saga command dependencies it does not use.
2. As a maintainer of Basket, I want Basket to reference platform, event-handler registration, messaging, and QA seeding packages, so that subscriber-only Redis code does not depend on dead-letter capture.
3. As a maintainer of Product, I want Product to reference platform, outbox/event bus, messaging, and QA seeding packages, so that publisher-only behavior does not pull saga contracts or DLQ operator storage.
4. As a maintainer of Order, I want Order to reference platform, event bus/outbox, messaging, contracts, and QA seeding, so that its saga command handlers compile without the umbrella.
5. As a maintainer of Inventory, I want Inventory to reference platform, event bus/outbox, messaging, contracts, and QA seeding, so that inventory saga commands remain explicit direct dependencies.
6. As a maintainer of Payment, I want Payment to reference platform, event bus/outbox, messaging, contracts, and QA seeding, so that fully-qualified saga command usage is backed by an explicit contracts dependency.
7. As a maintainer of Shipping, I want Shipping to reference platform, event bus/outbox, messaging, contracts, and QA seeding, so that shipment command handling is explicit without pulling gateway DLQ storage.
8. As a maintainer of Saga, I want Saga to reference platform, event bus/outbox, messaging, contracts, and QA seeding, so that the orchestrator owns its command/event surface directly.
9. As a maintainer of API Gateway, I want Gateway to reference platform, messaging, and dead-letter packages, so that DLQ operator behavior is explicit while saga contracts and QA fixtures are excluded.
10. As a shared-libs maintainer, I want provider-aware messaging registration to live outside DeadLetter, so that normal services do not need a DLQ package just to select RabbitMQ or Azure Service Bus.
11. As a shared-libs maintainer, I want the DeadLetter package to depend on the messaging package, so that DLQ capture and replay use the same provider selection rules as the rest of the platform.
12. As a shared-libs maintainer, I want the umbrella package to remain available, so that prototypes, broad integration experiments, and external demos can keep a one-line package reference.
13. As a release engineer, I want narrow package pinning to happen one consumer at a time, so that any missing dependency surfaces in the smallest possible blast radius.
14. As a release engineer, I want package versions to remain lockstep, so that no service can pin mismatched shared-libs sibling package versions.
15. As a developer reviewing service csproj files, I want direct package references to explain the service's shared capability needs, so that dependency ownership is readable without inspecting transitive restore output.
16. As a developer using `dotnet list package`, I want transitive RabbitMQ, Azure Service Bus, DeadLetter, and Contracts dependencies to appear only when the service actually needs the owning capability.
17. As a developer adding a new service, I want a simple package selection rule, so that I choose narrow packages for production services and the umbrella only for deliberate all-in consumption.
18. As an operator, I want runtime behavior to stay unchanged after repinning, so that health checks, JWT validation, outbox publishing, provider selection, and DLQ replay keep their existing contracts.
19. As an SRE testing broker provider switches, I want both RabbitMQ and Azure Service Bus boot tests to continue passing after the messaging package extraction, so that the provider switch remains behavior-preserving.
20. As an architect, I want package-boundary analyzers updated for the new messaging package, so that future shared-libs changes cannot reintroduce hidden DeadLetter coupling.

## Implementation Decisions

- Add a new `ECommerce.Shared.Messaging` capability package for provider-aware messaging composition.
- Move provider resolution and provider-switch event bus registration from DeadLetter into Messaging.
- Keep broker adapter packages provider-specific and avoid direct service references to those packages.
- Keep DeadLetter focused on DLQ capture, storage, replay, discard, and provider-specific DLQ adapters.
- Keep Platform bundled for this PRD. Splitting Authentication, HealthChecks, Observability, and OpenAPI is a separate decision.
- Keep EventBus bundled with Outbox for this PRD. Splitting event abstractions from outbox would further reduce Basket's dependency surface, but it is a separate design.
- Keep QA seeding as a direct runtime dependency for services that call QA seeding hooks. Do not add it to API Gateway.
- Remove direct RabbitMQ client package references from services unless service-owned code uses RabbitMQ client types directly.
- Keep the umbrella package and update documentation to describe it as compatibility/prototype/default-broad consumption, not the optimized production target.
- Repin consumers in low-risk order: Auth, Basket, Product, Order, Inventory, Payment, Shipping, Saga, API Gateway.

## Testing Decisions

- Good tests should verify external behavior: DI registration outcomes, provider selection, service boot, outbox publishing, subscriber registration, DLQ replay, and auth policy behavior.
- Add or update shared-libs package-boundary tests so Messaging is allowed to depend on Kernel, EventBus, RabbitMQ, and Azure Service Bus, while DeadLetter depends on Messaging and the provider-specific DLQ adapters it owns.
- Run shared-libs build and tests after extracting Messaging.
- For each service repin, run restore, build, and test from that service directory.
- For broker-dependent services, run existing provider boot tests and one RabbitMQ default-provider smoke.
- For services with Azure Service Bus provider tests, verify the Azure Service Bus branch still selects the same adapter and fails fast on invalid provider values.
- For API Gateway, verify DLQ list, detail, replay, discard, and batch replay tests still pass.
- For Auth, verify no messaging packages are restored as direct dependencies after narrow pinning.
- For Basket, verify subscriber registration tests still pass and no DeadLetter package is directly referenced.

## Out of Scope

- Changing messaging behavior, retry policies, queue names, topic names, event payloads, or DLQ schemas.
- Splitting Platform into smaller packages.
- Splitting EventBus abstractions from Outbox.
- Moving saga command contracts out of shared-libs.
- Changing the local NuGet feed location or lockstep versioning rule.
- Adopting central package management across the monorepo.
- Removing QA seeding from production service assemblies.

## Further Notes

- A csproj-only narrow repin can be attempted without adding Messaging, but it is not recommended because normal services would need to reference DeadLetter solely to access provider-switch registration.
- Order, Inventory, Payment, Shipping, and Saga are the closest umbrella candidates because they need most shared capabilities. If the priority is fewest csproj lines rather than dependency minimization, they can remain on the umbrella. If the priority is optimized references, they should narrow-pin after the Messaging extraction.
- API Gateway is the only current production consumer with a real DeadLetter ownership reason.
