# Product Service Clean Architecture + Vertical Slices Pilot PRD

> Companion to [PRD-Order-CleanArch-VSA-Pilot.md](PRD-Order-CleanArch-VSA-Pilot.md) and [ADR 0011](../adr/0011-order-cleanarch-vsa-pilot.md). Propagation step #1 (out of: inventory → payment → shipping → saga → product → auth → basket per ADR 0011 follow-up list — Product brought forward as the smallest, lowest-risk consumer service to validate the pattern on a non-saga participant before tackling saga participants).
>
> Epic: [#152](https://github.com/daonhan/Nhamnhi/issues/152)

## Problem Statement

The `Product.Service` codebase shares the same technical-type folder organization as every other service in this repo (except the now-pilot Order service): endpoints in `Endpoints/`, domain types in `Models/`, integration event payloads in `IntegrationEvents/`, persistence in `Infrastructure/Data/`. To understand "what happens when a product price is updated?" a developer must read `Endpoints/ProductApiEndpoints.cs`, find the `MapPut` branch, follow the `IProductStore` call into `Infrastructure/Data/EntityFramework/ProductContext.cs`, follow the `outboxStore.AddOutboxEvent` call to find the integration event in `IntegrationEvents/ProductPriceUpdatedEvent.cs`, and reconstruct the feature mentally. The `Product` domain class is anemic — public setters, no invariants, no encapsulation — so price-change emission lives inside the HTTP endpoint and could be bypassed by any future caller mutating `Product.Price` directly. Boundaries between domain, application, and infrastructure exist only as conventions; nothing enforces them, so they erode silently under AI-assisted edits.

The Order pilot (PR #162, merged 2026-05-21) proved a Clean Architecture + Vertical Slice Architecture (VSA) layout on a richer service with multiple inbound triggers, outbox translation, and saga participation. Propagation to a **simpler** consumer service is the next step: it validates that the pattern generalizes downward — that a service with no saga participation, no integration-event consumers, and an anemic domain still benefits from the layout and that the runbook is followable for a contributor not deep in the Order pilot's context.

The team wants:

1. A codebase grouped by *what the application does* (features) rather than by technical type, matching the layout that ADR 0011 documents.
2. Clear, enforceable Clean Architecture boundaries: Domain has no infrastructure dependencies; Features depend on Domain + Contracts; Infrastructure implements interfaces.
3. The Product pilot to expose the *same* `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/` shape Order uses, so the runbook (`docs/runbooks/adding-a-new-slice.md`) is reused without modification.
4. The anemic `Product` class promoted to a rich aggregate with encapsulated state and domain events, so that integration-event emission becomes a domain concern rather than an endpoint concern — the deepest module change in this pilot.
5. A new generic `DomainEventOutboxInterceptor` seam introduced in Product (Product has no `Translate` switch like Order had — today it calls `outboxStore.AddOutboxEvent` directly from the endpoint), proving the seam works on a service that did not previously have one.
6. The same belt-and-suspenders boundary enforcement (NetArchTest + Roslyn analyzer) Order uses, ported to a per-service `Product.Service.LayoutAnalyzer` and `Product.Tests/Architecture/LayoutTests.cs`.

## Solution

Apply the Clean Architecture + VSA layout from ADR 0011 to `Product.Service` only, inside a single `Product.Service.csproj` (no project split). Reorganize source into:

- `Features/<Slice>/` — one folder per inbound HTTP route. Each self-contained: endpoint, request/response DTOs, slice-local sealed handler class, slice-local DI registration extension, and (if the slice produces an integration event) a co-located domain-event-to-integration-event mapper.
- `Domain/` — `Product` aggregate (promoted from anemic to rich), `ProductType` reference entity, domain events (`ProductCreatedDomainEvent`, `ProductPriceChangedDomainEvent`), `IDomainEvent`, `Entity` base, and `Abstractions/IProductStore`. No EF, no HTTP references.
- `Contracts/Integration/` — `ProductCreatedEvent`, `ProductPriceUpdatedEvent` payload classes (location/namespace move only; contract unchanged).
- `Infrastructure/Data/EntityFramework/` — `ProductContext`, `EfProductStore` (new impl of `IProductStore`, separated from the DbContext), EF configurations, seed data.
- `Infrastructure/Outbox/` — generic `DomainEventOutboxInterceptor`, `IIntegrationMap<TDomainEvent, TIntegrationEvent>` abstraction, `InternalOutboxEndpoints` (ops surface, `RequireService`).
- `Migrations/` — unchanged; `generated_code = true`.

Slice handlers are invoked through plain DI (constructor injection of the handler class into the endpoint) — no MediatR, no in-house dispatcher. Read slices (`GetProduct`, new `ListProducts`) project directly from `ProductContext` to response DTOs (CQRS-lite). Write slices (`CreateProduct`, `UpdateProduct`) go through the rich `Product` aggregate via `IProductStore`. The aggregate raises domain events on state transitions (creation; price change when new price differs from current price). A generic `DomainEventOutboxInterceptor` resolves per-domain-event mappers via DI and writes the translated integration event to the outbox. Endpoint code no longer touches `IOutboxStore`. Boundaries are enforced with both NetArchTest assertions (in `Product.Tests/Architecture/LayoutTests.cs`) and a Roslyn `Product.Service.LayoutAnalyzer` mirroring Order's analyzer. Tests are reshaped to mirror slices, with aggregate-level unit tests in `Product.Tests/Domain/`. Namespaces are renamed to match the new folder layout. The work lands as staged commits on a single branch `refactor/product-vsa` and merges via one PR. No new ADR is filed — the pilot composes ADR 0011 by reference, and the runbook `docs/runbooks/adding-a-new-slice.md` is reused unchanged. Propagation to remaining services (inventory, payment, shipping, saga, auth, basket) is deferred to a follow-up ADR after both Order and Product pilots have at least one review pass.

## User Stories

1. As a Product service developer, I want to open a single folder to see everything the "create product" feature does, so that I do not have to reconstruct the feature from four scattered folders.
2. As a Product service developer, I want each slice to register its own dependencies via an `AddXxxSlice()` extension, so that adding a new feature is a drop-in change and `Program.cs` reads like a manifest.
3. As a Product service developer, I want to add a new HTTP endpoint by creating one new `Features/<Name>/` folder, so that I never need to touch unrelated handlers or DTOs.
4. As a Product service developer, I want `Domain/Product.cs` to encapsulate state transitions (creation, price change) and emit domain events from those transitions, so that integration-event emission cannot be silently bypassed by an endpoint that mutates `Product.Price` directly.
5. As a Product service developer, I want write slices to load the aggregate through `IProductStore`, mutate it via domain methods (`ChangePrice`, `Rename`, etc.), and persist, so that the write path always enforces invariants and emits the correct domain events.
6. As a Product service developer, I want read slices (`GetProduct`, `ListProducts`) to project directly from EF to response DTOs, so that reads do not pay the cost of hydrating the aggregate and including child collections they don't need.
7. As a Product service developer, I want each slice that emits an integration event to co-locate the domain-event-to-integration-event mapping with the slice that produces it, so that "what does this slice publish?" is answerable by reading one folder.
8. As a Product service maintainer, I want `ProductContext` to contain only persistence and unit-of-work logic — no event-translation code, no domain-event-aware logic — so that the DbContext stays a deep, single-purpose module.
9. As a Product service maintainer, I want a generic `DomainEventOutboxInterceptor` that resolves per-event mappers via DI, so that adding a new domain event requires only adding a new mapper, not touching any central switch or endpoint.
10. As a Product service maintainer, I want `IProductStore` separated from `ProductContext` (today the DbContext implements the store interface), so that `Domain/Abstractions/IProductStore` is defined without an EF dependency and the store impl lives in `Infrastructure/Data/EntityFramework/EfProductStore.cs`.
11. As a Product service maintainer, I want NetArchTest rules that fail the test suite if `Domain` references infrastructure, if any slice references another slice, or if infrastructure leaks past Domain + Contracts, so that boundary violations are caught in CI rather than in code review.
12. As a Product service maintainer, I want a Roslyn `Product.Service.LayoutAnalyzer` (mirroring `Order.Service.LayoutAnalyzer`) as a second guardrail beside NetArchTest, so that violations surface as compiler errors during development — not only when tests run.
13. As a Product service contributor, I want the cross-slice sharing rule documented as "duplicate first, extract on third" with a NetArchTest rule forbidding slice-to-slice references, so that I do not accidentally create a hidden coupling between two slices.
14. As a Product service contributor, I want namespaces to match the new folder layout (`Product.Service.Domain`, `Product.Service.Features.CreateProduct`, `Product.Service.Contracts.Integration`, `Product.Service.Infrastructure.Data.EntityFramework`), so that I can grep for layer membership and analyzer rules can target namespaces.
15. As a Product service contributor, I want `Product.Tests` to mirror `Features/<Slice>/` while keeping `Domain/` aggregate tests separate, so that feature tests and domain unit tests are each easy to locate.
16. As a Product service contributor, I want `InternalOutboxEndpoints` (DLQ-poller ops surface) to live under `Infrastructure/Outbox/`, not under `Features/`, so that operational plumbing does not pollute the feature manifest.
17. As a reviewer, I want the pilot to land as staged commits on one branch and a single PR, with each commit building and tests passing, so that the refactor is bisectable and reviewable end-to-end.
18. As a reviewer, I want behavioral changes to be minimal and localized — every existing `Product.Tests` test passes (with namespace updates only), and the only new behavior is (a) the new `ListProducts` endpoint and (b) the relocation of integration-event emission from the endpoint to the aggregate via the interceptor — so that the layout migration cannot regress functional behavior on existing paths.
19. As a Product service developer, I want a new `ListProducts` slice (GET `/`) added during the pilot, so that the CQRS-lite read pattern has two read slices to demonstrate the shape (matching Order's `GetOrder` + `ListOrders` pair).
20. As a release engineer, I want the pilot to leave `ECommerce.Shared` public API unchanged, so that other services are not forced to consume a breaking shared package version.
21. As a release engineer, I want the pre-commit hook (`dotnet format`, `dotnet build`, Basket tests) to gate every commit on the refactor branch, so that the branch cannot accumulate partial-validation commits. Product tests run manually before pushing per the sandbox policy in root `CLAUDE.md`.
22. As an architect, I want the pilot to compose ADR 0011 (Order pilot) by reference and reuse the `adding-a-new-slice.md` runbook unchanged, so that the runbook's reusability is validated by a second consumer. No new ADR is required for Product itself; propagation to remaining services becomes a separate ADR after Product lands.
23. As an architect, I want propagation to remaining services (inventory, payment, shipping, saga, auth, basket) to be a separate ADR after the Product pilot lands and at least one review pass completes, so that propagation order can be informed by lessons from both pilots.
24. As an AI-assisted contributor, I want the layout, namespaces, and architecture rules to be self-describing and analyzer-enforced (same as Order), so that AI edits cannot silently drift across boundaries.
25. As an operator, I want the DLQ poller's call to `/internal/outbox/failed` (gated by `RequireService`) to continue working after the refactor, so that DLQ ingestion is not interrupted.
26. As an operator, I want QA seed data (`ProductHappy`, `ProductDecline`, `ProductZeroStock`, `ProductLowStock`, `ProductRestockTarget`) to continue seeding through the existing `ProductConfiguration.HasData` path, so that QA flows depending on those personas do not regress.
27. As an operator, I want `ProductTypes` reference data (Shoes, Shorts) to continue seeding through the existing `ProductTypeConfiguration.HasData` path, so that product-type FK constraints remain satisfied.
28. As a Product service developer, I want the `MetricFactory.Counter("products-created")` and `MetricFactory.Counter("product-price-updates")` counters to continue incrementing on the same conditions as today (one per successful create; one per successful update with `priceChanged == true`), so that operational dashboards do not break. The slice handler owns the counter calls after the refactor.

## Implementation Decisions

### Pilot scope

- Pilot is `Product.Service` only. No other service changes. Order pilot already merged; propagation to remaining services handled by a follow-up ADR.
- Composes ADR 0011 by reference. No new ADR filed for Product. Runbook `docs/runbooks/adding-a-new-slice.md` reused unchanged.

### Project shape

- Single `Product.Service.csproj` is retained. No split into `Product.Domain` / `Product.Application` / `Product.Infrastructure` projects.
- A separate `Product.Service.LayoutAnalyzer` sub-project is added (mirroring `Order.Service.LayoutAnalyzer`) for the Roslyn analyzer. Referenced by `Product.Service` as an `Analyzer` package reference.
- Boundaries are enforced by namespace conventions + analyzer rules + architecture tests, not by csproj references.

### Folder topology

- `Features/<Slice>/` — one folder per HTTP route. Slices: `CreateProduct`, `GetProduct`, `ListProducts` (new), `UpdateProduct`. Each owns its endpoint, request/response DTOs, sealed handler, slice DI extension, and (if it emits an integration event) its domain-event-to-integration-event mapper.
- `Domain/` — `Product` aggregate (rich after promotion), `ProductType` (reference entity), `Domain/Events/ProductCreatedDomainEvent`, `Domain/Events/ProductPriceChangedDomainEvent`, `IDomainEvent`, `Entity` base, and `Domain/Abstractions/IProductStore`. No EF, no HTTP references.
- `Contracts/Integration/` — `ProductCreatedEvent`, `ProductPriceUpdatedEvent` (payload classes; cross-service contract unchanged).
- `Infrastructure/Data/EntityFramework/` — `ProductContext` (persistence only, no `IProductStore` impl), `EfProductStore` (new file implementing `IProductStore`), `ProductConfiguration`, `ProductTypeConfiguration`, `ProductContextSeed`, `ProductContextDesignTimeFactory`, `EntityFrameworkExtensions`.
- `Infrastructure/Outbox/` — `IIntegrationMap<TDomainEvent, TIntegrationEvent>`, generic `DomainEventOutboxInterceptor`, `InternalOutboxEndpoints` (ops surface).
- `Migrations/` — unchanged; `generated_code = true`.

### Dispatch model

- No MediatR, no in-house mediator.
- Endpoints take their slice handler class via constructor injection (delegate-style minimal-API parameter binding from `[FromServices]`) and call `HandleAsync(...)` directly.
- Slice handler classes are `sealed`, internal, and have one public async method.

### Domain richness rule (significant change vs current state)

- Rich domain: `Product` aggregate owns invariants and state transitions.
  - Constructor encapsulates creation, raises `ProductCreatedDomainEvent`.
  - `ChangePrice(decimal newPrice)` updates price only when value differs; on change raises `ProductPriceChangedDomainEvent`.
  - `Rename(string name)` etc. — methods cover the existing PUT mutations (name, description, product type).
  - Public setters removed; properties become `{ get; private set; }` (or init-only where the EF mapping permits).
- Slice handlers are orchestration only: load aggregate via `IProductStore`, call domain method(s), persist. Endpoints no longer reference `IOutboxStore` directly.
- Read slices bypass the aggregate and project directly from `ProductContext` to response DTOs.

### Persistence

- Single `IProductStore` abstraction lives in `Domain/Abstractions/`.
- EF implementation `EfProductStore` lives in `Infrastructure/Data/EntityFramework/`. Today `ProductContext` itself implements `IProductStore` — this is decoupled so the DbContext is persistence-only.
- `EfProductStore` exposes `GetById`, `Add`, `Update` (or the renamed equivalents needed by the new handlers). Maintains the `Include(p => p.ProductType)` behavior in `GetById` so the existing response shape is unchanged.
- `ProductContext` ceases to implement `IProductStore`. EF entity-framework configurations and seed data stay where they are.

### Outbox / event translation seam

- A new abstraction `IIntegrationMap<TDomainEvent, TIntegrationEvent>` is introduced under `Infrastructure/Outbox/`.
- Each producing slice ships one mapper implementation co-located with the slice (e.g., `Features/CreateProduct/ProductCreatedIntegrationMap.cs`, `Features/UpdateProduct/ProductPriceUpdatedIntegrationMap.cs`).
- A generic `DomainEventOutboxInterceptor` (registered as an EF interceptor on `ProductContext`) resolves mappers by domain-event runtime type via DI and calls `IOutboxStore.AddOutboxEvent` with the translated integration event during `SaveChangesAsync`.
- Unmapped domain-event type fails fast with a descriptive `InvalidOperationException` naming the unmapped type — mirroring Order's pattern.
- After the refactor, endpoint code does **not** call `outboxStore.AddOutboxEvent`. The aggregate raises domain events; the interceptor translates and persists them.

### Slice DI

- Each slice exposes a static class with `AddXxxSlice(this IServiceCollection)` extension. The extension registers the handler as scoped, any slice-specific options, and the slice's `IIntegrationMap<,>` if any.
- `Program.cs` chains slice extensions: `services.AddCreateProductSlice().AddGetProductSlice().AddListProductsSlice().AddUpdateProductSlice()`. The shared infra extensions (`AddSqlServerDatastore`, `AddOutbox`, `AddPlatformEventBus`, `AddPlatformEventPublisher`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddJwtAuthentication`, `AddRequireServicePolicy`, `AddPlatformOpenApi`) remain.
- `DomainEventOutboxInterceptor` is registered once in `Program.cs` (or in a small `AddProductOutbox()` helper inside `Infrastructure/Outbox/`) along with EF's `DbContextOptions` interceptor wiring.

### Namespaces

- Renamed to match folders: `Product.Service.Domain`, `Product.Service.Domain.Events`, `Product.Service.Domain.Abstractions`, `Product.Service.Features.<Slice>`, `Product.Service.Contracts.Integration`, `Product.Service.Infrastructure.Data.EntityFramework`, `Product.Service.Infrastructure.Outbox`.
- Existing `Product.Service.IntegrationEvents` namespace is removed when its types move into `Contracts/Integration/`.

### Cross-slice sharing rule

- Rule of three: duplicate freely between slices; extract to `Domain/` (behavioral) or `Features/Shared/` (helper) only on the third use.
- NetArchTest forbids `Product.Service.Features.<X>` referencing `Product.Service.Features.<Y>` for any `X != Y`.

### Boundary enforcement

- NetArchTest rules in `Product.Tests/Architecture/LayoutTests.cs`:
  - `Domain` types must not reference `Product.Service.Infrastructure.*` or `Product.Service.Features.*`.
  - `Features.<X>` types must not reference `Features.<Y>` for distinct slices.
  - `Infrastructure` types may reference `Domain` + `Contracts`, but not `Features`.
  - `Contracts` types reference nothing internal.
- Roslyn `Product.Service.LayoutAnalyzer` (new analyzer sub-project) implements the same four rules as compile-time errors. Mirrors `Order.Service.LayoutAnalyzer`.
- Both guardrails must fail on an intentional spike before the enforcement phase is marked done. Spike-and-revert recorded in PR description.

### Internal ops endpoints

- `InternalOutboxEndpoints` moves from `Endpoints/` to `Infrastructure/Outbox/InternalOutboxEndpoints.cs`.
- Wiring done from `Program.cs` after slice registration.
- `RequireService` policy gate on `/internal/outbox/failed` preserved.

### New `ListProducts` slice (carve-out from zero-behavior-change)

- New HTTP route `GET /` returning a list of products. Public surface grows by one route.
- Response DTO mirrors `GetProductResponse` shape, projected directly from `ProductContext` (no aggregate hydration, no `Include` of unused children).
- New endpoint requires no auth (matches the existing `GetProduct` `MapGet` which is unauthenticated today). If existing service-wide auth policy applies, the new slice matches it.
- This is the **only** intentional behavior addition in the pilot. All other behavior is preserved byte-identically.

### Shared library

- `ECommerce.Shared` public API is unchanged. The pilot composes existing `AddSqlServerDatastore`, `AddOutbox`, `AddPlatformEventBus`, `AddPlatformEventPublisher`, `AddJwtAuthentication`, `AddRequireServicePolicy`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddPlatformOpenApi`, `AuthorizationPolicies.RequireServicePolicy`.

### Validation

- Out of scope. Existing absence of `FluentValidation` / `DataAnnotations` is preserved. Add per-slice FluentValidation listed as a follow-up — same as Order.

### Rollout

- Branch `refactor/product-vsa`. Staged commits land in this order, each green:
  1. Scaffold `Product.Service.LayoutAnalyzer` sub-project (rules disabled / warning-only) + NetArchTest project dependency added to `Product.Tests` + skipped layout tests authored.
  2. Move files into `Domain/`, `Contracts/Integration/`; rename namespaces. Anemic `Product` and `ProductType` move first (still anemic at this point). `IProductStore` moves to `Domain/Abstractions/`; `ProductContext` continues to implement it temporarily.
  3. Introduce `Domain/Events/`, `IDomainEvent`, `Entity` base. Promote `Product` to rich aggregate with `ChangePrice` etc. Aggregate raises domain events; endpoints still call `outboxStore.AddOutboxEvent` directly (interceptor not yet wired). Aggregate unit tests added.
  4. Introduce `IIntegrationMap<,>` + `DomainEventOutboxInterceptor`. Add mappers temporarily in `Infrastructure/Outbox/Mappers/`. Wire interceptor on `ProductContext`. Remove `outboxStore.AddOutboxEvent` calls from endpoints. Split `EfProductStore` out of `ProductContext`.
  5. Extract slices one at a time: `CreateProduct`, `GetProduct`, `UpdateProduct`. Move mappers from `Infrastructure/Outbox/Mappers/` into their producing slice. Migrate per-slice tests.
  6. Add new `ListProducts` slice with new endpoint + tests.
  7. Move `InternalOutboxEndpoints` to `Infrastructure/Outbox/`. Clean up `Program.cs` into a slice manifest. Delete now-empty `Endpoints/`, `Models/`, `IntegrationEvents/`, `Infrastructure/Outbox/Mappers/`.
  8. Unskip NetArchTest rules; enable `Product.Service.LayoutAnalyzer` rules as errors. Demonstrate spike-and-revert.
  9. Update root `CLAUDE.md` Product line to reference ADR 0011 and the existing runbook. No new ADR. No new runbook.
- Single PR for review. Pre-commit hook gates every commit (no `--no-verify`, no validation deferral).

## Testing Decisions

### Test philosophy

- A good test verifies external behavior of a module through its public interface, not internal implementation details.
- Refactor produces zero behavior change on existing routes. Every existing `Product.Tests` test passes after namespace updates required by the rename.
- New tests are added for: (a) the promoted `Product` aggregate's domain methods + invariants; (b) the new outbox interceptor + integration maps; (c) the new `ListProducts` endpoint; (d) the architecture rules themselves.

### Modules to test

- **`Product` aggregate (new)** — new `Product.Tests/Domain/ProductTests.cs` covering: constructor raises `ProductCreatedDomainEvent`; `ChangePrice` raises `ProductPriceChangedDomainEvent` only when new price differs from current; `Rename` / type-change methods do not raise price-change event when price unchanged; setters are inaccessible from outside the aggregate. Display names follow `Given_When_Then` style consistent with `Order.Tests/Domain/OrderTests.cs`.
- **Per-slice handler tests** — existing `Product.Tests/Api/ProductApiTests.cs` tests migrate into `Product.Tests/Features/<Slice>/` without behavioral changes. They continue to use `ProductWebApplicationFactory` and `IntegrationTestBase`. Existing `Product.Tests/Api/InternalOutboxEndpointsTests.cs`, `ObservabilityTests.cs`, `MessagingProviderBootTests.cs`, `HealthChecksTests.cs` either migrate with the slice they exercise or stay under `Product.Tests/Api/` if they exercise cross-cutting infrastructure. The QA seed test (`Product.Tests/Qa/ProductQaSeedTests.cs`) stays at its current location.
- **`DomainEventOutboxInterceptor`** — new unit tests covering: given a `ProductContext` change tracker with a tracked `Product` carrying domain events, mappers are resolved per domain-event runtime type and emit one outbox event per domain event; an unmapped domain-event type fails fast with a descriptive error.
- **Per-slice `IIntegrationMap<TDomainEvent, TIntegrationEvent>` implementations** — small pure-function tests that assert the mapping preserves `ProductId`, `Name`, `Price` (`ProductCreatedEvent`) and `ProductId`, `NewPrice` (`ProductPriceUpdatedEvent`). One test class per mapper.
- **`ListProducts` endpoint** — new integration test covering: empty database → empty list; seeded products → list of all products with correct shape; auth requirement matches the existing `GetProduct` slice.
- **`Product.Tests/Architecture/LayoutTests.cs`** — NetArchTest rules tests that act as the executable specification of the boundary policy. Mirror Order's `LayoutTests.cs` rules verbatim with namespace swap.
- **`EfProductStore`** — already covered indirectly by integration tests through `WebApplicationFactory<Program>`. No new dedicated tests unless the impl grows beyond a pure split-out from `ProductContext`.

### Prior art in the codebase

- `Product.Tests/IntegrationTestBase.cs` + `Product.Tests/ProductWebApplicationFactory.cs` — existing factory + base used by all current integration tests. Refactor preserves both at the root of the tests project.
- `Order.Tests/Domain/OrderTests.cs` — pattern of aggregate unit tests with `Given_When_Then` underscored display names (`CA1707` suppressed via `Directory.Build.props`). New `Product.Tests/Domain/ProductTests.cs` mirrors this shape.
- `Order.Tests/Architecture/LayoutTests.cs` — direct prior art for the NetArchTest rules. Port verbatim with namespace swap.
- `Order.Service.LayoutAnalyzer/LayoutAnalyzer.cs` — direct prior art for the Roslyn analyzer. New `Product.Service.LayoutAnalyzer/LayoutAnalyzer.cs` is a copy-paste with namespace prefix swap.
- Pre-commit hook (`dotnet husky run --group pre-commit`) enforces `dotnet format --verify-no-changes` and `dotnet build --no-restore` + Basket tests on every commit. Product tests are run manually per the root `CLAUDE.md` sandbox policy before pushing.

## Out of Scope

- Refactoring any other service (basket, auth, inventory, shipping, payment, saga, api-gateway). Order is already done; remaining propagation is a follow-up ADR after Product lands.
- Modifying `ECommerce.Shared`. The pilot composes existing extensions only.
- Adding request validation (FluentValidation or DataAnnotations). Listed as follow-up.
- Introducing MediatR or any mediator-style dispatcher.
- Splitting `Product.Service.csproj` into multiple projects (the analyzer sub-project is a separate analyzer assembly, not an application-tier split).
- Changing the `Product` / `ProductType` database schema. No new EF migrations.
- Changing integration event payload contracts. Only their location (folder + namespace) moves. `ProductCreatedEvent` and `ProductPriceUpdatedEvent` payloads are byte-identical after the refactor.
- Changing the outbox table, dispatcher, or retry/DLQ behavior in `ECommerce.Shared.Infrastructure.Outbox`.
- Changing `ProductApiEndpoints`' existing public HTTP routes, response shapes, status codes, or auth requirements. The only public-surface change is the *addition* of `GET /` (`ListProducts`).
- Changing CI/CD pipelines, Docker Compose, Kubernetes manifests, or Azure pipelines.
- Performance optimization. The CQRS-lite read-path decision is structural, not performance-driven.
- Filing a new ADR. The pilot composes ADR 0011 by reference.
- Writing a new runbook. `docs/runbooks/adding-a-new-slice.md` is reused unchanged.

## Further Notes

- Product was chosen as the **second pilot** (vs the ADR 0011 candidate-order list which began with inventory) for three reasons:
  1. Product is the simplest service in the repo with a non-trivial outbox path. Validating the pattern on a simpler service before a saga participant catches "did the pattern actually need Order's complexity?" early.
  2. Product is anemic today. Promoting it to a rich aggregate is the deepest module change available in this pilot and exercises the `Domain/` layer in a way Order's pilot (which already had a rich aggregate) did not.
  3. Product has zero integration-event consumers. The slice inventory is therefore exactly the HTTP routes — making the slice mapping mechanical and the pilot small enough to review in one sitting.
- The deepest module change in this pilot is the promotion of `Product` from anemic class to rich aggregate. This is the only place where existing test code may need behavioral updates beyond namespace renames — specifically, any test that constructs `new Product { ... }` with object-initializer syntax will need to switch to a constructor. The PR description should call this out for reviewers.
- The outbox interceptor seam is **new** to Product. Order had a `Translate` switch to dismantle; Product had direct `outboxStore.AddOutboxEvent` calls inside endpoints. The outcome is the same — endpoints no longer touch the outbox — but the migration path differs: Product removes calls from endpoints and replaces them with aggregate domain-event emission + interceptor.
- NetArchTest + Roslyn analyzer redundancy carries over from Order. The "belt + suspenders" choice is justified by the AI-assisted contribution model — violations need to surface at the earliest possible moment.
- The "duplicate first, extract on third" rule remains load-bearing. With only 4 slices in Product, the temptation to share will be lower than in Order; the NetArchTest slice-to-slice rule mechanically enforces it regardless.
- After Product lands and at least one follow-up review pass on either pilot, a separate ADR will propose propagation to the remaining services. Candidate order (revised from ADR 0011 follow-up list): inventory (saga participant, similar shape to Order) → payment (saga participant) → shipping → saga (orchestrator, last to validate the layout generalizes to non-CRUD services) → auth → basket (Redis-only, least benefit).
- Behavioral guidance from root `CLAUDE.md` applies: surgical changes only, no improving adjacent code, match existing style, push back on over-engineering. The Product pilot is smaller in line count than Order but carries one substantive design change (rich-aggregate promotion) that warrants careful review.
