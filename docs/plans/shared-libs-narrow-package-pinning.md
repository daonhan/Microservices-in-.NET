# Plan: Shared-Libs Narrow Package Pinning

> Source PRD: [`docs/prd/PRD-Shared-Libs-Narrow-Package-Pinning.md`](../prd/PRD-Shared-Libs-Narrow-Package-Pinning.md)

## Architectural decisions

Durable decisions that apply across all phases:

- **Messaging package ownership**: `ECommerce.Shared.Messaging` owns provider-aware composition in the existing `ECommerce.Shared.Infrastructure.Messaging` namespace, including provider resolution and `AddPlatformEventBus`, `AddPlatformEventPublisher`, and `AddPlatformSubscriberService`.
- **Broker adapter ownership**: `ECommerce.Shared.RabbitMq` and `ECommerce.Shared.AzureServiceBus` remain provider-specific adapter packages. Production services should not directly reference them unless service-owned code uses provider-specific types.
- **DeadLetter ownership**: `ECommerce.Shared.DeadLetter` owns DLQ capture, storage, replay, discard, and provider-specific DLQ adapters. It depends on `ECommerce.Shared.Messaging` for provider selection; normal services do not reference DeadLetter just to select RabbitMQ or Azure Service Bus.
- **Umbrella package**: `ECommerce.Shared` remains available as a compatibility and prototype metapackage. Optimized production consumers move to direct capability packages.
- **Lockstep versioning**: All shared-libs packages, including the new Messaging package, ship at one shared version. Every service repin uses that same version for each direct package reference.
- **Stable namespaces**: Moving provider-switch code changes package ownership, not public namespaces or service `using` statements beyond package-reference cleanup.
- **Routes and schemas**: No API routes, event payloads, queue names, retry policies, outbox schema, DLQ schema, or service database schemas change. Gateway operator routes remain under `/operator/api/failures*`.
- **Direct package rule**: Auth uses only Platform and Testing.Qa; Basket/Product use Platform, EventBus, Messaging, Testing.Qa; saga participant services and Saga add Contracts; API Gateway uses Platform, Messaging, DeadLetter.
- **Validation cadence**: Repin one consumer at a time in the PRD order: Auth, Basket, Product, Order, Inventory, Payment, Shipping, Saga, API Gateway.

---

## Phase 1: Messaging Capability Tracer

**User stories**: 10, 11, 12, 14, 19, 20

### What to build

Create `ECommerce.Shared.Messaging` as the provider-aware composition package. Move the provider resolver and provider-switch DI registration surface out of DeadLetter into Messaging while preserving the existing behavior: missing or blank `Messaging:Provider` defaults to RabbitMQ, Azure Service Bus selects the ASB adapter, and unknown providers fail fast. Update DeadLetter to consume Messaging for provider selection, keep the umbrella package as a metapackage over all capabilities, and update package-boundary analyzer rules so Messaging is the only package allowed to bridge EventBus plus broker adapters for normal event-bus composition.

Cut and publish the next lockstep shared-libs version to the local NuGet feed before service repins start.

### Acceptance criteria

- [ ] `ECommerce.Shared.Messaging` is included in `shared-libs/ECommerce.Shared.slnx` and in the umbrella package.
- [ ] Messaging references only the allowed provider-selection dependencies: Kernel, EventBus, RabbitMq, and AzureServiceBus.
- [ ] DeadLetter references Messaging for provider selection and no longer owns `AddPlatformEventBus`, `AddPlatformEventPublisher`, or `AddPlatformSubscriberService`.
- [ ] Provider-switch tests prove RabbitMQ default behavior, Azure Service Bus selection, provider-selected logging, and fail-fast invalid provider behavior.
- [ ] DeadLetter tests still prove DLQ capture, replay, discard, metrics, and provider-specific DLQ adapters.
- [ ] `dotnet build ECommerce.Shared.slnx` and `dotnet test ECommerce.Shared.slnx` pass from `shared-libs`.
- [ ] `dotnet pack -c Release ECommerce.Shared.slnx` emits every shared-libs package at one lockstep version, including Messaging, and all packages are available in `shared-libs/local-nuget-packages`.

---

## Phase 2: Auth Narrow Pin

**User stories**: 1, 13, 14, 15, 16, 18

### What to build

Repin Auth as the lowest-risk service tracer. Replace the umbrella package with direct Platform and Testing.Qa references at the shared lockstep version. Keep JWT issuance, JWKS, health checks, observability, OpenAPI, and QA seeding behavior unchanged.

### Acceptance criteria

- [ ] `Auth.Service.csproj` has direct references only to the shared capabilities Auth actually uses: `ECommerce.Shared.Platform` and `ECommerce.Shared.Testing.Qa`.
- [ ] `ECommerce.Shared`, Messaging, EventBus, Contracts, and DeadLetter are not direct Auth package references.
- [ ] Auth restore, build, and tests pass from `auth-microservice`.
- [ ] Auth JWT, JWKS, service-token, health, observability, OpenAPI, and QA seeding tests still pass.
- [ ] `dotnet list package` confirms the Auth csproj communicates its shared capability needs without the umbrella.

---

## Phase 3: Basket Subscriber Narrow Pin

**User stories**: 2, 13, 14, 15, 16, 18, 19

### What to build

Repin Basket as the subscriber-only tracer. Replace the umbrella package with Platform, EventBus, Messaging, and Testing.Qa. Preserve Redis-backed basket behavior, event handler registration, RabbitMQ default boot behavior, and Azure Service Bus provider-switch boot coverage.

### Acceptance criteria

- [ ] `Basket.Service.csproj` directly references `ECommerce.Shared.Platform`, `ECommerce.Shared.EventBus`, `ECommerce.Shared.Messaging`, and `ECommerce.Shared.Testing.Qa`.
- [ ] Basket has no direct DeadLetter, Contracts, RabbitMq, AzureServiceBus, or umbrella shared package reference.
- [ ] Any direct `RabbitMQ.Client` package reference is removed unless Basket-owned code uses RabbitMQ client types.
- [ ] Basket restore, build, and tests pass from `basket-microservice`.
- [ ] Basket subscriber registration and provider boot tests still pass for RabbitMQ default and Azure Service Bus selection.
- [ ] `dotnet list package --include-transitive` shows DeadLetter only if a justified transitive dependency remains; it is not direct.

---

## Phase 4: Product Publisher Narrow Pin

**User stories**: 3, 13, 14, 15, 16, 18, 19

### What to build

Repin Product as the publisher-only tracer. Replace the umbrella package with Platform, EventBus, Messaging, and Testing.Qa. Preserve Product API behavior, outbox publishing, provider-aware event bus registration, health checks, observability, OpenAPI, and QA seeding.

### Acceptance criteria

- [ ] `Product.Service.csproj` directly references `ECommerce.Shared.Platform`, `ECommerce.Shared.EventBus`, `ECommerce.Shared.Messaging`, and `ECommerce.Shared.Testing.Qa`.
- [ ] Product has no direct DeadLetter, Contracts, RabbitMq, AzureServiceBus, or umbrella shared package reference.
- [ ] Any direct `RabbitMQ.Client` package reference is removed unless Product-owned code uses RabbitMQ client types.
- [ ] Product restore, build, and tests pass from `product-microservice`.
- [ ] Product outbox and provider boot tests still pass.
- [ ] Product event payloads, outbox schema, and queue or topic names are unchanged.

---

## Phase 5: Order Saga Participant Narrow Pin

**User stories**: 4, 13, 14, 15, 16, 18, 19

### What to build

Repin Order as the first saga participant that needs shared contracts. Replace the umbrella package with Platform, EventBus, Messaging, Contracts, and Testing.Qa. Preserve order APIs, outbox publishing, command handling, internal outbox endpoints, JWT/service authorization, and saga replies.

### Acceptance criteria

- [ ] `Order.Service.csproj` directly references `ECommerce.Shared.Platform`, `ECommerce.Shared.EventBus`, `ECommerce.Shared.Messaging`, `ECommerce.Shared.Contracts`, and `ECommerce.Shared.Testing.Qa`.
- [ ] Order has no direct DeadLetter, RabbitMq, AzureServiceBus, or umbrella shared package reference.
- [ ] Any direct `RabbitMQ.Client` package reference is removed unless Order-owned code uses RabbitMQ client types.
- [ ] Order restore, build, and tests pass from `order-microservice`.
- [ ] Confirm/cancel order command handlers compile through the explicit Contracts dependency.
- [ ] Existing order API, outbox, auth policy, and messaging provider boot tests still pass.

---

## Phase 6: Inventory Saga Participant Narrow Pin

**User stories**: 5, 13, 14, 15, 16, 18, 19

### What to build

Repin Inventory with the same saga participant package set: Platform, EventBus, Messaging, Contracts, and Testing.Qa. Preserve stock reservation, commit, release, product-created handling, outbox publishing, provider-switch boot behavior, and QA seed data.

### Acceptance criteria

- [ ] `Inventory.Service.csproj` directly references `ECommerce.Shared.Platform`, `ECommerce.Shared.EventBus`, `ECommerce.Shared.Messaging`, `ECommerce.Shared.Contracts`, and `ECommerce.Shared.Testing.Qa`.
- [ ] Inventory has no direct DeadLetter, RabbitMq, AzureServiceBus, or umbrella shared package reference.
- [ ] Any direct `RabbitMQ.Client` package reference is removed unless Inventory-owned code uses RabbitMQ client types.
- [ ] Inventory restore, build, and tests pass from `inventory-microservice`.
- [ ] Reserve, commit, and release stock command handlers compile through the explicit Contracts dependency.
- [ ] Existing inventory API, outbox, provider boot, and saga command tests still pass.

---

## Phase 7: Payment Saga Participant Narrow Pin

**User stories**: 6, 13, 14, 15, 16, 18, 19

### What to build

Repin Payment with Platform, EventBus, Messaging, Contracts, and Testing.Qa. Preserve payment authorization, capture, void, refund, order-created handling, outbox publishing, provider-switch boot behavior, and explicit saga command usage.

### Acceptance criteria

- [ ] `Payment.Service.csproj` directly references `ECommerce.Shared.Platform`, `ECommerce.Shared.EventBus`, `ECommerce.Shared.Messaging`, `ECommerce.Shared.Contracts`, and `ECommerce.Shared.Testing.Qa`.
- [ ] Payment has no direct DeadLetter, RabbitMq, AzureServiceBus, or umbrella shared package reference.
- [ ] Any direct `RabbitMQ.Client` package reference is removed unless Payment-owned code uses RabbitMQ client types.
- [ ] Payment restore, build, and tests pass from `payment-microservice`.
- [ ] Authorize, capture, void, and refund command handlers compile through the explicit Contracts dependency.
- [ ] Existing payment API, outbox, provider boot, and saga command tests still pass.

---

## Phase 8: Shipping Saga Participant Narrow Pin

**User stories**: 7, 13, 14, 15, 16, 18, 19

### What to build

Repin Shipping with Platform, EventBus, Messaging, Contracts, and Testing.Qa. Preserve shipment creation, cancellation, carrier webhook behavior, outbox publishing, provider-switch boot behavior, and explicit shipment command handling.

### Acceptance criteria

- [ ] `Shipping.Service.csproj` directly references `ECommerce.Shared.Platform`, `ECommerce.Shared.EventBus`, `ECommerce.Shared.Messaging`, `ECommerce.Shared.Contracts`, and `ECommerce.Shared.Testing.Qa`.
- [ ] Shipping has no direct DeadLetter, RabbitMq, AzureServiceBus, or umbrella shared package reference.
- [ ] Any direct `RabbitMQ.Client` package reference is removed unless Shipping-owned code uses RabbitMQ client types.
- [ ] Shipping restore, build, and tests pass from `shipping-microservice`.
- [ ] Create and cancel shipment command handlers compile through the explicit Contracts dependency.
- [ ] Existing shipping API, outbox, provider boot, and saga command tests still pass.

---

## Phase 9: Saga Orchestrator Narrow Pin

**User stories**: 8, 13, 14, 15, 16, 18, 19

### What to build

Repin Saga with Platform, EventBus, Messaging, Contracts, and Testing.Qa. Preserve saga state persistence, order-created orchestration, command publishing, participant reply handling, compensation flows, internal outbox behavior, and operator endpoints.

### Acceptance criteria

- [ ] `Saga.Service.csproj` directly references `ECommerce.Shared.Platform`, `ECommerce.Shared.EventBus`, `ECommerce.Shared.Messaging`, `ECommerce.Shared.Contracts`, and `ECommerce.Shared.Testing.Qa`.
- [ ] Saga has no direct DeadLetter, RabbitMq, AzureServiceBus, or umbrella shared package reference.
- [ ] Any direct `RabbitMQ.Client` package reference is removed unless Saga-owned code uses RabbitMQ client types.
- [ ] Saga restore, build, and tests pass from `saga-microservice`.
- [ ] Orchestrator command publishing and participant reply handling compile through explicit EventBus, Messaging, and Contracts references.
- [ ] Existing saga orchestration, compensation, outbox, and provider boot tests still pass.

---

## Phase 10: API Gateway DLQ Narrow Pin

**User stories**: 9, 11, 13, 14, 15, 16, 18, 19

### What to build

Repin API Gateway as the only production consumer with direct DLQ ownership. Replace the umbrella package with Platform, Messaging, and DeadLetter. Preserve YARP/Ocelot provider switching, JWT/operator authorization, DLQ polling, failed-outbox aggregation, replay, discard, batch replay, metrics, and the provider-aware event bus registration used by DLQ replay.

### Acceptance criteria

- [ ] `ApiGateway.csproj` directly references `ECommerce.Shared.Platform`, `ECommerce.Shared.Messaging`, and `ECommerce.Shared.DeadLetter`.
- [ ] API Gateway has no direct EventBus, Contracts, Testing.Qa, RabbitMq, AzureServiceBus, or umbrella shared package reference unless gateway-owned code proves it needs one directly.
- [ ] API Gateway restore, build, and tests pass from `api-gateway`.
- [ ] Operator list, detail, replay, batch replay, and discard tests still pass.
- [ ] `/operator/api/failures*` authorization requirements remain Bearer plus `Operator` claim.
- [ ] `dead_letter_messages`, `dlq_messages_total`, `dlq_replays_total`, and `dlq_discards_total` behavior remains unchanged.

---

## Phase 11: Documentation and Final Dependency Audit

**User stories**: 12, 14, 15, 16, 17, 18, 20

### What to build

Document the package selection rule for future services and lock down the final dependency shape. Update repo guidance so production services choose narrow capability packages and use the umbrella only for deliberate broad consumption. Add or update analyzer and package-boundary tests so future shared-libs changes cannot reintroduce hidden DeadLetter coupling or bypass the Messaging package.

### Acceptance criteria

- [ ] Shared-libs guidance documents the narrow package selection rule and the umbrella compatibility role.
- [ ] Package-boundary analyzer/tests explicitly allow Messaging to own provider-aware composition and disallow normal service dependencies on DeadLetter for provider selection.
- [ ] Code search shows no production service csproj still directly references `ECommerce.Shared`.
- [ ] Code search shows API Gateway is the only production consumer with a direct `ECommerce.Shared.DeadLetter` reference.
- [ ] Code search shows no production service directly references `ECommerce.Shared.RabbitMq` or `ECommerce.Shared.AzureServiceBus`.
- [ ] `dotnet build` and `dotnet test` pass for shared-libs and every service solution.
- [ ] `docker compose up --build` succeeds for the full stack, or any skipped host-only validation is recorded with a concrete reason and rerun requirement.
- [ ] A saga smoke still completes from order creation through confirmation, and a forced DLQ failure still lists and replays through the gateway operator API.

## End-to-end verification

Run before declaring the implementation complete:

1. `cd shared-libs && dotnet build ECommerce.Shared.slnx && dotnet test ECommerce.Shared.slnx`
2. `cd shared-libs && dotnet pack -c Release ECommerce.Shared.slnx`, then push all lockstep packages to `shared-libs/local-nuget-packages`.
3. For each consumer directory in order: `dotnet restore`, `dotnet build`, and `dotnet test`.
4. `dotnet list package --include-transitive` for Auth, Basket, Product, Saga, and API Gateway to spot-check the intended package graph.
5. `rg 'PackageReference Include="ECommerce.Shared"' -g '*.csproj'` returns no production service hits.
6. `rg 'PackageReference Include="ECommerce.Shared.DeadLetter"' -g '*.csproj'` returns API Gateway only among production services.
7. `docker compose up --build` from the repo root, then verify `/health/ready` across services.
8. Place an order through the gateway and verify the saga reaches confirmed state.
9. Force a DLQ path and verify list, detail, replay, batch replay, discard, metrics, and replay tracing still behave as before.

## Critical areas to modify

- `shared-libs/ECommerce.Shared.Messaging/` (new package)
- `shared-libs/ECommerce.Shared.DeadLetter/Composition/` and `shared-libs/ECommerce.Shared.DeadLetter/Impl/`
- `shared-libs/ECommerce.Shared.LayoutAnalyzer/`
- `shared-libs/ECommerce.Shared/ECommerce.Shared.csproj`
- `shared-libs/ECommerce.Shared.slnx`
- `shared-libs/Directory.Build.props`
- Production service csprojs in Auth, Basket, Product, Order, Inventory, Payment, Shipping, Saga, and API Gateway
- Shared-libs docs and repo guidance that describe package selection and pack/publish workflow

## Out of scope

- Splitting Platform into Authentication, HealthChecks, Observability, and OpenAPI packages.
- Splitting EventBus abstractions from Outbox.
- Changing saga command contracts or moving them out of shared-libs.
- Changing event payloads, queues, topics, retry policies, DLQ routes, DLQ schemas, or service database schemas.
- Adopting central package management across the monorepo.
- Removing QA seeding from production service assemblies.
