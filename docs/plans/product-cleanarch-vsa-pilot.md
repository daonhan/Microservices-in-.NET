# Plan: Product Service Clean Architecture + VSA Pilot

> Source PRD: [../prd/PRD-Product-CleanArch-VSA-Pilot.md](../prd/PRD-Product-CleanArch-VSA-Pilot.md)
> Composes ADR: [../adr/0011-order-cleanarch-vsa-pilot.md](../adr/0011-order-cleanarch-vsa-pilot.md)
> Runbook (reused unchanged): [../runbooks/adding-a-new-slice.md](../runbooks/adding-a-new-slice.md)
> Branch: `refactor/product-vsa` — single PR

## Context

`Product.Service` today is organized by technical type (`Endpoints/`, `Models/`, `IntegrationEvents/`, `Infrastructure/Data/`). To understand "what happens when a product price is updated?" a reader must hop across four folders, and the `Product` class is anemic — `outboxStore.AddOutboxEvent` is called directly from the PUT endpoint, so a future caller mutating `Product.Price` outside that endpoint would silently bypass `ProductPriceUpdatedEvent` emission. The Order pilot (PR #162, merged 2026-05-21) proved Clean Architecture + Vertical Slices on a richer, saga-participant service; ADR 0011 expects propagation. Product is chosen as the second pilot because it is the simplest service with a non-trivial outbox path and an anemic domain — validating that the pattern generalizes downward and exercising the `Domain/` layer in a way Order did not.

Outcome: `Product.Service` reorganized into `Features/<Slice>/` + `Domain/` + `Contracts/Integration/` + `Infrastructure/` with the same belt-and-suspenders boundary enforcement (NetArchTest + Roslyn analyzer) as Order. `Product` is promoted from anemic class to rich aggregate; a new generic `DomainEventOutboxInterceptor` (Product's first such seam) translates domain events to integration events at `SaveChangesAsync`. Existing HTTP routes, response shapes, status codes, auth, and integration-event payloads are byte-identical — the only behavior addition is `GET /` (`ListProducts`).

## Architectural decisions

Durable across all phases. Lifted from PRD §"Implementation Decisions" and Order pilot prior art.

- **Project shape**: Single `Product.Service.csproj` (no split). New sibling project `Product.Service.LayoutAnalyzer` (netstandard2.0, `IsRoslynComponent=true`) referenced from `Product.Service.csproj` as `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`. Mirrors `Order.Service.LayoutAnalyzer`.
- **Folder topology** under `product-microservice/Product.Service/`:
  - `Features/<Slice>/` — one per inbound HTTP route. Slices: `CreateProduct`, `GetProduct`, `ListProducts` (new), `UpdateProduct`. Each owns endpoint + request/response DTOs + sealed handler + `AddXxxSlice()` DI extension + (if it emits an integration event) co-located `IIntegrationMap` impl.
  - `Domain/` — `Product` (rich aggregate), `ProductType` (reference entity), `Entity` base, `IDomainEvent`, `Domain/Events/{ProductCreatedDomainEvent, ProductPriceChangedDomainEvent}`, `Domain/Abstractions/IProductStore`. No EF, no HTTP.
  - `Contracts/Integration/` — `ProductCreatedEvent`, `ProductPriceUpdatedEvent` (payload classes; cross-service contract unchanged).
  - `Infrastructure/Data/EntityFramework/` — `ProductContext` (persistence only), `EfProductStore` (new), `ProductConfiguration`, `ProductTypeConfiguration`, `ProductContextSeed`, `ProductContextDesignTimeFactory`, `EntityFrameworkExtensions`.
  - `Infrastructure/Outbox/` — `IIntegrationMap<TDomainEvent,TIntegrationEvent>`, `DomainEventOutboxInterceptor`, `InternalOutboxEndpoints`.
  - `Migrations/` — unchanged; `generated_code = true`.
- **Namespaces** match folders: `Product.Service.Domain`, `Product.Service.Domain.Events`, `Product.Service.Domain.Abstractions`, `Product.Service.Features.<Slice>`, `Product.Service.Contracts.Integration`, `Product.Service.Infrastructure.Data.EntityFramework`, `Product.Service.Infrastructure.Outbox`. Old `Product.Service.IntegrationEvents` and `Product.Service.ApiModels`/`Product.Service.Endpoints`/`Product.Service.Models` namespaces are deleted as files relocate.
- **Routes** (unchanged unless flagged):
  - `GET /{productId}` — unauthenticated. `GetProduct` slice. Direct EF projection.
  - `POST /` — `RequireAuthorization`. `CreateProduct` slice.
  - `PUT /{productId}` — `RequireAuthorization`. `UpdateProduct` slice.
  - `GET /` — **new**, matches `GetProduct` auth posture. `ListProducts` slice. Direct EF projection.
  - `GET /internal/outbox/failed` — `RequireService`. Stays.
- **Dispatch model**: No MediatR. Endpoint takes its slice handler via `[FromServices]`-style minimal-API parameter binding and calls `HandleAsync(...)` directly. Handler classes are `sealed`, internal, one public async method.
- **Domain richness rule**: Public setters removed; properties become `{ get; private set; }` (or `init` where EF mapping permits). Constructor encapsulates creation, raises `ProductCreatedDomainEvent`. `ChangePrice(decimal newPrice)` raises `ProductPriceChangedDomainEvent` only when `newPrice != Price`. `Rename`/type-change methods do not raise price-change events.
- **Persistence split**: `IProductStore` lives in `Domain/Abstractions/`. `EfProductStore` (new file) implements it in `Infrastructure/Data/EntityFramework/`. `ProductContext` ceases to implement `IProductStore`. `GetById` continues to `Include(p => p.ProductType)` to preserve existing response shape.
- **Outbox seam**: New `IIntegrationMap<TDomainEvent,TIntegrationEvent>` interface (mirrors Order: marker `IIntegrationMap` for polymorphic resolution + generic for type-safe mapping). Generic `DomainEventOutboxInterceptor` resolves mappers via `IEnumerable<IIntegrationMap>` keyed by `DomainEventType`; calls `IOutboxStore.AddOutboxEvent` during `SaveChangesAsync`. Unmapped runtime types throw `InvalidOperationException` naming the unmapped type. Endpoint code no longer references `IOutboxStore`.
- **Cross-slice rule**: Duplicate first, extract to `Domain/` (behavioral) or `Features/Shared/` (helper) on third use. NetArchTest forbids `Features.<X>` → `Features.<Y>` for distinct slices.
- **Boundary enforcement** (4 rules, both NetArchTest in `Product.Tests/Architecture/LayoutTests.cs` AND Roslyn `Product.Service.LayoutAnalyzer`):
  1. `Domain` → not `Infrastructure.*` or `Features.*`.
  2. `Features.<X>` → not `Features.<Y>` for distinct slices.
  3. `Infrastructure` → not `Features` (may reference `Domain` + `Contracts`).
  4. `Contracts` → not `Domain` or `Infrastructure` or `Features` or `Endpoints` or `ApiModels` or `IntegrationEvents`.
- **`ECommerce.Shared` public API unchanged.** Pilot composes existing extensions: `AddSqlServerDatastore`, `AddOutbox`, `AddPlatformEventBus`, `AddPlatformEventPublisher`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddJwtAuthentication`, `AddRequireServicePolicy`, `AddPlatformOpenApi`.
- **Behavior preservation**: Every existing `Product.Tests` test passes after namespace updates. `MetricFactory.Counter("products-created")` and `MetricFactory.Counter("product-price-updates")` continue to increment on the same conditions (one per successful create; one per successful update with `priceChanged == true`). The slice handler owns the counter call after the refactor. QA seed (`ProductHappy`/`ProductDecline`/`ProductZeroStock`/`ProductLowStock`/`ProductRestockTarget`) and `ProductTypes` (Shoes/Shorts) continue to seed through `HasData`.
- **Pre-commit gate**: `dotnet husky run --group pre-commit` (`dotnet format --verify-no-changes`, `dotnet build --no-restore`, Basket tests) on every commit. No `--no-verify`, no deferred validation. Product tests run manually before pushing per root `CLAUDE.md` sandbox policy.

## Critical files (Order pilot prior art to reuse / copy)

Treat each as a verbatim template — copy with namespace prefix swap, then specialize for Product.

- `order-microservice/Order.Service.LayoutAnalyzer/LayoutAnalyzer.cs` → port to `product-microservice/Product.Service.LayoutAnalyzer/LayoutAnalyzer.cs` (4 rules, namespace prefixes swapped). Also port `AnalyzerReleases.Shipped.md` + `AnalyzerReleases.Unshipped.md`.
- `order-microservice/Order.Tests/Architecture/LayoutTests.cs` → port to `product-microservice/Product.Tests/Architecture/LayoutTests.cs` (4 NetArchTest assertions).
- `order-microservice/Order.Service/Infrastructure/Outbox/IIntegrationMap.cs` → port verbatim (marker + generic).
- `order-microservice/Order.Service/Infrastructure/Outbox/DomainEventOutboxInterceptor.cs` → port verbatim, retarget to `ProductContext`.
- `order-microservice/Order.Service/Domain/Entity.cs`, `Domain/Events/IDomainEvent.cs` → port verbatim into `Product.Service/Domain/`.
- `order-microservice/Order.Tests/Domain/OrderTests.cs` → mirror shape for new `Product.Tests/Domain/ProductTests.cs` (Given_When_Then with underscore display names; `CA1707` already suppressed in `Directory.Build.props`).
- `order-microservice/Order.Service/Program.cs` slice-manifest section → mirror in `product-microservice/Product.Service/Program.cs` (Phase 7).

## Critical files (Product service to modify)

- `product-microservice/Product.Service/Models/Product.cs` → moves to `Domain/Product.cs`, then promoted to rich aggregate (Phase 3).
- `product-microservice/Product.Service/Models/ProductType.cs` → moves to `Domain/ProductType.cs`.
- `product-microservice/Product.Service/Infrastructure/Data/IProductStore.cs` → moves to `Domain/Abstractions/IProductStore.cs`.
- `product-microservice/Product.Service/Infrastructure/Data/EntityFramework/ProductContext.cs` → drops `IProductStore` implementation (Phase 4); new sibling `EfProductStore.cs` takes it.
- `product-microservice/Product.Service/IntegrationEvents/ProductCreatedEvent.cs` and `ProductPriceUpdatedEvent.cs` → move to `Contracts/Integration/` (location-only; payload unchanged).
- `product-microservice/Product.Service/Endpoints/ProductApiEndpoints.cs` → deleted at the end of Phase 5; routes split into per-slice endpoint files.
- `product-microservice/Product.Service/Endpoints/InternalOutboxEndpoints.cs` → moves to `Infrastructure/Outbox/` (Phase 7).
- `product-microservice/Product.Service/Program.cs` → DI chain becomes a slice manifest (Phase 7).
- `product-microservice/Product.Service/Product.Service.csproj` → add analyzer reference (Phase 1).
- `product-microservice/Product.Tests/Product.Tests.csproj` → add `NetArchTest.Rules` package + `Product.Service.LayoutAnalyzer` project ref (Phase 1).
- `product-microservice/Product.Tests/Api/ProductApiTests.cs`, `InternalOutboxEndpointsTests.cs`, `ObservabilityTests.cs`, `MessagingProviderBootTests.cs`, `HealthChecksTests.cs` → migrate per-slice or stay cross-cutting (Phases 5 & 7).
- `product-microservice/Product.Tests/Qa/ProductQaSeedTests.cs` → stays put.
- `CLAUDE.md` (root) → add "Product service exception" paragraph mirroring the Order one (Phase 7).

---

## Phase 1: Scaffold analyzer sub-project + skipped layout tests

**User stories**: 11, 12, 17, 21, 24

### What to build

Land the boundary-enforcement scaffolding before any source files move. Add a new `Product.Service.LayoutAnalyzer` netstandard2.0 sub-project that copies `Order.Service.LayoutAnalyzer`'s `LayoutAnalyzer.cs` with namespace prefix swap (`Order.Service.*` → `Product.Service.*` in the analyzer's rule strings). Wire it into `Product.Service.csproj` as `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`, but configure rule severities so violations are **warnings, not errors** at this phase. Add the `NetArchTest.Rules` NuGet package to `Product.Tests.csproj`. Author `Product.Tests/Architecture/LayoutTests.cs` with all four NetArchTest assertions, each marked `Skip="Enabled in Phase 7"`.

Zero source files move. Zero behavior change. Build green, all existing tests green, no analyzer warnings (because no namespaces match the analyzer's targets yet).

### Acceptance criteria

- [ ] `product-microservice/Product.Service.LayoutAnalyzer/Product.Service.LayoutAnalyzer.csproj` exists, builds, `IsRoslynComponent=true`.
- [ ] `Product.Service.LayoutAnalyzer/LayoutAnalyzer.cs` is a copy of Order's analyzer with namespace targets retargeted to `Product.Service.*`. Diagnostic IDs use a Product-specific prefix (e.g., `PRDLAY001`..`PRDLAY004`).
- [ ] `Product.Service.csproj` references the analyzer project with `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`.
- [ ] `Product.Tests.csproj` adds `NetArchTest.Rules` package reference.
- [ ] `Product.Tests/Architecture/LayoutTests.cs` exists with 4 `[Fact(Skip="...")]` tests mirroring `Order.Tests/Architecture/LayoutTests.cs`.
- [ ] `cd product-microservice && dotnet build` green.
- [ ] `cd product-microservice && dotnet test` green (existing tests pass; new layout tests show as skipped).
- [ ] Pre-commit gate passes; commit lands on `refactor/product-vsa`.

---

## Phase 2: Move files into Domain / Contracts / Abstractions; rename namespaces (anemic still)

**User stories**: 1, 8, 10, 14, 18, 20

### What to build

Reorganize existing source into the new folder topology with zero richness change. Move `Models/Product.cs` and `Models/ProductType.cs` into `Domain/` (still anemic — no constructors, no methods, no domain events yet). Move `Infrastructure/Data/IProductStore.cs` into `Domain/Abstractions/IProductStore.cs`. `ProductContext` still implements `IProductStore` (the split happens in Phase 4). Move `IntegrationEvents/ProductCreatedEvent.cs` and `IntegrationEvents/ProductPriceUpdatedEvent.cs` into `Contracts/Integration/`. Rename namespaces to match new folders. Delete the now-empty `Models/`, `Infrastructure/Data/` (top level — `EntityFramework/` stays), and `IntegrationEvents/` folders.

Every existing test passes after `using` directive updates. The HTTP surface, response payloads, integration-event payloads, and metrics all stay byte-identical.

### Acceptance criteria

- [ ] `product-microservice/Product.Service/Domain/Product.cs` exists with namespace `Product.Service.Domain` (still anemic).
- [ ] `product-microservice/Product.Service/Domain/ProductType.cs` exists with namespace `Product.Service.Domain`.
- [ ] `product-microservice/Product.Service/Domain/Abstractions/IProductStore.cs` exists with namespace `Product.Service.Domain.Abstractions`.
- [ ] `product-microservice/Product.Service/Contracts/Integration/ProductCreatedEvent.cs` and `ProductPriceUpdatedEvent.cs` exist with namespace `Product.Service.Contracts.Integration`. Payload contracts byte-identical.
- [ ] Old `Models/`, `Infrastructure/Data/IProductStore.cs` (top-level), and `IntegrationEvents/` are gone.
- [ ] `Product.Tests` `using` directives updated; no other test changes.
- [ ] `cd product-microservice && dotnet build` green.
- [ ] `cd product-microservice && dotnet test` green (existing test count unchanged; all green).
- [ ] Pre-commit gate passes.

---

## Phase 3: Promote Product to rich aggregate; add domain events

**User stories**: 4, 5, 15

### What to build

The deepest module change. Introduce `Domain/Entity.cs` (base class holding `[NotMapped] List<IDomainEvent>` with `Raise` / `DequeueDomainEvents`) and `Domain/Events/IDomainEvent.cs` — both ported verbatim from Order. Author `Domain/Events/ProductCreatedDomainEvent.cs` and `Domain/Events/ProductPriceChangedDomainEvent.cs`.

Promote `Product` from anemic to rich: remove public setters (properties become `{ get; private set; }` or `init` where the EF mapping permits); add a public constructor that encapsulates creation and raises `ProductCreatedDomainEvent`; add `ChangePrice(decimal newPrice)` that raises `ProductPriceChangedDomainEvent` only when `newPrice != Price`; add `Rename(string)`, `ChangeDescription(string?)`, `ChangeType(int productTypeId)` covering existing PUT mutations (no domain events from these).

Endpoints continue to call `outboxStore.AddOutboxEvent` directly (interceptor not yet wired) — but they now load the aggregate via `IProductStore`, call domain methods, persist. The POST endpoint constructs via the new constructor; the PUT endpoint loads then calls the new methods. Any existing test code that built `new Product { ... }` via object-initializer is updated to use the constructor.

Author `Product.Tests/Domain/ProductTests.cs` with Given_When_Then unit tests covering: ctor raises `ProductCreatedDomainEvent`; `ChangePrice` with same price raises no event; `ChangePrice` with different price raises `ProductPriceChangedDomainEvent` exactly once; `Rename` raises no price-change event; setters inaccessible from outside the aggregate (compile-time check).

### Acceptance criteria

- [ ] `Domain/Entity.cs` and `Domain/Events/IDomainEvent.cs` exist (ports from Order, verbatim except namespace).
- [ ] `Domain/Events/ProductCreatedDomainEvent.cs` and `Domain/Events/ProductPriceChangedDomainEvent.cs` exist.
- [ ] `Domain/Product.cs` has no public setters; constructor + `ChangePrice` + `Rename` + `ChangeDescription` + `ChangeType` methods present; inherits `Entity`.
- [ ] POST and PUT endpoints in `ProductApiEndpoints.cs` route through aggregate methods. They still call `outboxStore.AddOutboxEvent` (interceptor lands in Phase 4).
- [ ] `Product.Tests/Domain/ProductTests.cs` exists; all new aggregate tests green.
- [ ] All pre-existing tests still green (object-initializer call sites in tests updated to constructor; no behavioral assertions change).
- [ ] Integration-event payloads emitted to the outbox are byte-identical to Phase 2.
- [ ] `MetricFactory.Counter("products-created")` and `MetricFactory.Counter("product-price-updates")` increment on the same conditions as before (one per successful POST; one per successful PUT with `priceChanged == true`).
- [ ] Pre-commit gate passes.

---

## Phase 4: Introduce outbox interceptor seam; split EfProductStore from ProductContext

**User stories**: 5, 8, 9, 10

### What to build

Author `Infrastructure/Outbox/IIntegrationMap.cs` (marker + generic, ported from Order). Author `Infrastructure/Outbox/DomainEventOutboxInterceptor.cs` (generic, resolves mappers via `IEnumerable<IIntegrationMap>` keyed by `DomainEventType`; calls `IOutboxStore.AddOutboxEvent` during the `SaveChangesAsync` extension hook the Order pattern uses; fails fast with `InvalidOperationException` on unmapped runtime types, naming the type).

Add **temporary** mapper files under `Infrastructure/Outbox/Mappers/`:
- `ProductCreatedIntegrationMap.cs` → `IIntegrationMap<ProductCreatedDomainEvent, ProductCreatedEvent>`.
- `ProductPriceUpdatedIntegrationMap.cs` → `IIntegrationMap<ProductPriceChangedDomainEvent, ProductPriceUpdatedEvent>`.

These will be moved into their producing slices in Phase 5.

Register the interceptor on `ProductContext` via the DbContext options wiring. Add a small `AddProductOutbox()` helper (or inline registration in `Program.cs`) that registers the interceptor + scans/registers `IIntegrationMap` implementations.

Author `Infrastructure/Data/EntityFramework/EfProductStore.cs` implementing `IProductStore` against `ProductContext`. Update DI: `services.AddScoped<IProductStore, EfProductStore>()`. `ProductContext` drops its `IProductStore` implementation. `GetById` continues to `Include(p => p.ProductType)` to preserve response shape.

Remove all `outboxStore.AddOutboxEvent` calls from `ProductApiEndpoints.cs`. The aggregate now raises domain events; the interceptor translates and persists them during `SaveChangesAsync`. Endpoint code no longer references `IOutboxStore`.

Add new tests:
- `Product.Tests/Infrastructure/Outbox/DomainEventOutboxInterceptorTests.cs` — given a tracked `Product` with N domain events, N outbox rows are written; unmapped event type throws `InvalidOperationException` naming the type.
- Per-mapper unit tests: `ProductCreatedIntegrationMapTests.cs` asserts `ProductId`/`Name`/`Price` preservation; `ProductPriceUpdatedIntegrationMapTests.cs` asserts `ProductId`/`NewPrice` preservation.

### Acceptance criteria

- [ ] `Infrastructure/Outbox/IIntegrationMap.cs` exists (marker + generic).
- [ ] `Infrastructure/Outbox/DomainEventOutboxInterceptor.cs` exists; fail-fast unmapped behavior covered by test.
- [ ] `Infrastructure/Outbox/Mappers/ProductCreatedIntegrationMap.cs` and `ProductPriceUpdatedIntegrationMap.cs` exist (temporary location).
- [ ] `Infrastructure/Data/EntityFramework/EfProductStore.cs` exists and is the registered `IProductStore` implementation. `ProductContext` no longer implements `IProductStore`.
- [ ] `ProductApiEndpoints.cs` contains zero references to `IOutboxStore` / `AddOutboxEvent`.
- [ ] All existing integration tests green — `ProductCreatedEvent` and `ProductPriceUpdatedEvent` are still emitted (now via interceptor) with byte-identical payloads. The event-subscription assertion in `ProductApiTests` passes unchanged.
- [ ] New interceptor + mapper unit tests green.
- [ ] Pre-commit gate passes.

---

## Phase 5: Extract slices — CreateProduct, GetProduct, UpdateProduct (three commits)

**User stories**: 1, 2, 3, 5, 6, 7, 13, 15, 28

### What to build

Three separate commits on the branch, one per slice. Each commit independently green.

**Commit 5a — `CreateProduct`**:
- `Features/CreateProduct/CreateProductEndpoint.cs` (POST `/`, `RequireAuthorization`).
- `Features/CreateProduct/CreateProductRequest.cs`.
- `Features/CreateProduct/CreateProductHandler.cs` — sealed internal; injects `IProductStore`, `MetricFactory`; constructs aggregate, persists, increments `products-created` counter.
- `Features/CreateProduct/ProductCreatedIntegrationMap.cs` — moved from `Infrastructure/Outbox/Mappers/`.
- `Features/CreateProduct/CreateProductSliceExtensions.cs` — `AddCreateProductSlice(this IServiceCollection)` registers handler + integration map.
- Wire `services.AddCreateProductSlice()` into `Program.cs`.
- Move `Product.Tests/Api/ProductApiTests.cs` create-related tests → `Product.Tests/Features/CreateProduct/CreateProductTests.cs`.

**Commit 5b — `GetProduct`**:
- `Features/GetProduct/GetProductEndpoint.cs` (GET `/{productId}`, unauthenticated).
- `Features/GetProduct/GetProductResponse.cs`.
- `Features/GetProduct/GetProductHandler.cs` — injects `ProductContext` directly (read slice bypasses `IProductStore`); LINQ-projects to `GetProductResponse` (includes `ProductType` join projection, no aggregate hydration).
- `Features/GetProduct/GetProductSliceExtensions.cs` — `AddGetProductSlice` (no integration map).
- Move get-related tests → `Product.Tests/Features/GetProduct/GetProductTests.cs`.

**Commit 5c — `UpdateProduct`**:
- `Features/UpdateProduct/UpdateProductEndpoint.cs` (PUT `/{productId}`, `RequireAuthorization`).
- `Features/UpdateProduct/UpdateProductRequest.cs`.
- `Features/UpdateProduct/UpdateProductHandler.cs` — injects `IProductStore`, `MetricFactory`; loads aggregate, calls `Rename`/`ChangeDescription`/`ChangeType`/`ChangePrice`; increments `product-price-updates` counter only if `ChangePrice` raised an event (e.g., by inspecting `DequeueDomainEvents` snapshot or returning a bool from `ChangePrice`).
- `Features/UpdateProduct/ProductPriceUpdatedIntegrationMap.cs` — moved from `Infrastructure/Outbox/Mappers/`.
- `Features/UpdateProduct/UpdateProductSliceExtensions.cs`.
- Move update-related tests → `Product.Tests/Features/UpdateProduct/UpdateProductTests.cs`.

After 5c, `Infrastructure/Outbox/Mappers/` is empty and deleted. Cross-cutting tests (`ObservabilityTests`, `HealthChecksTests`, `MessagingProviderBootTests`, `InternalOutboxEndpointsTests`) stay under `Product.Tests/Api/` for now (relocated in Phase 7 if appropriate).

### Acceptance criteria

- [ ] Three slice folders exist with the file inventory above. Each slice folder is self-contained.
- [ ] `Infrastructure/Outbox/Mappers/` is deleted after commit 5c.
- [ ] `Program.cs` chains `AddCreateProductSlice().AddGetProductSlice().AddUpdateProductSlice()` (the manifest is still imperfect; final cleanup is Phase 7).
- [ ] `ProductApiEndpoints.cs` is empty or deleted by end of commit 5c (its routes now live in slice endpoint files).
- [ ] `MetricFactory.Counter("products-created")` and `MetricFactory.Counter("product-price-updates")` increment from inside the slice handler — same conditions as today.
- [ ] After each commit (5a, 5b, 5c) independently: `dotnet build` green, `dotnet test` green, pre-commit gate passes.
- [ ] No `Features.<X>` namespace references `Features.<Y>` for distinct slices (verified manually until Phase 7 unskips the rule).

---

## Phase 6: Add new `ListProducts` slice

**User stories**: 6, 19

### What to build

The pilot's one intentional behavior addition. New HTTP route `GET /` returning a list of products, mirroring the auth posture of `GetProduct` (unauthenticated to match existing convention).

- `Features/ListProducts/ListProductsEndpoint.cs` — GET `/`.
- `Features/ListProducts/ListProductsResponse.cs` — `record ListProductsResponseItem(int Id, string Name, decimal Price, string ProductType, string? Description)` and a wrapper if needed; shape mirrors `GetProductResponse`.
- `Features/ListProducts/ListProductsHandler.cs` — injects `ProductContext`; projects `Products.Select(...)` directly to response items including the `ProductType.Type` join projection; returns the list.
- `Features/ListProducts/ListProductsSliceExtensions.cs` — `AddListProductsSlice` (no integration map; read-only).
- Wire `services.AddListProductsSlice()` into `Program.cs`.

New integration tests in `Product.Tests/Features/ListProducts/ListProductsTests.cs`:
- Empty database (or filtered down) → empty list, 200 OK.
- Seeded products → list contains every seeded persona (`ProductHappy`, `ProductDecline`, `ProductZeroStock`, `ProductLowStock`, `ProductRestockTarget`) with correct shape.
- Auth posture matches `GetProduct` (no `Authorization` header required).

### Acceptance criteria

- [ ] `Features/ListProducts/` folder exists with the four files.
- [ ] `Program.cs` registers and maps `ListProducts` slice.
- [ ] `GET /` returns 200 with a list of products matching the QA seed.
- [ ] `Product.Tests/Features/ListProducts/ListProductsTests.cs` covers empty + seeded + auth-posture cases; green.
- [ ] All other tests green.
- [ ] Pre-commit gate passes.

---

## Phase 7: Cleanup, slice manifest, enforce boundaries, docs

**User stories**: 11, 12, 16, 17, 22, 24, 25

### What to build

Final consolidation and enforcement turn-on.

- Move `Endpoints/InternalOutboxEndpoints.cs` → `Infrastructure/Outbox/InternalOutboxEndpoints.cs` (namespace + folder; `RequireService` policy preserved).
- Delete now-empty `Endpoints/`, `Models/`, `IntegrationEvents/`, `ApiModels/`, `Infrastructure/Data/` (top-level, only `EntityFramework/` subfolder remains), `Infrastructure/Outbox/Mappers/`.
- Decide per-test for `Product.Tests/Api/` cross-cutting tests: `ObservabilityTests`, `HealthChecksTests`, `MessagingProviderBootTests`, `InternalOutboxEndpointsTests` stay under `Product.Tests/Api/` (they exercise cross-cutting infrastructure). `ProductApiTests.cs` is fully decomposed by this point — any remainder is deleted.
- Clean `Program.cs` into a slice manifest: shared platform extensions first (unchanged), then a chained slice manifest `.AddCreateProductSlice().AddGetProductSlice().AddListProductsSlice().AddUpdateProductSlice()`, then endpoint mapping calls (`app.MapCreateProduct(); app.MapGetProduct(); app.MapListProducts(); app.MapUpdateProduct(); app.RegisterInternalOutboxEndpoints();`).
- **Unskip** all four `Product.Tests/Architecture/LayoutTests.cs` rules. Verify each passes against the current source tree.
- **Promote** `Product.Service.LayoutAnalyzer` diagnostic severities from `Warning` to `Error`.
- **Spike-and-revert demonstration**: temporarily introduce one violation per rule (e.g., `Domain/Product.cs` `using Product.Service.Features.CreateProduct;`), confirm both the analyzer fires as a compile error AND the matching NetArchTest assertion fails; revert the spike; record the result in the PR description.
- Update root `CLAUDE.md` (`../../CLAUDE.md` from this file): add a "Product service exception" paragraph below the "Order service exception" paragraph, mirroring it (link to ADR 0011, link to runbook, note that propagation to remaining services is a separate ADR).
- **No new ADR.** **No new runbook.** The pilot composes ADR 0011 by reference and reuses `adding-a-new-slice.md` unchanged.

### Acceptance criteria

- [ ] `Endpoints/`, `Models/`, `IntegrationEvents/`, `ApiModels/`, `Infrastructure/Outbox/Mappers/`, top-level `Infrastructure/Data/` (excluding `EntityFramework/`) are gone.
- [ ] `Infrastructure/Outbox/InternalOutboxEndpoints.cs` exists; `RequireService` policy gate preserved; DLQ-poller call to `/internal/outbox/failed` still works.
- [ ] `Program.cs` reads as a manifest: shared platform setup → slice DI chain → endpoint maps → infra wiring. Diff vs. Phase 6 limited to relocation + chaining.
- [ ] `Product.Tests/Architecture/LayoutTests.cs` — all four tests unskipped and green.
- [ ] `Product.Service.LayoutAnalyzer` diagnostics raised to `Error` severity (verified by spike-and-revert).
- [ ] PR description records the spike-and-revert outcome per rule (4 cases).
- [ ] Root `CLAUDE.md` "Product service exception" paragraph added; links resolve.
- [ ] No new ADR file added. No new runbook file added.
- [ ] `cd product-microservice && dotnet build` green with errors-on analyzer.
- [ ] `cd product-microservice && dotnet test` green (every test from every prior phase still green).
- [ ] Pre-commit gate passes; final commit on `refactor/product-vsa`.

---

## Verification (end-to-end)

After all phases land on `refactor/product-vsa`:

1. **Build**: `cd product-microservice && dotnet build` — green, no warnings (analyzer rules errors-on, no violations).
2. **Tests**: `cd product-microservice && dotnet test` — every test passes (existing + new aggregate + new interceptor + new mapper + new `ListProducts` + 4 NetArchTest assertions).
3. **HTTP smoke against Docker stack**:
   - `docker compose up sql rabbitmq redis -d` then `docker compose up product-service --build`.
   - `GET http://localhost:8002/<seeded-product-id>` → 200, response shape unchanged.
   - `GET http://localhost:8002/` → 200, list contains all seeded personas.
   - `POST http://localhost:8002/` with valid `Authorization: Bearer <user-token>` and a `CreateProductRequest` body → 201, side-effect: subscribe to `ProductCreatedEvent` on the bus and confirm one message with byte-identical payload to pre-refactor.
   - `PUT http://localhost:8002/<id>` with a price change → 200, side-effect: `ProductPriceUpdatedEvent` published with `NewPrice` matching; PUT with same price → 200, no `ProductPriceUpdatedEvent` published.
   - `GET http://localhost:8002/internal/outbox/failed` without service token → 401/403; with service token (`scope=service`) → 200.
4. **Metrics**: scrape `/metrics`, confirm `products_created_total` and `product_price_updates_total` counters increment from the smoke tests above.
5. **Cross-service**: with Order + Inventory + Saga running, run the existing Order create flow against a `ProductHappy`-seeded product and confirm the saga still completes (no Product-side regression in `ProductCreated` or price update consumption paths in other services).
6. **Boundary spike sanity** (optional during review): re-introduce one violation per rule on a throwaway branch, observe both compile-error (analyzer) and test failure (NetArchTest); revert.
7. **PR**: single PR from `refactor/product-vsa` → `main`. Description references PRD, ADR 0011, runbook (unchanged), and the spike-and-revert results.
