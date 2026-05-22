# Plan: Basket Service Clean Architecture + Vertical Slices Pilot

> Source PRD: [`docs/prd/PRD-Basket-CleanArch-VSA-Pilot.md`](../prd/PRD-Basket-CleanArch-VSA-Pilot.md) (GitHub issue [#171](https://github.com/daonhan/Microservices-in-.NET/issues/171))
> Companion ADR: [`docs/adr/0011-order-cleanarch-vsa-pilot.md`](../adr/0011-order-cleanarch-vsa-pilot.md) — composed by reference; no new ADR for Basket.
> Runbook: [`docs/runbooks/adding-a-new-slice.md`](../runbooks/adding-a-new-slice.md) — reused unchanged.
> Branch: `refactor/basket-vsa` — single PR for review.

## Context

`Basket.Service` is the third Clean Architecture + Vertical Slice (VSA) pilot in the repo (after Order and Product). Today it is organized by technical type: HTTP routes in `Endpoints/BasketApiEndpoints.cs`, the `CustomerBasket` aggregate and `BasketProduct` value object in `Models/`, two integration-event handlers in `IntegrationEvents/EventHandlers/`, Redis repositories and the QA seeder in `Infrastructure/`. Reading "what happens when a product is added to a basket?" requires hopping across three folders.

This plan reorganizes `Basket.Service` into the same `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/` shape Order and Product use, inside a single `Basket.Service.csproj` (plus a sibling `Basket.Service.LayoutAnalyzer` analyzer sub-project). Basket is the *floor case* of the pattern: no SQL, no EF, no outbox, no integration events published, no saga participation, no auth on routes. Three documented divergences from Order/Product:

1. **No outbox seam.** `IIntegrationMap<,>` + `DomainEventOutboxInterceptor` are not introduced. Basket emits no events; the seam would be dead code.
2. **No CQRS-lite read split.** `GetBasket` uses `IBasketStore` like the writes; there is only one read and no projection benefit.
3. **No rich-aggregate promotion.** `CustomerBasket` is already a reasonable light aggregate. Methods preserved verbatim.

Zero behavior change on existing paths. Every existing `Basket.Tests` test passes after namespace updates only. `ECommerce.Shared` public API is unchanged; no nupkg version bump. No new EF migrations (no SQL). No changes to public HTTP routes, response shapes, status codes, auth requirements, integration event payload contracts, Redis cache key schema, the QA seeder, the custom OpenTelemetry meter view, healthcheck endpoints, or CI/CD pipelines.

Propagation to remaining services (auth, inventory, payment, shipping, saga, api-gateway) is deferred to a follow-up ADR after Basket lands.

## Architectural decisions

Durable across all phases:

- **Project shape**: single `Basket.Service.csproj` retained. New sibling `Basket.Service.LayoutAnalyzer` analyzer sub-project (mirroring Order/Product) referenced as an `Analyzer` package reference. No application-tier split (no `Basket.Domain`/`Basket.Application`/`Basket.Infrastructure`).
- **Folder topology**:
  - `Features/<Slice>/` — one folder per inbound trigger. Slices: `GetBasket`, `CreateBasket`, `AddBasketProduct`, `DeleteBasketProduct`, `DeleteBasket`, `OrderCreated`, `ProductPriceUpdated`. Each owns its endpoint or consumer, request DTOs, sealed handler, slice DI extension. No mappers (no outbox seam).
  - `Domain/` — `CustomerBasket` aggregate, `BasketProduct` value object, `Domain/Abstractions/IBasketStore`. No Redis, no HTTP references. Aggregate API identical to today.
  - `Contracts/Integration/` — `OrderCreatedEvent`, `ProductPriceUpdatedEvent` payload classes (cross-service contracts; payload unchanged).
  - `Infrastructure/Data/Redis/` — `RedisBasketStore`, `InMemoryBasketStore`, `CustomerBasketCacheModel`, `RedisExtensions`, `RedisOptions`.
  - `Infrastructure/Seeding/` — `RedisQaSeederHostedService` (location unchanged).
- **Namespaces** match folders: `Basket.Service.Domain`, `Basket.Service.Domain.Abstractions`, `Basket.Service.Features.<Slice>`, `Basket.Service.Contracts.Integration`, `Basket.Service.Infrastructure.Data.Redis`, `Basket.Service.Infrastructure.Seeding`. Old `Basket.Service.Endpoints`, `Basket.Service.ApiModels`, `Basket.Service.Models`, `Basket.Service.IntegrationEvents`, `Basket.Service.IntegrationEvents.EventHandlers`, `Basket.Service.Infrastructure.Data` namespaces are removed as files relocate.
- **Dispatch model**: no MediatR, no in-house mediator. Endpoints and integration-event consumers take their slice handler class via constructor injection (delegate-style minimal-API `[FromServices]` for endpoints; standard scoped DI for consumers) and call `HandleAsync(...)` directly. Slice handler classes are `internal sealed` with one public async method.
- **Domain richness rule**: `CustomerBasket` stays light. Existing `AddBasketProduct`, `RemoveBasketProduct`, `BasketTotal` methods preserved verbatim. No new domain events, no new invariants, no promotion. Slice handlers are orchestration only: load aggregate via `IBasketStore`, call domain method(s), persist via `IBasketStore`.
- **CQRS-lite read split — skipped**: Basket has one read (`GET /{customerId}`) with no projection benefit. `Features/GetBasket/` uses `IBasketStore` like the writes.
- **Persistence seam**: `IBasketStore` lives in `Domain/Abstractions/`. `RedisBasketStore` + `InMemoryBasketStore` + `CustomerBasketCacheModel` live in `Infrastructure/Data/Redis/`.
- **Outbox / event translation seam — omitted**: no `IIntegrationMap<,>`, no `DomainEventOutboxInterceptor`, no `Infrastructure/Outbox/` folder. `AddOutbox(...)` is not called. Documented divergence from Order/Product, recorded in the PRD, in the PR description, and as a one-line note in root `CLAUDE.md`.
- **Internal ops endpoints — none**: no `InternalOutboxEndpoints`, no `/internal/outbox/failed`, no DLQ poller integration on the outbox side. Basket is consumer-only; the DLQ poller's consumer-side dead-letter handling is unaffected.
- **Price-cache lookup placement**: stays inside the slice handler in `Features/CreateBasket/` and `Features/AddBasketProduct/`. No new `IProductPriceProvider` abstraction. The duplication is intentional and deferred per the rule of three.
- **Slice DI**: each slice exposes a static `AddXxxSlice(this IServiceCollection)` extension. Event-consumer slices wire their consumer via the existing shared `AddEventHandler<TEvent, THandler>` inside the slice extension. `Program.cs` chains slice extensions as a fluent manifest. `IBasketStore` registration (`AddScoped<IBasketStore, RedisBasketStore>`) either lives in a small `AddBasketInfrastructure()` helper in `Infrastructure/` or remains in `Program.cs` — chosen during phase 6 by what reads more cleanly.
- **Cross-slice sharing rule**: rule of three — duplicate freely; extract to `Domain/` (behavioral) or `Features/Shared/` (helper) only on the third use. NetArchTest forbids `Basket.Service.Features.<X>` referencing `Basket.Service.Features.<Y>` for any `X != Y`.
- **Boundary enforcement**:
  - NetArchTest rules in `Basket.Tests/Architecture/LayoutTests.cs` (ported from `Order.Tests/Architecture/LayoutTests.cs` with namespace swap):
    - `Domain` types must not reference `Basket.Service.Infrastructure.*` or `Basket.Service.Features.*`.
    - `Features.<X>` types must not reference `Features.<Y>` for distinct slices.
    - `Infrastructure` types may reference `Domain` + `Contracts`, but not `Features`.
    - `Contracts` types reference nothing internal.
  - Roslyn `Basket.Service.LayoutAnalyzer` sub-project (ported from `Product.Service.LayoutAnalyzer`) raises the same four rules as compile-time errors.
  - Both must fail on an intentional spike before phase 8 is marked done. Spike-and-revert recorded in PR description.
- **Routes / contracts / payloads**: unchanged. Public HTTP routes (`GET/POST/PUT/DELETE /{customerId}`, `DELETE /{customerId}/{productId}`), response shapes, status codes, anonymous auth posture preserved. `OrderCreatedEvent` and `ProductPriceUpdatedEvent` payloads byte-identical; only their folder + namespace move.
- **QA seeder**: `RedisQaSeederHostedService` location unchanged (`Infrastructure/Seeding/`). `BasketQaSeederTests` location unchanged (`Basket.Tests/Qa/`).
- **OpenTelemetry**: the custom meter view in `AddPlatformObservability` (the `basket-size` histogram with explicit buckets `[0, 1, 3, 5, 10, 25]`) preserved verbatim. Counters (`basket-updates`, `basket-products-added`, `basket-products-removed`) increment on the same conditions as today.
- **Shared library**: `ECommerce.Shared` public API unchanged. No nupkg version bump. The pilot composes existing `AddPlatformEventBus`, `AddPlatformSubscriberService`, `AddEventHandler`, `AddRedisCache`, `AddQaSeeding`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddRedisProbe`, `AddRabbitMqProbe`, `AddPlatformOpenApi`.
- **Validation / auth**: out of scope. Existing absence of FluentValidation / DataAnnotations and anonymous-routes posture preserved.
- **Test layout**: `Basket.Tests/Features/<Slice>/` mirrors `Features/<Slice>/`. `Basket.Tests/Domain/CustomerBasketTests.cs` stays. `Basket.Tests/Architecture/LayoutTests.cs` holds NetArchTest rules. `Basket.Tests/Qa/BasketQaSeederTests.cs` stays. `MessagingProviderBootTests` moves from `Basket.Tests/IntegrationEvents/` to `Basket.Tests/Infrastructure/`.
- **Commit gating**: pre-commit hook (`dotnet husky run --group pre-commit` — runs `dotnet format --verify-no-changes`, `dotnet build --no-restore`, and **Basket tests**) gates every commit. Basket is unique: its own tests are the ones the hook runs, so every commit on the branch validates Basket end-to-end automatically. No `--no-verify`. No `Hooks-Deferred:` / `Validation-Deferred:` footers. If the sandbox hook cannot pass, stop and hand off to host.

---

## Phase 1: Scaffold — NetArchTest dependency + LayoutAnalyzer sub-project + skipped layout tests

**User stories**: 10, 11, 14, 18

### What to build

Lay enforcement scaffolding so later phases can flip rules on without re-authoring. Add a `NetArchTest.Rules` package reference to `Basket.Tests`. Create `Basket.Tests/Architecture/LayoutTests.cs` with the four boundary rules authored but **skipped** (`[Fact(Skip = "Enabled in phase 8")]`). Scaffold a new `Basket.Service.LayoutAnalyzer` sub-project (port-paste from `Product.Service.LayoutAnalyzer/LayoutAnalyzer.cs` with namespace prefix swap from `Product` to `Basket`); wire it into `Basket.Service.csproj` as an `Analyzer` package reference. Configure analyzer rules in `.editorconfig` at warning-only (or disabled) severity for now so the build stays clean. Update `basket-microservice.slnx` to include the new analyzer project. No source-file moves in this phase; only the test project and analyzer project change.

### Acceptance criteria

- [ ] `Basket.Tests/Architecture/LayoutTests.cs` exists with four NetArchTest rules authored as `[Fact(Skip = "Enabled in phase 8")]`.
- [ ] `basket-microservice/Basket.Service.LayoutAnalyzer/` sub-project exists with `LayoutAnalyzer.cs` ported from `Product.Service.LayoutAnalyzer` (namespace `Basket.Service.LayoutAnalyzer`).
- [ ] `Basket.Service.csproj` references `Basket.Service.LayoutAnalyzer` as an `Analyzer` package reference.
- [ ] `basket-microservice.slnx` includes the new analyzer project.
- [ ] `.editorconfig` declares the analyzer's rules at warning-only severity for this phase.
- [ ] `dotnet build` clean for the whole solution; no new errors or warnings.
- [ ] `dotnet test` green (the four skipped tests are reported as skipped, not failed).
- [ ] Pre-commit hook (`dotnet husky run --group pre-commit`) passes on the commit.

---

## Phase 2: Layout move — Domain + Contracts

**User stories**: 5, 13, 14, 19, 20

### What to build

Move pure-domain types and cross-service contract payloads into the new layout without changing behavior. Create `Domain/` and `Domain/Abstractions/`. Relocate `CustomerBasket` and `BasketProduct` from `Models/` into `Domain/`. Relocate `IBasketStore` from `Infrastructure/Data/` into `Domain/Abstractions/`. Create `Contracts/Integration/`. Relocate `OrderCreatedEvent` and `ProductPriceUpdatedEvent` from `IntegrationEvents/` into `Contracts/Integration/`. Rename namespaces accordingly: types now live in `Basket.Service.Domain`, `Basket.Service.Domain.Abstractions`, `Basket.Service.Contracts.Integration`. Update all `using` directives across `Basket.Service` and `Basket.Tests` to point at the new namespaces. `RedisBasketStore` (which implements `IBasketStore`) and the event handlers (which consume the inbound payloads) update their `using` directives but do not move yet.

### Acceptance criteria

- [ ] `Domain/CustomerBasket.cs`, `Domain/BasketProduct.cs`, `Domain/Abstractions/IBasketStore.cs` exist with the new namespaces.
- [ ] `Contracts/Integration/OrderCreatedEvent.cs`, `Contracts/Integration/ProductPriceUpdatedEvent.cs` exist with namespace `Basket.Service.Contracts.Integration`.
- [ ] No file in `Domain/` has a `using` for `Microsoft.Extensions.Caching.Distributed`, `StackExchange.Redis`, or any `Basket.Service.Infrastructure.*` namespace.
- [ ] No file in `Contracts/Integration/` references any other `Basket.Service.*` namespace.
- [ ] `dotnet build` clean across the repo.
- [ ] `dotnet test` green — every existing `Basket.Tests` test passes after namespace updates only.
- [ ] Pre-commit hook passes on the commit.

---

## Phase 3: Layout move — Infrastructure

**User stories**: 13, 19, 20

### What to build

Relocate persistence and supporting infrastructure into the new layout. Create `Infrastructure/Data/Redis/`. Move `RedisBasketStore`, `InMemoryBasketStore`, `CustomerBasketCacheModel`, `RedisExtensions`, `RedisOptions` into `Infrastructure/Data/Redis/`. Rename namespaces to `Basket.Service.Infrastructure.Data.Redis`. Keep `RedisQaSeederHostedService` at `Infrastructure/Seeding/` (already in the right place; only its namespace becomes `Basket.Service.Infrastructure.Seeding`). Update all `using` directives across `Basket.Service` and `Basket.Tests`. The composition root in `Program.cs` updates its imports but the registration calls stay in the same shape. `BasketApiEndpoints` (still in `Endpoints/`) and the event handlers (still in `IntegrationEvents/EventHandlers/`) update their `using` directives.

### Acceptance criteria

- [ ] `Infrastructure/Data/Redis/RedisBasketStore.cs`, `InMemoryBasketStore.cs`, `CustomerBasketCacheModel.cs`, `RedisExtensions.cs`, `RedisOptions.cs` exist with namespace `Basket.Service.Infrastructure.Data.Redis`.
- [ ] `Infrastructure/Seeding/RedisQaSeederHostedService.cs` exists with namespace `Basket.Service.Infrastructure.Seeding`.
- [ ] No file in `Infrastructure/Data/Redis/` references any `Basket.Service.Features.*` namespace.
- [ ] No file in `Infrastructure/Data/Redis/` references `Basket.Service.Endpoints.*` or `Basket.Service.IntegrationEvents.EventHandlers.*`.
- [ ] `dotnet build` clean across the repo.
- [ ] `dotnet test` green; messaging-provider boot test still passes against both `RabbitMq` and `AzureServiceBus`.
- [ ] Pre-commit hook passes on the commit.

---

## Phase 4: Extract HTTP slices — GetBasket, CreateBasket, AddBasketProduct, DeleteBasketProduct, DeleteBasket

**User stories**: 1, 2, 3, 6, 7, 8, 12, 19, 26

### What to build

Extract the five HTTP routes into self-contained vertical slices. For each slice, create `Features/<Slice>/` containing: the route registration (an `IEndpointRouteBuilder` extension or a route mapping invoked from the slice DI extension), the slice's request/response DTOs (where the existing `ApiModels/*` records apply), a sealed slice handler class with a single public async `HandleAsync(...)` method, and an `AddXxxSlice(this IServiceCollection)` extension that registers the handler as scoped. Constructor-inject the slice handler into the endpoint; the endpoint method is thin and delegates to `HandleAsync`. Move the OpenTelemetry counter calls (`basket-updates`, `basket-products-added`, `basket-products-removed`, `basket-size`) into the slice handler that owns them. The cached product-price lookup in `CreateBasket` and `AddBasketProduct` lives inside their slice handlers (`IDistributedCache` injected on the slice handler) — intentionally duplicated per the rule of three. As each slice is extracted, remove the corresponding `routeBuilder.Map*` line from `Endpoints/BasketApiEndpoints.cs`; once all five are gone, `BasketApiEndpoints.cs` is deleted. `Program.cs` chains `services.AddGetBasketSlice().AddCreateBasketSlice().AddAddBasketProductSlice().AddDeleteBasketProductSlice().AddDeleteBasketSlice()` and the `app.RegisterEndpoints()` call is replaced by per-slice route registration (chosen during implementation: either a unified `app.MapBasketFeatures()` helper that calls each slice's `MapXxxRoute(this IEndpointRouteBuilder)` or each slice's route mapping invoked individually).

The five slices may land as one commit each or as a single bundled commit, at the implementer's discretion — the phase acceptance criteria are per-slice so partial progress is trackable.

### Acceptance criteria

#### GetBasket (`GET /{customerId}`)
- [ ] `Features/GetBasket/` exists with handler, slice DI extension, and route registration.
- [ ] Handler uses `IBasketStore.GetBasketByCustomerId` directly; no CQRS-lite projection.
- [ ] `Program.cs` calls `services.AddGetBasketSlice()`; the corresponding `MapGet` line in `BasketApiEndpoints.cs` is removed.
- [ ] Route, response shape, status code, anonymous auth posture preserved byte-identically.

#### CreateBasket (`POST /{customerId}`)
- [ ] `Features/CreateBasket/` exists with handler, `CreateBasketRequest` DTO (moved from `ApiModels/`), slice DI extension, and route registration.
- [ ] Handler injects `IBasketStore`, `IDistributedCache`, and `MetricFactory`; performs the cached-price lookup inline; calls `IBasketStore.CreateCustomerBasket`; records `basket-updates`, `basket-products-added`, `basket-size` counters/histogram.
- [ ] `Program.cs` calls `services.AddCreateBasketSlice()`; the corresponding `MapPost` line in `BasketApiEndpoints.cs` is removed.
- [ ] Route, response status (201 Created), and the cache-miss `InvalidOperationException` behavior preserved byte-identically.

#### AddBasketProduct (`PUT /{customerId}`)
- [ ] `Features/AddBasketProduct/` exists with handler, `AddBasketProductRequest` DTO (moved from `ApiModels/`), slice DI extension, and route registration.
- [ ] Handler injects `IBasketStore`, `IDistributedCache`, `MetricFactory`; performs the cached-price lookup inline (intentionally duplicated with `CreateBasket`); calls `IBasketStore.UpdateCustomerBasket`; records the same counters/histogram.
- [ ] `Program.cs` calls `services.AddAddBasketProductSlice()`; the corresponding `MapPut` line in `BasketApiEndpoints.cs` is removed.
- [ ] Route, response status (204 NoContent), and cache-miss behavior preserved byte-identically.

#### DeleteBasketProduct (`DELETE /{customerId}/{productId}`)
- [ ] `Features/DeleteBasketProduct/` exists with handler, slice DI extension, and route registration.
- [ ] Handler injects `IBasketStore` and `MetricFactory`; loads aggregate, calls `RemoveBasketProduct`, persists; records `basket-updates`, `basket-products-removed`, `basket-size`.
- [ ] `Program.cs` calls `services.AddDeleteBasketProductSlice()`; the corresponding `MapDelete` line in `BasketApiEndpoints.cs` is removed.
- [ ] Route, response status (204 NoContent) preserved byte-identically.

#### DeleteBasket (`DELETE /{customerId}`)
- [ ] `Features/DeleteBasket/` exists with handler, slice DI extension, and route registration.
- [ ] Handler injects `IBasketStore`; calls `IBasketStore.DeleteCustomerBasket`. No counters recorded (matches today's behavior).
- [ ] `Program.cs` calls `services.AddDeleteBasketSlice()`; the corresponding `MapDelete` line in `BasketApiEndpoints.cs` is removed.
- [ ] Route, response status (204 NoContent) preserved byte-identically.

#### Phase-wide
- [ ] `Endpoints/BasketApiEndpoints.cs` and the `Endpoints/` folder are deleted once all five slices are extracted.
- [ ] `ApiModels/CreateBasketRequest.cs` and `ApiModels/AddBasketProductRequest.cs` no longer live in `ApiModels/`; the `ApiModels/` folder is deleted if empty.
- [ ] No file in `Features/<X>/` references any `Basket.Service.Features.<Y>.*` namespace for any other slice.
- [ ] `dotnet build` clean across the repo.
- [ ] `dotnet test` green — all existing endpoint tests pass; the OpenTelemetry meter-listener tests continue to observe the same counters and histogram on the same conditions.
- [ ] Manual smoke against running service confirms all five routes return the same status codes and response shapes as before.
- [ ] Pre-commit hook passes on each commit.

---

## Phase 5: Extract event-consumer slices — OrderCreated, ProductPriceUpdated

**User stories**: 4, 19, 28

### What to build

Extract the two integration-event consumers into vertical slices matching the HTTP shape. `Features/OrderCreated/` contains the consumer (the existing `OrderCreatedEventHandler` logic, renamed if helpful), a sealed handler class, and `AddOrderCreatedSlice(this IServiceCollection)` extension that registers the consumer via the existing shared `AddEventHandler<OrderCreatedEvent, OrderCreatedHandler>`. `Features/ProductPriceUpdated/` mirrors the shape for `ProductPriceUpdatedEvent` — the slice handler retains the `IDistributedCache` `SetStringAsync` with the 24-hour sliding expiration. `Program.cs` calls `services.AddOrderCreatedSlice()` and `services.AddProductPriceUpdatedSlice()`, replacing the chained `.AddEventHandler<...>` calls today. `IntegrationEvents/EventHandlers/OrderCreatedEventHandler.cs` and `IntegrationEvents/EventHandlers/ProductPriceUpdatedEventHandler.cs` are deleted; the `IntegrationEvents/` folder is deleted once its inbound-payload classes have already moved (phase 2) and these handlers are gone. The messaging-provider boot test continues to exercise both `Messaging:Provider` values without modification beyond `using` updates.

### Acceptance criteria

- [ ] `Features/OrderCreated/` exists with handler, slice DI extension wiring `AddEventHandler<OrderCreatedEvent, ...>`, and namespace `Basket.Service.Features.OrderCreated`.
- [ ] `Features/ProductPriceUpdated/` exists with handler, slice DI extension wiring `AddEventHandler<ProductPriceUpdatedEvent, ...>`, namespace `Basket.Service.Features.ProductPriceUpdated`, and preserved 24-hour `SlidingExpiration` cache entry options.
- [ ] `Program.cs` calls both slice extensions; the chained `.AddEventHandler<OrderCreatedEvent, ...>` and `.AddEventHandler<ProductPriceUpdatedEvent, ...>` calls in `Program.cs` are removed.
- [ ] `IntegrationEvents/EventHandlers/` is empty and deleted. The `IntegrationEvents/` folder is empty and deleted (its inbound payload classes already moved in phase 2).
- [ ] No file in `Features/OrderCreated/` or `Features/ProductPriceUpdated/` references any other `Basket.Service.Features.<Y>.*` namespace.
- [ ] `dotnet build` clean across the repo.
- [ ] `dotnet test` green — `MessagingProviderBootTests` continues to pass against both `RabbitMq` and `AzureServiceBus` providers; any per-handler tests pass with namespace updates only.
- [ ] Manual smoke: publishing a synthetic `OrderCreatedEvent` clears the matching customer basket; publishing a synthetic `ProductPriceUpdatedEvent` updates the cached product price with the 24-hour sliding expiration intact.
- [ ] Pre-commit hook passes on the commit.

---

## Phase 6: Program.cs manifest + delete now-empty folders

**User stories**: 2, 17, 19, 27

### What to build

Polish the composition root into a manifest-style chain and remove leftover artifacts. `Program.cs` becomes a fluent sequence of `services.AddXxxSlice()` calls (for the seven slices) plus the existing shared infra extensions (`AddPlatformEventBus`, `AddPlatformSubscriberService`, `AddRedisCache`, `AddQaSeeding`, `AddPlatformObservability` with the preserved `basket-size` meter view, `AddPlatformHealthChecks`, `AddRedisProbe`, `AddRabbitMqProbe`, `AddPlatformOpenApi`). The `IBasketStore` registration (`AddScoped<IBasketStore, RedisBasketStore>`) is either inlined in `Program.cs` as the only non-slice scoped registration or relocated into a small `AddBasketInfrastructure(this IServiceCollection)` helper in `Infrastructure/` — pick whichever reads more cleanly. Delete now-empty `Endpoints/`, `ApiModels/`, `Models/`, `IntegrationEvents/` folders if they have not already been deleted by earlier phases. Confirm no orphan files remain. The healthcheck route, Redis probe, RabbitMQ probe, Prometheus exporter endpoint, and OpenAPI document URL are unchanged.

### Acceptance criteria

- [ ] `Program.cs` reads as a manifest: a chain of seven slice extension calls + the existing shared infra extensions. No per-handler `AddScoped` and no per-event `AddEventHandler` remain in `Program.cs` (those calls now live in slice extensions).
- [ ] The custom OpenTelemetry meter view passed to `AddPlatformObservability` (the `basket-size` histogram with explicit buckets `[0, 1, 3, 5, 10, 25]`) is preserved verbatim.
- [ ] `IBasketStore` registration lives either in `Program.cs` (clearly delimited as infrastructure wiring) or in an `AddBasketInfrastructure()` helper in `Infrastructure/`.
- [ ] `Endpoints/`, `ApiModels/`, `Models/`, `IntegrationEvents/` folders are removed.
- [ ] `dotnet build` clean across the repo.
- [ ] `dotnet test` green.
- [ ] Manual smoke: service boots, `/health` returns Healthy, `/metrics` exposes the same Prometheus counters and the `basket-size` histogram, the OpenAPI document renders.
- [ ] Pre-commit hook passes on the commit.

---

## Phase 7: Test reshape — `Basket.Tests/Features/<Slice>/`

**User stories**: 14, 15, 16, 19

### What to build

Reshape `Basket.Tests` to mirror the production layout while keeping the cross-cutting test files at the project root. Split `Basket.Tests/Endpoints/BasketApiEndpointsTests.cs` into one test file per slice under `Basket.Tests/Features/<Slice>/` (e.g. `Basket.Tests/Features/GetBasket/GetBasketHandlerTests.cs`, `Basket.Tests/Features/CreateBasket/CreateBasketHandlerTests.cs`, etc.). Tests continue to construct slice handlers directly with `NSubstitute`-mocked `IBasketStore` + `IDistributedCache` + a `MetricFactory` test double — exactly the construction pattern used today, only the test class shape changes. The OpenTelemetry meter-listener tests move with the slice that exercises them. `Basket.Tests/Domain/CustomerBasketTests.cs` stays verbatim. `Basket.Tests/Qa/BasketQaSeederTests.cs` stays verbatim. Move `Basket.Tests/IntegrationEvents/MessagingProviderBootTests.cs` to `Basket.Tests/Infrastructure/MessagingProviderBootTests.cs` and update its `using` directives. Delete the now-empty `Basket.Tests/Endpoints/` and `Basket.Tests/IntegrationEvents/` folders.

### Acceptance criteria

- [ ] `Basket.Tests/Features/GetBasket/`, `Basket.Tests/Features/CreateBasket/`, `Basket.Tests/Features/AddBasketProduct/`, `Basket.Tests/Features/DeleteBasketProduct/`, `Basket.Tests/Features/DeleteBasket/`, `Basket.Tests/Features/OrderCreated/`, `Basket.Tests/Features/ProductPriceUpdated/` each contain the tests for their slice.
- [ ] `Basket.Tests/Domain/CustomerBasketTests.cs` exists unchanged.
- [ ] `Basket.Tests/Qa/BasketQaSeederTests.cs` exists unchanged.
- [ ] `Basket.Tests/Infrastructure/MessagingProviderBootTests.cs` exists; it continues to use `WebApplicationFactory<Program>` and continues to exercise both `Messaging:Provider` values.
- [ ] `Basket.Tests/Architecture/LayoutTests.cs` exists (from phase 1) with rules still skipped.
- [ ] `Basket.Tests/Endpoints/` and `Basket.Tests/IntegrationEvents/` folders are deleted.
- [ ] `dotnet test` green; test counts before and after this phase match (no tests dropped or duplicated).
- [ ] Pre-commit hook passes on the commit.

---

## Phase 8: Enforcement — unskip NetArchTest rules + analyzer as errors + spike-and-revert

**User stories**: 9, 10, 11, 24

### What to build

Turn enforcement on. Remove the `[Fact(Skip = ...)]` attributes from every rule in `Basket.Tests/Architecture/LayoutTests.cs`. Promote the `Basket.Service.LayoutAnalyzer` rules in `.editorconfig` from warning-only to error severity for each of the four boundary rules:

- Code in `Basket.Service.Domain.*` may not reference `Basket.Service.Infrastructure.*` or `Basket.Service.Features.*`.
- Code in `Basket.Service.Features.<X>.*` may not reference `Basket.Service.Features.<Y>.*` for any `X != Y`.
- Code in `Basket.Service.Infrastructure.*` may not reference `Basket.Service.Features.*`.
- Code in `Basket.Service.Contracts.*` may not reference any other internal `Basket.Service.*` namespace.

Demonstrate that both guardrails fire on an intentional violation: introduce one cross-boundary `using` in a throwaway commit (e.g. a `using Basket.Service.Infrastructure.Data.Redis;` inside a `Domain/` file, or a `using Basket.Service.Features.CreateBasket;` inside `Features/AddBasketProduct/`). Confirm NetArchTest fails AND the `Basket.Service.LayoutAnalyzer` raises a build-time error. Revert the spike before the phase merges. Document the spike-and-revert demonstration in the PR description (linked commit shas + paste of both error outputs).

### Acceptance criteria

- [ ] No `[Fact(Skip = ...)]` remains in `Basket.Tests/Architecture/LayoutTests.cs`. All four layout tests run and pass.
- [ ] `Basket.Service/.editorconfig` (or equivalent analyzer config) declares the four `Basket.Service.LayoutAnalyzer` rules at error severity.
- [ ] PR description records the spike-and-revert demonstration showing both NetArchTest and the analyzer fire on a deliberately introduced cross-boundary reference. Both error messages are quoted in the PR description.
- [ ] `dotnet build` clean across the repo.
- [ ] `dotnet test` green across the repo.
- [ ] Pre-commit hook passes on the commit.

---

## Phase 9: Docs — root `CLAUDE.md` Basket line update

**User stories**: 22, 23

### What to build

Update the root `CLAUDE.md` Basket entry to reference ADR 0011 and the existing runbook, and to record the documented divergence. Locate the Basket line (currently under the "Services" list or wherever Basket is mentioned) and add a short pointer in the form used for Order today: e.g. "Basket: pilots Clean Architecture + Vertical Slices (ADR 0011 + runbook `adding-a-new-slice.md`). **Diverges from Order/Product: no outbox seam (Basket emits no integration events); no CQRS-lite read split.**" If `basket-microservice/CLAUDE.md` exists, add the same pointer there. No new ADR. No new runbook. No update to `docs/adr/0011-order-cleanarch-vsa-pilot.md`'s follow-up list — that ADR's candidate order is preserved historically; this PRD's "Further Notes" already captures the revised order.

### Acceptance criteria

- [ ] Root `CLAUDE.md` mentions ADR 0011 and the runbook in the Basket entry, plus the one-line "no outbox seam, no CQRS-lite split" divergence note.
- [ ] `basket-microservice/CLAUDE.md` mentions both (if such a file exists in the repo).
- [ ] `dotnet build` clean and `dotnet test` green across the repo.
- [ ] Pre-commit hook passes on the commit.

---

## Out of scope (per PRD)

- Refactoring any other service (auth, inventory, payment, shipping, saga, api-gateway, product if not yet merged). Propagation is a follow-up ADR after Basket lands.
- Modifying `ECommerce.Shared`. The pilot composes existing extensions only. No nupkg version bump.
- Adding request validation (FluentValidation or DataAnnotations).
- Adding authentication to Basket routes.
- Introducing MediatR or any mediator-style dispatcher.
- Splitting `Basket.Service.csproj` into multiple application-tier projects (the analyzer sub-project is a separate analyzer assembly, not an application split).
- Introducing the outbox translation seam (`IIntegrationMap<,>` + `DomainEventOutboxInterceptor`).
- Introducing a CQRS-lite read/write split.
- Promoting `CustomerBasket` to a richer aggregate with domain events.
- Extracting a new `IProductPriceProvider` abstraction.
- Changing the Redis cache key schema or `CustomerBasketCacheModel`.
- Changing integration event payload contracts (`OrderCreatedEvent`, `ProductPriceUpdatedEvent`).
- Changing the outbox table, dispatcher, or retry/DLQ behavior in `ECommerce.Shared.Infrastructure.Outbox` (Basket doesn't use it).
- Changing `BasketApiEndpoints`' public HTTP routes, response shapes, status codes, or auth requirements.
- Changing the QA seeder behavior or seeded personas.
- Changing the custom OpenTelemetry meter view (`basket-size` histogram bucket boundaries).
- Changing CI/CD pipelines, Docker Compose, Kubernetes manifests, or Azure pipelines.
- Performance optimization.
- Filing a new ADR or runbook.
