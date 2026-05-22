# Basket Service Clean Architecture + Vertical Slices Pilot PRD

> Companion to [PRD-Order-CleanArch-VSA-Pilot.md](PRD-Order-CleanArch-VSA-Pilot.md), [PRD-Product-CleanArch-VSA-Pilot.md](PRD-Product-CleanArch-VSA-Pilot.md), and [ADR 0011](../adr/0011-order-cleanarch-vsa-pilot.md). Propagation step #2 (Order pilot merged 2026-05-21; Product pilot in flight on `refactor/product-vsa`). Basket is brought forward from the end of the ADR 0011 candidate order — it is the smallest service in the repo, has no SQL/EF, no outbox, and publishes no integration events, so the pattern's *floor case* is validated here: does the layout still earn its keep when the deepest module changes from Order's pilot (outbox translation seam) and Product's pilot (rich-aggregate promotion) are both absent?
>
> Epic: [#152](https://github.com/daonhan/Nhamnhi/issues/152)

## Problem Statement

`Basket.Service` is organized by technical type, like every non-pilot service in the repo: HTTP routes live in `Endpoints/BasketApiEndpoints.cs`; the `CustomerBasket` aggregate and `BasketProduct` value object live in `Models/`; the two integration-event handlers (`OrderCreatedEventHandler`, `ProductPriceUpdatedEventHandler`) live in `IntegrationEvents/EventHandlers/`; the Redis repository (`RedisBasketStore`), the in-memory test repository (`InMemoryBasketStore`), and the QA seeder live in `Infrastructure/`. To understand "what happens when a product is added to a basket?" a developer must read the `MapPut` branch in `BasketApiEndpoints.cs`, follow the `IDistributedCache.GetStringAsync(productId)` call back to the `ProductPriceUpdatedEventHandler` that populated it, follow `IBasketStore.GetBasketByCustomerId` into the Redis impl, and reconstruct the feature from three folders.

Compared to Order and Product, Basket is the *simplest* service in the repo: no SQL, no EF, no outbox, no domain events, no integration events published, no saga participation, no auth requirement on its routes. That simplicity makes it the natural floor case for the VSA pilot — the question this PRD answers is whether the layout still earns its keep when the deepest module changes from the prior pilots are absent. If the answer is yes, the propagation pattern is validated for Redis-only services; if no, the team learns where the layout's value floor sits.

The team wants:

1. A codebase grouped by *what the application does* (features) rather than by technical type, matching the layout that ADR 0011 documents and the Product pilot is replicating.
2. Clear, enforceable Clean Architecture boundaries: Domain has no infrastructure dependencies; Features depend on Domain + Contracts; Infrastructure implements interfaces.
3. The Basket pilot to expose the *same* `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/` shape Order and Product use, so the runbook (`docs/runbooks/adding-a-new-slice.md`) is reused unchanged.
4. The same belt-and-suspenders boundary enforcement (NetArchTest + Roslyn analyzer) Order and Product use, ported to a per-service `Basket.Service.LayoutAnalyzer` and `Basket.Tests/Architecture/LayoutTests.cs`.
5. An explicit, documented divergence from Order/Product: the outbox translation seam (`IIntegrationMap<,>` + `DomainEventOutboxInterceptor`) is **not** introduced in Basket. Basket emits no integration events; introducing the seam would create dead infrastructure. The pilot proves the layout works without the seam, so future Redis-only or consumer-only services know they can skip it too.

## Solution

Apply the Clean Architecture + VSA layout from ADR 0011 to `Basket.Service` only, inside a single `Basket.Service.csproj` (no project split, except a sibling `Basket.Service.LayoutAnalyzer` analyzer sub-project). Reorganize source into:

- `Features/<Slice>/` — one folder per inbound trigger (HTTP route OR integration message). Each slice is self-contained: endpoint or event consumer, request DTOs, sealed handler class, slice-local DI extension. Existing planned slices: `GetBasket`, `CreateBasket`, `AddBasketProduct`, `DeleteBasketProduct`, `DeleteBasket`, `OrderCreated`, `ProductPriceUpdated`.
- `Domain/` — `CustomerBasket` aggregate (moved verbatim from `Models/`), `BasketProduct` value object, and `Abstractions/IBasketStore`. No Redis, no HTTP references. Aggregate stays light — the existing `AddBasketProduct` / `RemoveBasketProduct` / `BasketTotal` API is preserved; no promotion to rich-with-domain-events.
- `Contracts/Integration/` — `OrderCreatedEvent`, `ProductPriceUpdatedEvent` payload classes (cross-service contracts; payload unchanged). Even though Basket only consumes, inbound payloads land here to match Order/Product convention.
- `Infrastructure/Data/Redis/` — `RedisBasketStore`, `InMemoryBasketStore`, `CustomerBasketCacheModel`, `RedisExtensions`, `RedisOptions`.
- `Infrastructure/Seeding/` — `RedisQaSeederHostedService` (unchanged location).

Slice handlers are invoked through plain DI (constructor injection of the handler class into the endpoint or event consumer) — no MediatR, no in-house dispatcher. Each slice handler is `internal sealed` with one public `HandleAsync(...)` method. Endpoint methods are thin: they bind route/body parameters and delegate to the slice handler.

The two write slices that need cached product prices (`CreateBasket`, `AddBasketProduct`) keep their `IDistributedCache` dependency on the slice handler — no new `IProductPriceProvider` abstraction is introduced. The lookup is one line; an abstraction would be speculative.

The outbox translation seam from Order and Product (`IIntegrationMap<,>` + `DomainEventOutboxInterceptor`) is intentionally **omitted**. Basket emits no integration events. Introducing the seam would create dead code. ADR 0011 is composed by reference for the rest of the layout; this single, well-justified divergence is captured in the Implementation Decisions section and in the PR description.

The QA seeder (`RedisQaSeederHostedService`) stays in `Infrastructure/Seeding/`, exactly as today. It is operational plumbing, not a feature.

The custom OpenTelemetry meter view in `Program.cs` (`basket-size` histogram with explicit buckets) is preserved verbatim in the new manifest-style composition root.

Boundaries are enforced with both NetArchTest assertions (in `Basket.Tests/Architecture/LayoutTests.cs`) and a Roslyn `Basket.Service.LayoutAnalyzer` mirroring Product's analyzer. Tests are reshaped to mirror slices, with the aggregate-level unit tests in `Basket.Tests/Domain/CustomerBasketTests.cs` kept verbatim. Namespaces are renamed to match the new folder layout (`Basket.Service.Domain`, `Basket.Service.Features.<Slice>`, `Basket.Service.Contracts.Integration`, `Basket.Service.Infrastructure.Data.Redis`, `Basket.Service.Infrastructure.Seeding`).

The work lands as staged commits on a single branch `refactor/basket-vsa` and merges via one PR. No new ADR is filed — the pilot composes ADR 0011 by reference, and the runbook `docs/runbooks/adding-a-new-slice.md` is reused unchanged. The root `CLAUDE.md` Basket line is updated to reference ADR 0011 and the runbook. Propagation to remaining services (auth, inventory, payment, shipping, saga, api-gateway) is deferred to a follow-up ADR after the Basket pilot lands.

## User Stories

1. As a Basket service developer, I want to open a single folder to see everything the "add product to basket" feature does, so that I do not have to reconstruct the feature from three scattered folders.
2. As a Basket service developer, I want each slice to register its own dependencies via an `AddXxxSlice()` extension, so that adding a new feature is a drop-in change and `Program.cs` reads like a manifest.
3. As a Basket service developer, I want to add a new HTTP endpoint by creating one new `Features/<Name>/` folder, so that I never need to touch unrelated handlers or DTOs.
4. As a Basket service developer, I want to add a new integration-event consumer by creating one new `Features/<EventName>/` folder, so that event-driven features feel identical to HTTP features (consistent with Order's pilot).
5. As a Basket service developer, I want `Domain/CustomerBasket.cs` to contain the existing aggregate API (`AddBasketProduct`, `RemoveBasketProduct`, `BasketTotal`) unchanged, so that the layout migration cannot regress aggregate behavior. No new domain events are introduced; promotion is explicitly out of scope.
6. As a Basket service developer, I want write slices (`CreateBasket`, `AddBasketProduct`, `DeleteBasketProduct`, `DeleteBasket`) to load the aggregate through `IBasketStore`, mutate it via existing domain methods, and persist, so that the write path always goes through the aggregate.
7. As a Basket service developer, I want the read slice (`GetBasket`) to use `IBasketStore.GetBasketByCustomerId` directly without a separate CQRS-lite projection, because there is only one read and no child-collection projection benefit. The slice is still its own folder, but it skips the read/write split Order and Product use.
8. As a Basket service developer, I want the cached product-price lookup in `CreateBasket` and `AddBasketProduct` to live inside the slice handler (`Features/CreateBasket/Handler.cs`, `Features/AddBasketProduct/Handler.cs`), so that the slice owns its dependency on `IDistributedCache` without adding a new `IProductPriceProvider` abstraction for a one-line lookup.
9. As a Basket service maintainer, I want the outbox translation seam (`IIntegrationMap<,>` + `DomainEventOutboxInterceptor`) **omitted** from this pilot, with the omission documented in this PRD and the PR description, so that the pattern's floor case is validated and future Redis-only or consumer-only services know they can skip the seam too.
10. As a Basket service maintainer, I want NetArchTest rules that fail the test suite if `Domain` references infrastructure, if any slice references another slice, or if infrastructure leaks past Domain + Contracts, so that boundary violations are caught in CI rather than in code review.
11. As a Basket service maintainer, I want a Roslyn `Basket.Service.LayoutAnalyzer` (mirroring Order/Product) as a second guardrail beside NetArchTest, so that violations surface as compiler errors during development — not only when tests run.
12. As a Basket service contributor, I want the cross-slice sharing rule documented as "duplicate first, extract on third" with a NetArchTest rule forbidding slice-to-slice references, so that I do not accidentally create a hidden coupling between two slices.
13. As a Basket service contributor, I want namespaces to match the new folder layout (`Basket.Service.Domain`, `Basket.Service.Features.CreateBasket`, `Basket.Service.Contracts.Integration`, `Basket.Service.Infrastructure.Data.Redis`), so that I can grep for layer membership and analyzer rules can target namespaces.
14. As a Basket service contributor, I want `Basket.Tests` to mirror `Features/<Slice>/` while keeping `Domain/CustomerBasketTests.cs` separate, so that feature tests and aggregate unit tests are each easy to locate.
15. As a Basket service contributor, I want `RedisQaSeederHostedService` to remain under `Infrastructure/Seeding/`, not under `Features/`, so that operational plumbing does not pollute the feature manifest. `BasketQaSeederTests` stays at its current path under `Basket.Tests/Qa/`.
16. As a Basket service contributor, I want `MessagingProviderBootTests` to remain at the tests project root (or under `Basket.Tests/Infrastructure/`), so that cross-cutting messaging-provider boot validation is not tied to any single slice's tests.
17. As a Basket service contributor, I want the custom OpenTelemetry meter view in `Program.cs` (`basket-size` histogram with explicit buckets `[0, 1, 3, 5, 10, 25]`) preserved byte-identically in the manifest-style composition root, so that observability dashboards depending on that histogram do not regress.
18. As a reviewer, I want the pilot to land as staged commits on one branch and a single PR, with each commit building and tests passing, so that the refactor is bisectable and reviewable end-to-end.
19. As a reviewer, I want zero behavior change from the pilot — every existing `Basket.Tests` test passes after namespace updates only, all five HTTP routes return the same status codes and shapes, both integration-event consumers behave identically, the QA seeder seeds the same five personas, and Prometheus exposes the same counters and histogram — so that the layout migration cannot regress functional behavior.
20. As a release engineer, I want the pilot to leave `ECommerce.Shared` public API unchanged (no nupkg version bump, no consumer impact), so that other services are not affected.
21. As a release engineer, I want the pre-commit hook (`dotnet format`, `dotnet build`, Basket tests) to gate every commit on `refactor/basket-vsa`, so that the branch cannot accumulate partial-validation commits. **Basket tests run as part of the pre-commit hook itself** (per root `CLAUDE.md`) — every commit on this branch validates Basket end-to-end automatically.
22. As an architect, I want the Basket pilot to compose ADR 0011 by reference and reuse `docs/runbooks/adding-a-new-slice.md` unchanged, so that the runbook's reusability is validated by a third consumer (after Order and Product). No new ADR for Basket itself.
23. As an architect, I want propagation to remaining services (auth, inventory, payment, shipping, saga, api-gateway) to be a separate ADR after the Basket pilot lands and at least one review pass completes, so that propagation order can be informed by lessons from all three pilots.
24. As an AI-assisted contributor, I want the layout, namespaces, and architecture rules to be self-describing and analyzer-enforced (same as Order and Product), so that AI edits cannot silently drift across boundaries.
25. As a QA operator, I want the seeded QA personas (whatever set `RedisQaSeederHostedService` currently seeds) to continue seeding through the existing path, so that QA flows depending on those personas do not regress.
26. As an operator, I want the existing OpenTelemetry counters (`basket-updates`, `basket-products-added`, `basket-products-removed`) and the `basket-size` histogram to continue incrementing on the same conditions as today (one per successful create/update; products-added/removed counters on the same paths; histogram recorded after every write), so that operational dashboards do not break. After the refactor, the slice handler owns the counter calls.
27. As an operator, I want the Basket service's healthcheck (`/health`), Redis probe, RabbitMQ probe, Prometheus exporter endpoint, and OpenAPI document to continue working unchanged, so that orchestration and monitoring are not interrupted.
28. As an operator, I want the messaging provider switch (`Messaging:Provider` = `RabbitMq` default | `AzureServiceBus`) to continue working — `MessagingProviderBootTests` must keep passing — so that the cross-broker abstraction remains valid through the refactor.

## Implementation Decisions

### Pilot scope

- Pilot is `Basket.Service` only. No other service changes. Order pilot already merged; Product pilot in flight; propagation to remaining services handled by a follow-up ADR.
- Composes ADR 0011 by reference. No new ADR filed for Basket. Runbook `docs/runbooks/adding-a-new-slice.md` reused unchanged.
- Documented divergence from Order/Product: **no outbox seam** (Basket emits no integration events). This divergence is recorded in this PRD, in the PR description, and as a one-line note in the updated root `CLAUDE.md` Basket entry.

### Project shape

- Single `Basket.Service.csproj` is retained. No split into `Basket.Domain` / `Basket.Application` / `Basket.Infrastructure` projects.
- A separate `Basket.Service.LayoutAnalyzer` sub-project is added (mirroring `Order.Service.LayoutAnalyzer` / `Product.Service.LayoutAnalyzer`) for the Roslyn analyzer. Referenced by `Basket.Service` as an `Analyzer` package reference.
- Boundaries are enforced by namespace conventions + analyzer rules + architecture tests, not by csproj references.

### Folder topology

- `Features/<Slice>/` — one folder per inbound trigger. Slices:
  - `GetBasket` (HTTP `GET /{customerId}`)
  - `CreateBasket` (HTTP `POST /{customerId}`)
  - `AddBasketProduct` (HTTP `PUT /{customerId}`)
  - `DeleteBasketProduct` (HTTP `DELETE /{customerId}/{productId}`)
  - `DeleteBasket` (HTTP `DELETE /{customerId}`)
  - `OrderCreated` (integration message consumer for `OrderCreatedEvent`)
  - `ProductPriceUpdated` (integration message consumer for `ProductPriceUpdatedEvent`)
  Each owns its endpoint or consumer, request DTOs, sealed handler, slice DI extension. No mappers (no outbox seam).
- `Domain/` — `CustomerBasket` aggregate (moved verbatim), `BasketProduct` value object, `Domain/Abstractions/IBasketStore`. No Redis, no HTTP references. Aggregate API is identical to today.
- `Contracts/Integration/` — `OrderCreatedEvent`, `ProductPriceUpdatedEvent` payload classes (cross-service contracts; namespace `Basket.Service.Contracts.Integration`; payload unchanged).
- `Infrastructure/Data/Redis/` — `RedisBasketStore`, `InMemoryBasketStore`, `CustomerBasketCacheModel`, `RedisExtensions`, `RedisOptions`.
- `Infrastructure/Seeding/` — `RedisQaSeederHostedService` (location unchanged).

### Dispatch model

- No MediatR, no in-house mediator.
- Endpoints and integration-event consumers take their slice handler class via constructor injection (delegate-style minimal-API parameter binding from `[FromServices]` for endpoints; standard scoped DI for consumers) and call `HandleAsync(...)` directly.
- Slice handler classes are `internal sealed` with one public async method.

### Domain richness rule

- `CustomerBasket` stays light. Existing methods (`AddBasketProduct`, `RemoveBasketProduct`, `BasketTotal`) preserved verbatim. No new domain events, no new invariants, no rich-aggregate promotion in this pilot. This is an intentional difference from the Product pilot, where promotion *was* the deepest module change.
- Slice handlers are orchestration only: load aggregate via `IBasketStore`, call domain method(s), persist via `IBasketStore`. The read slice (`GetBasket`) is a one-liner pass-through to `IBasketStore.GetBasketByCustomerId`.

### CQRS-lite read split — skipped

- Basket has one read (`GET /{customerId}`) that returns the aggregate. No SQL projection benefit, no child collections to elide. The read slice (`Features/GetBasket/`) uses `IBasketStore` like the writes; it is its own folder but it does not project around the aggregate.
- This is an intentional divergence from Order (`GetOrder` + `ListOrders` use CQRS-lite) and Product (`GetProduct` + `ListProducts` use CQRS-lite). Documented here so future contributors know the pattern is optional when there is no projection benefit.

### Persistence

- Single `IBasketStore` abstraction lives in `Domain/Abstractions/` (moved from `Infrastructure/Data/`).
- `RedisBasketStore` (Redis impl) and `InMemoryBasketStore` (test impl) live in `Infrastructure/Data/Redis/`. The Redis cache model `CustomerBasketCacheModel` lives alongside.

### Outbox / event translation seam — **omitted**

- Basket emits no integration events. No `IIntegrationMap<,>`, no `DomainEventOutboxInterceptor`, no `Infrastructure/Outbox/` folder.
- No `IOutboxStore` registration. The shared `AddOutbox(...)` extension is not called. No outbox table is provisioned for Basket.
- This divergence is documented in this PRD (under this section), in the PR description, and as a one-line note in the updated root `CLAUDE.md` Basket entry. Future contributors know to skip the seam when their service emits no integration events.

### Internal ops endpoints — **none**

- Basket has no `InternalOutboxEndpoints`, no `/internal/outbox/failed`, no DLQ poller integration on the outbox side. Basket is a consumer-only service. The DLQ poller's existing handling of *consumer-side* DLQ messages (RabbitMQ dead-letter exchange) is unaffected by this refactor.

### Slice DI

- Each slice exposes a static class with `AddXxxSlice(this IServiceCollection)` extension. The extension registers the handler as scoped and any slice-specific options.
- Event-consumer slices register their consumer via the existing shared `AddEventHandler<TEvent, THandler>` infra inside their slice extension.
- `Program.cs` chains slice extensions: `services.AddGetBasketSlice().AddCreateBasketSlice().AddAddBasketProductSlice().AddDeleteBasketProductSlice().AddDeleteBasketSlice().AddOrderCreatedSlice().AddProductPriceUpdatedSlice()`. The shared infra extensions (`AddPlatformEventBus`, `AddPlatformSubscriberService`, `AddRedisCache`, `AddQaSeeding`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddPlatformOpenApi`) remain.
- The `IBasketStore` registration (`AddScoped<IBasketStore, RedisBasketStore>`) lives in a small `AddBasketInfrastructure()` helper inside `Infrastructure/` (or remains in `Program.cs` as the only non-slice scoped registration — chosen during implementation, whichever reads more cleanly).
- The custom OpenTelemetry meter view in `AddPlatformObservability` (the `basket-size` histogram view) is preserved verbatim.

### Namespaces

- Renamed to match folders:
  - `Basket.Service.Domain`, `Basket.Service.Domain.Abstractions`
  - `Basket.Service.Features.<Slice>` (one per slice)
  - `Basket.Service.Contracts.Integration`
  - `Basket.Service.Infrastructure.Data.Redis`, `Basket.Service.Infrastructure.Seeding`
- Existing `Basket.Service.IntegrationEvents` and `Basket.Service.IntegrationEvents.EventHandlers` namespaces are removed when their types move into `Contracts/Integration/` and `Features/<EventName>/` respectively.
- Existing `Basket.Service.Endpoints`, `Basket.Service.ApiModels`, `Basket.Service.Models` namespaces are removed when their types move into `Features/<Slice>/` and `Domain/`.

### Cross-slice sharing rule

- Rule of three: duplicate freely between slices; extract to `Domain/` (behavioral) or `Features/Shared/` (helper) only on the third use.
- The cached-price lookup pattern appears in two slices (`CreateBasket`, `AddBasketProduct`). It is intentionally duplicated. If a third write slice ever needs the same lookup, extract then — not before.
- NetArchTest forbids `Basket.Service.Features.<X>` referencing `Basket.Service.Features.<Y>` for any `X != Y`.

### Boundary enforcement

- NetArchTest rules in `Basket.Tests/Architecture/LayoutTests.cs` (port verbatim from Order's `LayoutTests.cs` with namespace swap):
  - `Domain` types must not reference `Basket.Service.Infrastructure.*` or `Basket.Service.Features.*`.
  - `Features.<X>` types must not reference `Features.<Y>` for distinct slices.
  - `Infrastructure` types may reference `Domain` + `Contracts`, but not `Features`.
  - `Contracts` types reference nothing internal.
- Roslyn `Basket.Service.LayoutAnalyzer` sub-project (port verbatim from Product's `LayoutAnalyzer` with namespace prefix swap) implements the same four rules as compile-time errors.
- Both guardrails must fail on an intentional spike before the enforcement phase is marked done. Spike-and-revert recorded in PR description.

### QA seeder

- `RedisQaSeederHostedService` stays at `Infrastructure/Seeding/RedisQaSeederHostedService.cs`. It is operational plumbing, not a feature.
- `BasketQaSeederTests` stays at `Basket.Tests/Qa/BasketQaSeederTests.cs`.

### Messaging-provider boot test

- `Basket.Tests/IntegrationEvents/MessagingProviderBootTests.cs` is renamed/moved to `Basket.Tests/Infrastructure/MessagingProviderBootTests.cs` (or kept at its current root-of-tests location) and its `using` directives updated for the namespace renames. It continues to exercise both `Messaging:Provider` values.

### Shared library

- `ECommerce.Shared` public API is unchanged. No nupkg version bump. The pilot composes existing `AddPlatformEventBus`, `AddPlatformSubscriberService`, `AddEventHandler`, `AddRedisCache`, `AddQaSeeding`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddRedisProbe`, `AddRabbitMqProbe`, `AddPlatformOpenApi`.

### Validation

- Out of scope. Existing absence of `FluentValidation` / `DataAnnotations` is preserved. Same as Order and Product.

### Auth

- Out of scope. Existing absence of authentication on Basket routes is preserved. Same as today (Basket routes are anonymous).

### Rollout

- Branch `refactor/basket-vsa`. Staged commits land in this order, each green:
  1. **Scaffold** — add NetArchTest project dependency to `Basket.Tests`; scaffold `Basket.Service.LayoutAnalyzer` sub-project (rules disabled / warning-only); author `Basket.Tests/Architecture/LayoutTests.cs` with skipped rules.
  2. **Layout move (Domain + Contracts)** — move `CustomerBasket`, `BasketProduct` into `Domain/`; move `IBasketStore` into `Domain/Abstractions/`; move `OrderCreatedEvent`, `ProductPriceUpdatedEvent` into `Contracts/Integration/`; rename namespaces. No behavior change.
  3. **Layout move (Infrastructure)** — move `RedisBasketStore`, `InMemoryBasketStore`, `CustomerBasketCacheModel`, `RedisExtensions`, `RedisOptions` into `Infrastructure/Data/Redis/`; keep `RedisQaSeederHostedService` in `Infrastructure/Seeding/`. Rename namespaces.
  4. **Extract HTTP slices** — one commit per slice or one commit per cluster (chosen by reviewer feedback): `GetBasket`, `CreateBasket`, `AddBasketProduct`, `DeleteBasketProduct`, `DeleteBasket`. Each slice gets its own folder + handler + endpoint + slice DI extension. `BasketApiEndpoints.RegisterEndpoints` is dissolved; each slice's extension registers its own route.
  5. **Extract event-consumer slices** — `OrderCreated`, `ProductPriceUpdated`. Each gets its own folder + handler + slice DI extension that wires `AddEventHandler<TEvent, THandler>`.
  6. **Program.cs manifest** — `Program.cs` becomes a fluent chain of `services.AddXxxSlice()` calls plus the existing shared infra extensions. The custom OpenTelemetry meter view is preserved. Delete now-empty `Endpoints/`, `ApiModels/`, `Models/`, `IntegrationEvents/` folders.
  7. **Test reshape** — `Basket.Tests/Endpoints/BasketApiEndpointsTests.cs` is split into `Basket.Tests/Features/<Slice>/*Tests.cs` files mirroring the slice structure. `CustomerBasketTests` stays in `Basket.Tests/Domain/`. `MessagingProviderBootTests` moves to `Basket.Tests/Infrastructure/`. `BasketQaSeederTests` stays in `Basket.Tests/Qa/`.
  8. **Enforcement** — unskip NetArchTest rules; enable `Basket.Service.LayoutAnalyzer` rules as errors. Demonstrate spike-and-revert (introduce one cross-boundary `using`, confirm both NetArchTest fails and the analyzer raises a build error, revert before merge). Record demonstration in PR description.
  9. **Docs** — update root `CLAUDE.md` Basket line to reference ADR 0011 and the runbook. Add a one-line note that Basket does not introduce the outbox seam (documented divergence). No new ADR. No new runbook.
- Single PR for review. Pre-commit hook (`dotnet husky run --group pre-commit`, which runs `dotnet format`, `dotnet build`, and Basket tests) gates every commit. No `--no-verify`. No `Hooks-Deferred:` / `Validation-Deferred:` footers.

## Testing Decisions

### Test philosophy

- A good test verifies external behavior of a module through its public interface, not internal implementation details.
- Refactor produces zero behavior change. Every existing `Basket.Tests` test passes after namespace updates required by the rename.
- New tests are added only for the architecture rules themselves (`Basket.Tests/Architecture/LayoutTests.cs`). No new feature tests, no new aggregate tests, because no behavior is added or promoted.

### Modules to test

- **`CustomerBasket` aggregate (unchanged tests)** — existing `Basket.Tests/Domain/CustomerBasketTests.cs` covers `AddBasketProduct`, `RemoveBasketProduct`, `BasketTotal` invariants. Kept verbatim, only namespace touched.
- **Per-slice handler tests** — existing `Basket.Tests/Endpoints/BasketApiEndpointsTests.cs` is split into `Basket.Tests/Features/<Slice>/*Tests.cs` files mirroring the slice structure, without behavioral changes. Tests continue to construct handlers with mocked `IBasketStore` + mocked `IDistributedCache` + a `MetricFactory` test double, exactly as today.
- **Integration-event consumer tests** — if `Basket.Tests/IntegrationEvents/*` files exist for `OrderCreatedEventHandler` and `ProductPriceUpdatedEventHandler`, they move to `Basket.Tests/Features/OrderCreated/` and `Basket.Tests/Features/ProductPriceUpdated/` respectively, without behavioral changes.
- **Messaging-provider boot test** — `MessagingProviderBootTests` migrates to `Basket.Tests/Infrastructure/` (or stays at the tests-project root), continues to exercise both `Messaging:Provider` values, continues to use `WebApplicationFactory<Program>`.
- **QA seeder test** — `BasketQaSeederTests` stays under `Basket.Tests/Qa/`, unchanged.
- **OpenTelemetry counters + histogram** — existing endpoint tests that listen on the meter (verifying counter increments and histogram recordings) move with their slice. The observed metric names and conditions are unchanged.
- **`Basket.Tests/Architecture/LayoutTests.cs`** — new NetArchTest rules tests that act as the executable specification of the boundary policy. Mirror Order's `LayoutTests.cs` rules verbatim with namespace swap. Fail if any future contributor (human or AI) introduces a cross-boundary reference.

### Prior art in the codebase

- `Basket.Tests/Domain/CustomerBasketTests.cs` — existing aggregate-level unit tests. Pattern of `Given_When_Then` underscored display names is preserved (`CA1707` suppressed via `Directory.Build.props`).
- `Basket.Tests/Endpoints/BasketApiEndpointsTests.cs` — existing endpoint unit tests using `NSubstitute` for `IBasketStore` and `IDistributedCache` and `MetricFactory` for OpenTelemetry assertions. The split into per-slice test files preserves the same construction patterns.
- `Basket.Tests/IntegrationEvents/MessagingProviderBootTests.cs` — existing `WebApplicationFactory<Program>` integration test using a sealed inner factory class. Pattern preserved on relocation.
- `Order.Tests/Architecture/LayoutTests.cs` — direct prior art for the NetArchTest rules. Port verbatim with namespace swap.
- `Order.Service.LayoutAnalyzer/LayoutAnalyzer.cs` and `Product.Service.LayoutAnalyzer/LayoutAnalyzer.cs` — direct prior art for the Roslyn analyzer. New `Basket.Service.LayoutAnalyzer/LayoutAnalyzer.cs` is a copy-paste with namespace prefix swap.
- Pre-commit hook (`dotnet husky run --group pre-commit`) enforces `dotnet format --verify-no-changes`, `dotnet build --no-restore`, and **Basket tests** on every commit. Basket is in the unique position that its own test suite is the one the pre-commit hook runs — so every commit on `refactor/basket-vsa` validates Basket end-to-end automatically.

## Out of Scope

- Refactoring any other service (auth, inventory, payment, shipping, saga, api-gateway, product if not yet merged). Order is done, Product is in flight; remaining propagation is a follow-up ADR after Basket lands.
- Modifying `ECommerce.Shared`. The pilot composes existing extensions only. No nupkg version bump.
- Adding request validation (FluentValidation or DataAnnotations). Listed as a follow-up.
- Adding authentication to Basket routes. The existing anonymous-routes posture is preserved.
- Introducing MediatR or any mediator-style dispatcher.
- Splitting `Basket.Service.csproj` into multiple projects (the analyzer sub-project is a separate analyzer assembly, not an application-tier split).
- Introducing the outbox translation seam (`IIntegrationMap<,>` + `DomainEventOutboxInterceptor`). Basket emits no integration events; the seam would be dead code. Documented divergence from Order and Product.
- Introducing a CQRS-lite read/write split. Basket has one read with no projection benefit. Documented divergence from Order and Product.
- Promoting `CustomerBasket` to a richer aggregate with domain events. Aggregate stays as it is. Documented difference from the Product pilot, where promotion *was* the deepest module change.
- Extracting a new `IProductPriceProvider` abstraction for the cached-price lookup. The lookup stays inside the slice handlers.
- Changing the Redis cache key schema or the `CustomerBasketCacheModel` shape. No data migration.
- Changing integration event payload contracts (`OrderCreatedEvent`, `ProductPriceUpdatedEvent`). Only their location (folder + namespace) moves.
- Changing the outbox table, dispatcher, or retry/DLQ behavior in `ECommerce.Shared.Infrastructure.Outbox` (Basket doesn't use it; this is doubly out of scope).
- Changing `BasketApiEndpoints`' public HTTP routes, response shapes, status codes, or auth requirements. The five existing routes are preserved byte-identically.
- Changing the QA seeder behavior or seeded personas. `RedisQaSeederHostedService` is preserved verbatim.
- Changing the custom OpenTelemetry meter view in `Program.cs` (`basket-size` histogram bucket boundaries). Preserved verbatim.
- Changing CI/CD pipelines, Docker Compose, Kubernetes manifests, or Azure pipelines.
- Performance optimization. The pilot is structural, not performance-driven.
- Filing a new ADR. The pilot composes ADR 0011 by reference.
- Writing a new runbook. `docs/runbooks/adding-a-new-slice.md` is reused unchanged.

## Further Notes

- Basket was chosen as the **third pilot** (after Order and Product) for these reasons:
  1. Basket is the *simplest* service in the repo with non-trivial inbound triggers (5 HTTP + 2 events). Validating the layout on the simplest service answers "is the layout still worth it when the deepest module changes are absent?" — the floor case for the pattern's value.
  2. Basket has no SQL, no EF, no outbox, no integration events emitted, no saga participation. None of the Order pilot's deepest changes (outbox translation seam) and none of the Product pilot's deepest changes (rich-aggregate promotion) apply here. What's left is pure relocation + the manifest-style composition root + boundary enforcement.
  3. Basket's pre-commit hook *is* its own test suite. Every commit on `refactor/basket-vsa` validates Basket end-to-end via the hook itself — no manual `dotnet test` step is required between commits, which makes the refactor unusually low-friction.
- The documented divergence from Order/Product — **no outbox seam** — is the most important learning this pilot captures. It tells future contributors: when your service emits no integration events, skip the seam. The layout still earns its keep on Redis-only consumer-only services because of the folder-locality + boundary-enforcement benefits alone.
- The second documented divergence — **no CQRS-lite read split** — tells future contributors: when there's one read with no projection benefit, the read slice is still its own folder but it just calls the store. The split is a tool, not a requirement.
- The third documented difference — **no rich-aggregate promotion** — is not a divergence so much as a non-event: `CustomerBasket` was already a reasonable light aggregate. Order and Product had to promote; Basket doesn't.
- NetArchTest + Roslyn analyzer redundancy carries over from Order and Product. The "belt + suspenders" choice is justified by the AI-assisted contribution model — violations need to surface at the earliest possible moment.
- The "duplicate first, extract on third" rule remains load-bearing. With 7 slices in Basket and one obvious duplication candidate (the cached-price lookup in `CreateBasket` and `AddBasketProduct`), the temptation to extract on the *second* use will be present. The PRD and the PR description explicitly call out the duplication and explicitly defer extraction.
- After Basket lands and at least one follow-up review pass on any of the three pilots, a separate ADR will propose propagation to the remaining services. Candidate order (revised from ADR 0011 / Product PRD follow-up lists): inventory (saga participant, similar shape to Order) → payment (saga participant) → shipping → saga (orchestrator, last to validate the layout generalizes to non-CRUD services) → auth → api-gateway (different shape entirely; gateway service is YARP/Ocelot composition rather than a domain service, and may not warrant the layout at all — to be decided in the propagation ADR).
- Behavioral guidance from root `CLAUDE.md` applies: surgical changes only, no improving adjacent code, match existing style, push back on over-engineering. The Basket pilot is the *smallest* of the three by line count and carries no substantive design changes — it is almost pure relocation + namespace renames + the analyzer/test-rule additions.
