# Plan: Order Service Clean Architecture + Vertical Slices Pilot

> Source PRD: [`docs/prd/PRD-Order-CleanArch-VSA-Pilot.md`](../prd/PRD-Order-CleanArch-VSA-Pilot.md)
> Companion ADR (filed in Phase 9): `docs/adr/0011-order-cleanarch-vsa-pilot.md`
> Runbook (filed in Phase 9): `docs/runbooks/adding-a-new-slice.md`
> Branch: `refactor/order-vsa` — single PR for review.

## Context

`Order.Service` is organized by technical type today: endpoints, models, integration event handlers, repositories each in their own top-level folder. Understanding any one feature requires hopping across four or five folders. Boundaries between domain, application, and infrastructure exist only as conventions, so they erode silently — especially under AI-assisted edits. The most visible smell is `OrderContext.Translate`, which mixes EF persistence with domain-event-to-integration-event mapping inside the `DbContext`.

This plan reorganizes `Order.Service` into a Clean Architecture + Vertical Slice layout inside a single `Order.Service.csproj`. Each inbound trigger (HTTP route or integration message) gets its own `Features/<Slice>/` folder containing endpoint or event handler, request/response DTOs, slice-local handler, slice DI extension, and (for producers) a domain-event-to-integration-event mapper. `Domain/` becomes the home for aggregates, value objects, and abstractions with no infrastructure dependencies. `Contracts/Integration/` holds cross-service payloads. `Infrastructure/` implements abstractions declared in `Domain/`. Boundaries are enforced with NetArchTest + Roslyn banned-symbol analyzers as belt-and-suspenders.

Zero behavior change. Every existing `Order.Tests` test must continue to pass without modification beyond namespace renames. `ECommerce.Shared` public API is unchanged; one incidental lazy-RabbitMQ singleton fix bumped the package 2.23.0 → 2.24.0 for test reproducibility (Phase 5 / `dcbc29c`), with no production behavior change. No new EF migrations. No changes to public HTTP routes, response shapes, status codes, auth requirements, integration event payload contracts, or outbox / DLQ behavior.

The pilot is `Order.Service` only. Propagation to other services is deferred to a follow-up ADR after at least one review pass on the pilot.

## Architectural decisions

Durable across all phases:

- **Project shape**: single `Order.Service.csproj` retained. No split into `Order.Domain` / `Order.Application` / `Order.Infrastructure` projects. Boundaries enforced by namespace conventions + analyzer rules + architecture tests, not csproj references.
- **Folder topology**:
  - `Features/<Slice>/` — one folder per inbound trigger. Slice = one HTTP route OR one integration message handler. Each slice owns its handler, request/response DTOs, slice DI extension, and (if it emits an integration event) its domain-event-to-integration-event mapper.
  - `Domain/` — aggregates, value objects, domain events, `IDomainEvent`, `Entity` base, and `Abstractions/IOrderStore`. No EF, no HTTP, no Redis references.
  - `Contracts/Integration/` — cross-service event and command payload classes (e.g. `OrderCreatedEvent`, `ConfirmOrderCommand`).
  - `Infrastructure/Data/EntityFramework/` — `OrderContext`, `EfOrderStore` (impl of `IOrderStore`), EF configurations.
  - `Infrastructure/Providers/` — HTTP product catalog client, Redis product price provider.
  - `Infrastructure/Outbox/` — generic `DomainEventOutboxInterceptor`, `InternalOutboxEndpoints` (ops surface, `RequireService`).
  - `Migrations/` — unchanged; `generated_code = true`.
- **Namespaces** match folders:
  - `Order.Service.Domain`, `Order.Service.Domain.Events`, `Order.Service.Domain.Abstractions`.
  - `Order.Service.Features.<Slice>` (one per slice).
  - `Order.Service.Contracts.Integration`.
  - `Order.Service.Infrastructure.Data.EntityFramework`, `Order.Service.Infrastructure.Providers`, `Order.Service.Infrastructure.Outbox`.
- **Dispatch model**: no MediatR, no in-house mediator. Endpoints and integration-event consumers take their slice handler class via constructor injection and call `HandleAsync(...)` directly. Slice handler classes are sealed with one public async method.
- **Domain richness rule**: rich domain — `Order` aggregate owns invariants and state transitions. Existing methods (`AddOrderProduct`, `Submit`, `TryConfirm`, `TryCancel`) preserved. Slice handlers are orchestration only: load aggregate, call domain method, persist, optionally publish via outbox. Read slices bypass the aggregate and project directly from `OrderContext` to response DTOs (CQRS-lite).
- **Persistence seam**: single `IOrderStore` abstraction in `Domain/Abstractions/`. EF implementation `EfOrderStore` in `Infrastructure/Data/EntityFramework/`. `OrderContext` is persistence-only after the refactor; the existing `Translate(...)` switch is removed.
- **Outbox translation seam**: new abstraction `IIntegrationMap<TDomainEvent, TIntegrationEvent>` under `Infrastructure/Outbox/`. Each producing slice ships one mapper co-located with the slice (e.g. `Features/CreateOrder/OrderCreatedIntegrationMap.cs`). Generic `DomainEventOutboxInterceptor` resolves mappers by domain-event runtime type via DI and calls `IOutboxStore.AddOutboxEvent` with the translated integration event. `OrderContext.ExecuteAsync` delegates domain-event publication to the interceptor rather than calling `Translate` itself. Unmapped domain-event type fails fast with a descriptive error mirroring the current `InvalidOperationException`.
- **Slice DI**: each slice exposes a static class with `AddXxxSlice(this IServiceCollection)` extension. The extension registers the handler, any slice-specific options, the slice's `IIntegrationMap<,>` if any, and (if applicable) calls existing shared infra (`AddEventHandler<TEvent, THandler>`). `Program.cs` chains slice extensions in a fluent manifest. Per-handler `AddScoped` and per-event `AddEventHandler` calls in `Program.cs` are removed and become slice-local.
- **Cross-slice sharing rule**: rule of three — duplicate freely between slices; extract to `Domain/` (behavioral) or `Features/Shared/` (helper) only on the third use. NetArchTest forbids `Order.Service.Features.<X>` referencing `Order.Service.Features.<Y>` for any `X != Y`.
- **Boundary enforcement**:
  - NetArchTest rules in `Order.Tests/Architecture/LayoutTests.cs`:
    - `Domain` types must not reference `Order.Service.Infrastructure.*` or `Order.Service.Features.*`.
    - `Features.<X>` types must not reference `Features.<Y>` for distinct slices.
    - `Infrastructure` types may reference only `Domain` + `Contracts`.
    - `Contracts` types reference nothing internal.
  - `.editorconfig` banned-symbol / banned-namespace analyzer rules act as a second guardrail (compile-time errors).
  - Both must fail on an intentional violation spike before being marked done.
- **Internal ops endpoints**: `InternalOutboxEndpoints` moves from `Endpoints/` to `Infrastructure/Outbox/InternalOutboxEndpoints.cs`. Wiring done from `Program.cs` after slice registration. `RequireService` policy gate preserved on `/internal/outbox/failed`.
- **Routes / contracts / payloads**: unchanged. Public HTTP routes, response shapes, status codes, auth requirements of `OrderApiEndpoint` preserved. Integration event payload classes preserved — only their location (folder + namespace) moves.
- **Shared library**: `ECommerce.Shared` public API is unchanged. The pilot composes existing `AddSqlServerDatastore`, `AddOutbox`, `AddPlatformEventBus`, `AddEventHandler`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddPlatformOpenApi`, `AuthorizationPolicies.RequireServicePolicy`. One incidental fix during Phase 5 (`dcbc29c`) switched `RabbitMqStartupExtensions` to a lazy `IRabbitMqConnection` singleton factory so the test host does not eagerly open a RabbitMQ connection; package bumped 2.23.0 → 2.24.0, no production behavior change.
- **Validation**: out of scope. The existing absence of `FluentValidation` / `DataAnnotations` is preserved. Listed as follow-up in ADR 0011.
- **Test layout**: `Order.Tests/Features/<Slice>/` mirrors `Features/<Slice>/`. `Order.Tests/Domain/` keeps aggregate-level unit tests. `Order.Tests/Architecture/LayoutTests.cs` holds NetArchTest rules. `OrderWebApplicationFactory` and `IntegrationTestBase` stay at the root of the tests project.
- **Commit gating**: pre-commit hook (`dotnet husky run --group pre-commit`) gates every commit on the branch — `dotnet format --verify-no-changes`, `dotnet build --no-restore`, Basket tests. **`Order.Tests` is run manually before pushing each phase** per the sandbox policy in root `CLAUDE.md`. No `--no-verify`. No `Hooks-Deferred:` / `Validation-Deferred:` footers. If the sandbox hook cannot pass, stop and hand off to host.

---

## Phase 1: Foundation — layout scaffold + namespace move

**User stories**: 14, 17, 21 (partial), 19

### What to build

Lay the new folder and namespace skeleton without changing behavior. Add NetArchTest to `Order.Tests` and create `Order.Tests/Architecture/LayoutTests.cs` with the four boundary rules authored but **skipped** (so future phases can flip them on without re-authoring). Create `Domain/`, `Domain/Events/`, `Domain/Abstractions/`, `Contracts/Integration/`, `Infrastructure/Data/EntityFramework/`, `Infrastructure/Providers/`, `Infrastructure/Outbox/`. Move aggregates, value objects, domain events, `IDomainEvent`, `Entity` base into `Domain/`. Move `IOrderStore` (and define it if currently embedded) into `Domain/Abstractions/`. Move cross-service integration event/command payload classes into `Contracts/Integration/`. Relocate `OrderContext`, `EfOrderStore`, EF configurations into `Infrastructure/Data/EntityFramework/`. Relocate HTTP product catalog client and Redis product price provider into `Infrastructure/Providers/`. Rename namespaces to match the new folders. `Endpoints/`, `IntegrationEvents/EventHandlers/`, and `Models/` (or whatever currently exists) remain until phases 3–6 dissolve them. `OrderContext.Translate` is **not** touched in this phase. `Program.cs` `AddScoped` / `AddEventHandler` calls remain as-is.

### Acceptance criteria

- [ ] `dotnet build` clean across the repo.
- [ ] All existing `Order.Tests` tests pass without behavioral changes (only namespace updates required by the rename).
- [ ] `Order.Tests/Architecture/LayoutTests.cs` exists with NetArchTest rules authored as `[Fact(Skip = "Enabled in phase 8")]`.
- [ ] `Domain/`, `Contracts/Integration/`, `Infrastructure/Data/EntityFramework/`, `Infrastructure/Providers/`, `Infrastructure/Outbox/` folders exist and contain the relocated files.
- [ ] No file in `Domain/` has a `using` for `Microsoft.EntityFrameworkCore`, `StackExchange.Redis`, `System.Net.Http`, or any `Order.Service.Infrastructure.*` namespace.
- [ ] No file in `Contracts/Integration/` references any other `Order.Service.*` namespace.
- [ ] Pre-commit hook passes on the commit.

---

## Phase 2: Outbox translation seam

**User stories**: 9, 10

### What to build

Introduce `IIntegrationMap<TDomainEvent, TIntegrationEvent>` in `Infrastructure/Outbox/`. Implement a generic `DomainEventOutboxInterceptor` that, given a change tracker with a tracked entity carrying domain events, resolves the right mapper per domain-event runtime type via DI and calls `IOutboxStore.AddOutboxEvent` with the translated integration event. Unmapped domain-event type fails fast with the same `InvalidOperationException` shape used by `OrderContext.Translate` today. Wire the interceptor into the persistence path so `OrderContext.ExecuteAsync` delegates domain-event publication to it instead of calling `Translate` directly. Delete the `Translate(...)` switch from `OrderContext`. Register every existing domain-event-to-integration-event mapping as an `IIntegrationMap<,>` implementation **temporarily co-located** in `Infrastructure/Outbox/Mappers/` (these will move into their producing slice's folder in phases 3–4 as those slices are extracted). All registrations live in `Program.cs` for now.

### Acceptance criteria

- [ ] `OrderContext.Translate` no longer exists.
- [ ] `OrderContext` references only persistence + unit-of-work concerns.
- [ ] `DomainEventOutboxInterceptor` exists in `Infrastructure/Outbox/`.
- [ ] Each existing domain-event-to-integration-event mapping has an `IIntegrationMap<,>` implementation in `Infrastructure/Outbox/Mappers/`.
- [ ] New unit tests cover: (a) tracked entity with N domain events of mapped types emits N outbox events with correct mapped payloads; (b) tracked entity with a domain event of an unmapped type throws `InvalidOperationException` with a descriptive message naming the unmapped type.
- [ ] All existing `Order.Tests` tests continue to pass. Every code path that previously hit `Translate` now flows through the interceptor and produces byte-identical outbox events.
- [ ] Pre-commit hook passes on the commit.

---

## Phase 3: Tracer slice — CreateOrder

**User stories**: 1, 2, 3, 5, 7, 8, 15

### What to build

Extract the first full vertical slice end-to-end as the tracer for the pattern. Create `Features/CreateOrder/` containing the HTTP endpoint, request/response DTOs, sealed handler class with a single public `HandleAsync`, `AddCreateOrderSlice(this IServiceCollection)` static extension, and `OrderCreatedIntegrationMap.cs` co-located. The endpoint constructor-injects the handler class and calls `HandleAsync` directly — no MediatR, no in-house dispatcher. The handler is orchestration only: load the `Order` aggregate via `IOrderStore`, call domain methods, persist. The slice extension registers the handler as scoped, the integration map as `IIntegrationMap<OrderCreatedDomainEvent, OrderCreatedEvent>`, and any other slice-local dependencies. `Program.cs` calls `services.AddCreateOrderSlice()` and removes the corresponding per-handler `AddScoped` and per-event `AddEventHandler` calls. Move existing `Order.Tests` tests for create-order into `Order.Tests/Features/CreateOrder/` with namespace updates only — they continue to use `OrderWebApplicationFactory` and `IntegrationTestBase`. Add a small pure-function test for `OrderCreatedIntegrationMap` asserting ID, customer ID, items, currency, and any other field-level detail. Move the temporarily-located `OrderCreated` mapper out of `Infrastructure/Outbox/Mappers/` into the slice. The route, response shape, status codes, auth requirement, and emitted integration event payload are unchanged.

### Acceptance criteria

- [ ] `Features/CreateOrder/` exists with endpoint, DTOs, handler, slice DI extension, and `OrderCreatedIntegrationMap.cs`.
- [ ] `Program.cs` calls `services.AddCreateOrderSlice()` and no longer contains per-component registrations for create-order.
- [ ] No file outside `Features/CreateOrder/` references any type in `Order.Service.Features.CreateOrder.*` namespace.
- [ ] No file in `Features/CreateOrder/` references any type in `Order.Service.Features.<Other>.*` namespace.
- [ ] `Order.Tests/Features/CreateOrder/` contains the migrated tests and a new `OrderCreatedIntegrationMapTests`.
- [ ] All existing `Order.Tests` tests pass. The public HTTP route, response shape, status codes, and auth requirement are unchanged. The emitted `OrderCreatedEvent` payload is byte-identical to before.
- [ ] Pre-commit hook passes on the commit.

---

## Phase 4: Remaining write slices — ConfirmOrder, CancelOrder

**User stories**: 5, 7, 8, 25

### What to build

Replicate the slice shape from phase 3 for the remaining write paths. `Features/ConfirmOrder/` consumes the saga's `ConfirmOrderCommand` (integration message handler) and co-locates `OrderConfirmedIntegrationMap.cs`. `Features/CancelOrder/` consumes the saga's `CancelOrderCommand` and co-locates any cancel-side maps. Each slice has its own handler, DTOs, slice DI extension, mapper(s). Each slice's extension registers the consumer via the existing shared `AddEventHandler<TEvent, THandler>` infrastructure. `Program.cs` calls `services.AddConfirmOrderSlice()` and `services.AddCancelOrderSlice()`, removing the corresponding `AddScoped` / `AddEventHandler` calls. Migrate existing event-handler tests into `Order.Tests/Features/ConfirmOrder/` and `Order.Tests/Features/CancelOrder/`. Verify that trace IDs and correlation IDs propagate identically through HTTP → saga `ConfirmOrderCommand` → outbox `OrderConfirmedEvent` after the refactor (covered by existing integration tests; spot-check with a manual end-to-end run if any tests do not assert correlation IDs).

### Acceptance criteria

- [ ] `Features/ConfirmOrder/` and `Features/CancelOrder/` exist with endpoint/consumer, DTOs, handler, slice DI extension, and co-located mappers.
- [ ] `Program.cs` calls both slice extensions and no longer contains per-component registrations for confirm-order or cancel-order.
- [ ] No cross-slice references between any two of `CreateOrder`, `ConfirmOrder`, `CancelOrder`.
- [ ] `Order.Tests/Features/ConfirmOrder/` and `Order.Tests/Features/CancelOrder/` contain the migrated tests and per-mapper unit tests.
- [ ] All existing `Order.Tests` tests pass. The saga command consumer behavior is unchanged. The emitted `OrderConfirmedEvent` / cancel-side events carry the same `CorrelationId` / `CausationId` / `SagaId` propagation as before.
- [ ] Pre-commit hook passes on each commit.

---

## Phase 5: Read slices — GetOrder, ListOrders (CQRS-lite)

**User stories**: 6, 15

### What to build

Extract the read paths as slices that project directly from `OrderContext` to response DTOs — no aggregate hydration, no child collection include trees the response does not need. `Features/GetOrder/` and `Features/ListOrders/` each have an endpoint, response DTOs, slice-local handler that takes `OrderContext` (or a narrow read interface if one already exists) directly, and `AddGetOrderSlice()` / `AddListOrdersSlice()` extensions. `Program.cs` chains both extensions. Migrate existing get/list tests into `Order.Tests/Features/GetOrder/` and `Order.Tests/Features/ListOrders/` with namespace updates only. Public routes, response shapes, status codes, and auth requirements unchanged.

### Acceptance criteria

- [ ] `Features/GetOrder/` and `Features/ListOrders/` exist with endpoint, response DTOs, handler, slice DI extension.
- [ ] Read handlers do not call `IOrderStore` or hydrate the `Order` aggregate; they project from `OrderContext` to response DTOs.
- [ ] No cross-slice references between read slices and any other slice.
- [ ] `Order.Tests/Features/GetOrder/` and `Order.Tests/Features/ListOrders/` contain the migrated tests.
- [ ] All existing `Order.Tests` tests pass. Public routes, response shapes, status codes, and auth requirements are unchanged.
- [ ] Pre-commit hook passes on each commit.

---

## Phase 6: Event consumer slices — ProductCreated, ProductPriceUpdated

**User stories**: 4, 8

### What to build

Replicate the slice shape for the two inbound integration-event consumers. `Features/ProductCreated/` and `Features/ProductPriceUpdated/` each contain the event consumer, slice-local handler, any slice DTOs, slice DI extension (which wires the consumer via the existing shared `AddEventHandler<TEvent, THandler>`), and — if either produces a domain event that translates to an outgoing integration event — the co-located mapper. `Program.cs` calls `services.AddProductCreatedSlice()` and `services.AddProductPriceUpdatedSlice()`, removing the corresponding `AddEventHandler` calls. Migrate existing event-handler tests into `Order.Tests/Features/ProductCreated/` and `Order.Tests/Features/ProductPriceUpdated/`.

### Acceptance criteria

- [ ] `Features/ProductCreated/` and `Features/ProductPriceUpdated/` exist with consumer, handler, slice DI extension, and (if applicable) co-located mappers.
- [ ] `Program.cs` calls both slice extensions and no longer contains per-component registrations for either event consumer.
- [ ] No cross-slice references between event-consumer slices and any other slice.
- [ ] `Order.Tests/Features/ProductCreated/` and `Order.Tests/Features/ProductPriceUpdated/` contain the migrated tests.
- [ ] All existing `Order.Tests` tests pass. The behavior on receipt of either event is unchanged.
- [ ] After this phase, `Infrastructure/Outbox/Mappers/` is empty (every mapper now lives in its producing slice's folder).
- [ ] Pre-commit hook passes on each commit.

---

## Phase 7: Ops relocation + Program.cs manifest

**User stories**: 9, 16, 24

### What to build

Move `InternalOutboxEndpoints` from `Endpoints/` to `Infrastructure/Outbox/InternalOutboxEndpoints.cs`, preserving the `RequireService` policy on `/internal/outbox/failed`. Wire it from `Program.cs` after slice registration. Clean up `Program.cs` so the composition root is a fluent chain of `services.AddXxxSlice()` calls plus the existing shared infra extensions (`AddSqlServerDatastore`, `AddOutbox`, `AddPlatformEventBus`, `AddPlatformObservability`, etc.) — no per-handler `AddScoped`, no per-event `AddEventHandler`. Delete now-empty technical-type folders (`Endpoints/`, `Models/`, `IntegrationEvents/`, `Infrastructure/Outbox/Mappers/`) and any orphan files left behind by the slice extractions. Verify the DLQ poller's call to `/internal/outbox/failed` still succeeds.

### Acceptance criteria

- [ ] `InternalOutboxEndpoints` lives at `Infrastructure/Outbox/InternalOutboxEndpoints.cs` and is wired from `Program.cs`. The `/internal/outbox/failed` route is unchanged and remains gated by `RequireService`.
- [ ] `Program.cs` reads as a manifest: a chain of slice extension calls + the existing shared infra extensions, with no per-component registrations remaining.
- [ ] `Endpoints/`, `Models/`, `IntegrationEvents/`, and `Infrastructure/Outbox/Mappers/` folders are removed.
- [ ] Manual smoke against the DLQ poller (or existing integration test) confirms `/internal/outbox/failed` returns the expected shape and is gated by `RequireService`.
- [ ] All existing `Order.Tests` tests pass.
- [ ] Pre-commit hook passes on the commit.

---

## Phase 8: Enforcement — unskip rules + analyzers

**User stories**: 11, 12, 13, 23

### What to build

Unskip every NetArchTest rule in `Order.Tests/Architecture/LayoutTests.cs`. Add `.editorconfig` banned-symbol / banned-namespace analyzer rules in `Order.Service` covering the same boundaries:

- Code in `Order.Service.Domain.*` may not reference `Order.Service.Infrastructure.*` or `Order.Service.Features.*`.
- Code in `Order.Service.Features.<X>.*` may not reference `Order.Service.Features.<Y>.*` for any `X != Y`.
- Code in `Order.Service.Infrastructure.*` may not reference `Order.Service.Features.*`.
- Code in `Order.Service.Contracts.*` may not reference any other internal `Order.Service.*` namespace.

Demonstrate both guardrails fire on an intentional violation by spiking one cross-boundary `using` in a throwaway commit, confirming NetArchTest fails and the analyzer raises a build-time error, then reverting the spike before the phase merges. Document the demonstration in the PR description.

### Acceptance criteria

- [ ] No `[Fact(Skip = ...)]` remains in `Order.Tests/Architecture/LayoutTests.cs`. All four layout tests run and pass.
- [ ] `Order.Service/.editorconfig` (or equivalent analyzer config) declares banned-symbol / banned-namespace rules matching the four NetArchTest rules.
- [ ] PR description records the spike-and-revert demonstration showing both NetArchTest and the analyzer fire on a deliberately introduced cross-boundary reference.
- [ ] `dotnet build` clean and `dotnet test` green across the repo.
- [ ] Pre-commit hook passes on the commit.

---

## Phase 9: Docs — ADR 0011 + slice runbook + CLAUDE.md

**User stories**: 21, 22

### What to build

File `docs/adr/0011-order-cleanarch-vsa-pilot.md` (status `Accepted`) capturing: pilot scope (Order only), single-csproj choice, dispatch model (direct DI, no MediatR), rich-domain + CQRS-lite read split, outbox translation seam (`IIntegrationMap<,>` + `DomainEventOutboxInterceptor`), cross-slice rule-of-three, belt-and-suspenders boundary enforcement, namespace conventions, and explicit follow-ups (per-slice FluentValidation; propagation to other services as a separate ADR). Write `docs/runbooks/adding-a-new-slice.md` covering: choose slice name, scaffold `Features/<Slice>/`, write request/response DTOs + sealed handler + endpoint or consumer, write slice DI extension `AddXxxSlice`, add co-located `IIntegrationMap<,>` if the slice publishes integration events, register from `Program.cs`, mirror tests into `Order.Tests/Features/<Slice>/`, run the pre-commit hook. Update root `CLAUDE.md` with a short pointer to ADR 0011 and the runbook under the Order service line. If `order-microservice/CLAUDE.md` exists, add the same pointer there. Note in ADR 0011 that propagation to basket, product, auth, inventory, shipping, payment, saga is a separate ADR informed by pilot learnings; candidate propagation order if approved: inventory → payment → shipping → saga → product → auth → basket.

### Acceptance criteria

- [ ] `docs/adr/0011-order-cleanarch-vsa-pilot.md` exists, status `Accepted`, covers every decision listed above.
- [ ] `docs/runbooks/adding-a-new-slice.md` exists and gives a step-by-step walkthrough that lines up with the structure of an existing slice (e.g. `Features/CreateOrder/`).
- [ ] Root `CLAUDE.md` mentions ADR 0011 and the runbook under or next to the Order service line.
- [ ] `order-microservice/CLAUDE.md` mentions both (if such a file exists in the repo).
- [ ] ADR 0011 lists FluentValidation and propagation-to-other-services as explicit follow-ups, not pilot scope.
- [ ] Pre-commit hook passes on the commit.

---

## Out of scope (per PRD)

- Refactoring any other service (basket, product, auth, inventory, shipping, payment, saga, api-gateway). Propagation is a follow-up ADR.
- Modifying `ECommerce.Shared`. The pilot composes existing extensions only.
- Adding request validation (FluentValidation or DataAnnotations). Listed as a follow-up in ADR 0011.
- Introducing MediatR or any mediator-style dispatcher.
- Splitting `Order.Service.csproj` into multiple projects.
- Changing the `Order` / `OrderProduct` database schema. No new EF migrations.
- Changing integration event payload contracts. Only their location (folder + namespace) moves.
- Changing the outbox table, dispatcher, or retry/DLQ behavior in `ECommerce.Shared.Infrastructure.Outbox`.
- Changing `OrderApiEndpoint`'s public HTTP routes, response shapes, status codes, or auth requirements.
- Changing CI/CD pipelines, Docker Compose, Kubernetes manifests, or Azure pipelines.
- Performance optimization. The CQRS-lite read-path decision is structural, not performance-driven.
