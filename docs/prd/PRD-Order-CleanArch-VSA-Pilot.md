# Order Service Clean Architecture + Vertical Slices Pilot PRD

## Problem Statement

The `Order.Service` codebase—and every other service in this repo—is organized by technical type: all endpoints in `Endpoints/`, all domain models in `Models/`, all integration event handlers in `IntegrationEvents/EventHandlers/`, all repositories in `Infrastructure/Data/`. To understand or change one feature (e.g., "what happens when an order is created?"), a developer must hop across four or five folders and reconstruct the feature mentally. Cross-cutting concerns leak across files (e.g., the `OrderContext.Translate` switch mixes EF persistence with domain-event-to-integration-event mapping inside the DbContext). Boundaries between domain, application, and infrastructure exist only as conventions—nothing enforces them, so they erode silently, especially under AI-assisted edits.

The team wants:

1. A codebase grouped by *what the application does* (features) rather than by technical type.
2. Clear, enforceable Clean Architecture boundaries: Domain has no infrastructure dependencies; Features depend on Domain + Contracts; Infrastructure implements interfaces.
3. A pattern proven on one service before being propagated, so we learn the shape cheaply.

## Solution

Pilot a Clean Architecture + Vertical Slice Architecture (VSA) layout on `Order.Service` only, with zero behavior change. Inside a single `Order.Service.csproj` (no extra projects), reorganize source into:

- `Features/<Slice>/` — one folder per inbound trigger (HTTP route or integration message), each self-contained: endpoint or event handler, request/response DTOs, slice-local handler class, slice-local DI registration extension, slice-local domain-event-to-integration-event mapper.
- `Domain/` — aggregates, value objects, domain events, abstractions (e.g., `IOrderStore`). No infrastructure references.
- `Contracts/Integration/` — cross-service integration event and command payloads.
- `Infrastructure/` — EF `DbContext`, EF configurations, providers (HTTP, Redis), outbox plumbing, internal ops endpoints. Implements abstractions declared in `Domain/`.

Slice handlers are invoked through plain DI (constructor injection of the handler class into the endpoint or event consumer)—no MediatR, no in-house dispatcher. Read slices project directly from the EF context to response DTOs (CQRS-lite); write slices go through the rich `Order` aggregate. Domain-event-to-integration-event translation moves out of `OrderContext` into per-slice mappers resolved by a generic outbox interceptor. Boundaries are enforced with both NetArchTest assertions (in `Order.Tests`) and Roslyn banned-namespace analyzers / `.editorconfig` rules. Tests are reshaped to mirror slices, with aggregate-level unit tests kept separate. Namespaces are renamed to match the new folder layout so the architecture is grep-able and analyzer-targetable. The work lands as staged commits on a single branch and merges via one PR. An ADR (0011) and a "how to add a new slice" runbook capture the pattern. Propagation to other services is deferred to a follow-up ADR.

## User Stories

1. As an Order service developer, I want to open a single folder to see everything the "create order" feature does, so that I do not have to reconstruct the feature from four scattered folders.
2. As an Order service developer, I want each slice to register its own dependencies via an `AddXxxSlice()` extension, so that adding a new feature is a drop-in change and `Program.cs` reads like a manifest.
3. As an Order service developer, I want to add a new HTTP endpoint by creating one new `Features/<Name>/` folder, so that I never need to touch unrelated handlers or DTOs.
4. As an Order service developer, I want to add a new integration-event consumer by creating one new `Features/<EventName>/` folder, so that event-driven features feel identical to HTTP features.
5. As an Order service developer, I want `Domain/Order.cs` to contain all business invariants (state transitions, totals, status rules) and `Features/<Slice>/Handler.cs` to be thin orchestration only, so that business rules cannot be silently bypassed by a slice handler taking shortcuts.
6. As an Order service developer, I want read slices (`GetOrder`, `ListOrders`) to project directly from EF to response DTOs, so that reads do not pay the cost of hydrating the aggregate and including child collections they don't need.
7. As an Order service developer, I want write slices to load the aggregate through `IOrderStore`, mutate it via domain methods (`Submit`, `TryConfirm`, `TryCancel`), and persist, so that the write path always enforces invariants.
8. As an Order service developer, I want each slice that emits an integration event to co-locate the domain-event-to-integration-event mapping with the slice that produces it, so that "what does this slice publish?" is answerable by reading one folder.
9. As an Order service maintainer, I want `OrderContext` to contain only persistence and unit-of-work logic—no event-translation switch—so that the DbContext stays a deep, single-purpose module.
10. As an Order service maintainer, I want a generic `DomainEventOutboxInterceptor` that resolves per-event mappers via DI, so that adding a new domain event requires only adding a new mapper, not touching a central switch.
11. As an Order service maintainer, I want NetArchTest rules that fail the test suite if `Domain` references infrastructure, if any slice references another slice, or if infrastructure leaks past Domain + Contracts, so that boundary violations are caught in CI rather than in code review.
12. As an Order service maintainer, I want Roslyn banned-symbol analyzers as a second guardrail beside NetArchTest, so that violations surface as compiler errors during development—not only when tests run.
13. As an Order service contributor, I want the cross-slice sharing rule documented as "duplicate first, extract on third" with a NetArchTest rule forbidding slice-to-slice references, so that I do not accidentally create a hidden coupling between two slices.
14. As an Order service contributor, I want namespaces to match the new folder layout (`Order.Service.Domain`, `Order.Service.Features.CreateOrder`, `Order.Service.Contracts.Integration`, `Order.Service.Infrastructure.Data.EntityFramework`), so that I can grep for layer membership and analyzer rules can target namespaces.
15. As an Order service contributor, I want `Order.Tests` to mirror `Features/<Slice>/` while keeping `Domain/` aggregate tests separate, so that feature tests and domain unit tests are each easy to locate.
16. As an Order service contributor, I want `InternalOutboxEndpoints` (DLQ-poller ops surface) to live under `Infrastructure/Outbox/`, not under `Features/`, so that operational plumbing does not pollute the feature manifest.
17. As a reviewer, I want the pilot to land as staged commits on one branch and a single PR, with each commit building and tests passing, so that the refactor is bisectable and reviewable end-to-end.
18. As a reviewer, I want zero behavior change from the pilot—every existing Order test passes unchanged—so that the layout migration cannot regress functional behavior.
19. As a release engineer, I want the pilot to leave `ECommerce.Shared` public API unchanged, so that other services are not forced to consume a breaking shared package version.
20. As a release engineer, I want the pre-commit hook (`dotnet format`, `dotnet build`, `dotnet test`) to gate every commit on the refactor branch, so that the branch cannot accumulate partial-validation commits.
21. As an architect, I want an ADR (0011) describing the layout and a runbook describing how to add a new slice, so that the pattern is documented before it propagates and the rationale is preserved.
22. As an architect, I want the decision to propagate the pattern to other services (basket, product, auth, inventory, shipping, payment, saga) to be a separate ADR after the pilot lands, so that propagation is informed by what we learned from the pilot.
23. As an AI-assisted contributor, I want the layout, namespaces, and architecture rules to be self-describing and analyzer-enforced, so that AI edits cannot silently drift across boundaries.
24. As an operator, I want the DLQ poller's call to `/internal/outbox/failed` (gated by `RequireService`) to continue working after the refactor, so that DLQ ingestion is not interrupted.
25. As an operator, I want trace IDs and correlation IDs to propagate identically through HTTP → saga `ConfirmOrderCommand` → outbox `OrderConfirmedEvent` after the refactor, so that observability dashboards do not break.

## Implementation Decisions

### Pilot scope

- Pilot is `Order.Service` only. No other service changes. Propagation handled by a follow-up ADR.

### Project shape

- Single `Order.Service.csproj` is retained. No split into `Order.Domain` / `Order.Application` / `Order.Infrastructure` projects.
- Boundaries are enforced by namespace conventions + analyzer rules + architecture tests, not by csproj references.

### Folder topology

- `Features/<Slice>/` — one folder per inbound trigger. Slice = one HTTP route OR one integration message handler. Each slice owns its handler, request/response DTOs, slice DI extension, and (if it emits an integration event) its domain-event-to-integration-event mapper.
- `Domain/` — aggregates, value objects, domain events, `IDomainEvent`, `Entity` base, and `Abstractions/IOrderStore`. No EF, no HTTP, no Redis references.
- `Contracts/Integration/` — cross-service event and command payload classes (e.g., `OrderCreatedEvent`, `ConfirmOrderCommand`).
- `Infrastructure/Data/EntityFramework/` — `OrderContext`, `EfOrderStore` (impl of `IOrderStore`), EF configurations.
- `Infrastructure/Providers/` — HTTP product catalog client, Redis product price provider.
- `Infrastructure/Outbox/` — generic `DomainEventOutboxInterceptor`, `InternalOutboxEndpoints` (ops surface, `RequireService`).
- `Migrations/` — unchanged; `generated_code = true`.

### Dispatch model

- No MediatR, no in-house mediator.
- Endpoints and integration-event consumers take their slice handler class via constructor injection and call `HandleAsync(...)` directly.
- Slice handler classes are sealed and have one public async method.

### Domain richness rule

- Rich domain: `Order` aggregate owns invariants and state transitions. Existing methods (`AddOrderProduct`, `Submit`, `TryConfirm`, `TryCancel`) are preserved.
- Slice handlers are orchestration only: load aggregate, call domain method, persist, optionally publish via outbox.
- Read slices bypass the aggregate and project directly from `OrderContext` to response DTOs.

### Persistence

- Single `IOrderStore` abstraction lives in `Domain/Abstractions/`.
- EF implementation `EfOrderStore` lives in `Infrastructure/Data/EntityFramework/`.
- `OrderContext` is persistence-only after the refactor; the existing `Translate(...)` switch is removed.

### Outbox / event translation seam

- A new abstraction `IIntegrationMap<TDomainEvent, TIntegrationEvent>` is introduced under `Infrastructure/Outbox/`.
- Each producing slice ships one mapper implementation co-located with the slice (e.g., `Features/CreateOrder/OrderCreatedIntegrationMap.cs`).
- A generic `DomainEventOutboxInterceptor` resolves mappers by domain-event runtime type via DI and calls `IOutboxStore.AddOutboxEvent` with the translated integration event.
- `OrderContext.ExecuteAsync` delegates domain-event publication to the interceptor rather than calling `Translate` itself.

### Slice DI

- Each slice exposes a static class with `AddXxxSlice(this IServiceCollection)` extension. The extension registers the handler, any slice-specific options, and (if applicable) calls existing shared infra (`AddEventHandler<TEvent, THandler>`).
- `Program.cs` chains slice extensions in a fluent manifest. Per-handler `AddScoped` and per-event `AddEventHandler` calls in `Program.cs` are removed and become slice-local.

### Namespaces

- Renamed to match folders: `Order.Service.Domain`, `Order.Service.Domain.Events`, `Order.Service.Domain.Abstractions`, `Order.Service.Features.<Slice>`, `Order.Service.Contracts.Integration`, `Order.Service.Infrastructure.Data.EntityFramework`, `Order.Service.Infrastructure.Providers`, `Order.Service.Infrastructure.Outbox`.

### Cross-slice sharing rule

- Rule of three: duplicate freely between slices; extract to `Domain/` (behavioral) or `Features/Shared/` (helper) only on the third use.
- NetArchTest forbids `Order.Service.Features.<X>` referencing `Order.Service.Features.<Y>` for any `X != Y`.

### Boundary enforcement

- NetArchTest rules in `Order.Tests/Architecture/LayoutTests.cs`:
  - `Domain` types must not reference `Order.Service.Infrastructure.*` or `Order.Service.Features.*`.
  - `Features.<X>` types must not reference `Features.<Y>` for distinct slices.
  - `Infrastructure` types may reference only `Domain` + `Contracts`.
  - `Contracts` types reference nothing internal.
- `.editorconfig` banned-symbol / banned-namespace analyzer rules act as a second guardrail (compile-time errors).

### Internal ops endpoints

- `InternalOutboxEndpoints` moves from `Endpoints/` to `Infrastructure/Outbox/InternalOutboxEndpoints.cs`.
- Wiring done from `Program.cs` after slice registration.
- `RequireService` policy gate preserved.

### Shared library

- `ECommerce.Shared` public API is unchanged. One incidental fix landed during Phase 5 (commit `dcbc29c`): `RabbitMqStartupExtensions` switched to a lazy `IRabbitMqConnection` singleton factory so the test host does not eagerly open a RabbitMQ connection during `WebApplicationFactory<Program>` boot. Package version bumped 2.23.0 → 2.24.0; no production behavior change.

### Validation

- Out of scope for the pilot. The existing absence of `FluentValidation` / `DataAnnotations` is preserved. A note in ADR 0011 lists "add per-slice FluentValidation" as a follow-up.

### Rollout

- Branch `refactor/order-vsa`. Staged commits land in this order, each green:
  1. Scaffold NetArchTest project dependency + skipped layout tests.
  2. Move files into `Domain/`, `Contracts/Integration/`; rename namespaces.
  3. Extract `IIntegrationMap<,>` + `DomainEventOutboxInterceptor`; remove `OrderContext.Translate`.
  4. Extract slices one at a time: `CreateOrder`, `GetOrder`, `ListOrders`, `ConfirmOrder`, `CancelOrder`, `ProductCreated`, `ProductPriceUpdated`.
  5. Move `InternalOutboxEndpoints` to `Infrastructure/Outbox/`.
  6. Reshape `Order.Tests` to mirror slices.
  7. Unskip and enable NetArchTest rules; add `.editorconfig` / banned-symbol analyzer rules.
  8. ADR 0011 + adding-a-new-slice runbook + root `CLAUDE.md` reference.
- Single PR for review. Pre-commit hook gates every commit (no `--no-verify`, no validation deferral).

## Testing Decisions

### Test philosophy

- A good test verifies external behavior of a module through its public interface, not internal implementation details.
- Refactor must produce zero behavior change. Every existing `Order.Tests` test must continue to pass without modification beyond namespace updates required by the rename.
- New tests are added only for new seams (the outbox interceptor + integration maps) and for the architecture rules themselves.

### Modules to test

- **`Order` aggregate (unchanged tests)** — existing `Order.Tests/Domain/OrderTests.cs` covers `Submit`, `TryConfirm`, `TryCancel`, `AddOrderProduct` invariants. Kept verbatim, only namespace touched.
- **Per-slice handler tests** — existing `Order.Tests/Api/*` and `Order.Tests/IntegrationEvents/*` tests migrate into `Order.Tests/Features/<Slice>/` without behavioral changes. They continue to use `OrderWebApplicationFactory` and `IntegrationTestBase`.
- **`DomainEventOutboxInterceptor`** — new unit tests covering: given a change tracker with a tracked entity carrying domain events, mappers are resolved per domain-event runtime type and emit one outbox event per domain event; an unmapped domain-event type fails fast with a descriptive error (mirrors current `OrderContext.Translate` `InvalidOperationException`).
- **Per-slice `IIntegrationMap<TDomainEvent, TIntegrationEvent>` implementations** — small pure-function tests that assert the mapping preserves IDs, customer IDs, items, currency, and any other field-level detail. One test class per mapper.
- **`Order.Tests/Architecture/LayoutTests.cs`** — NetArchTest rules tests that act as the executable specification of the boundary policy. These tests fail if any future contributor (human or AI) introduces a cross-boundary reference.
- **`EfOrderStore`** — already covered indirectly by integration tests through `WebApplicationFactory<Program>`. No new tests added unless the impl changes beyond the rename.

### Prior art in the codebase

- `Order.Tests/IntegrationTestBase.cs` + `Order.Tests/OrderWebApplicationFactory.cs` — existing factory + base used by all current integration tests. Refactor preserves both at the root of the tests project.
- `Order.Tests/Domain/OrderTests.cs` — existing aggregate-level unit tests. Pattern of `Given_When_Then` underscored display names is preserved (`CA1707` suppressed via `Directory.Build.props`).
- `payment-microservice/Payment.Tests/` and `saga-microservice/Saga.Tests/` — both follow the `WebApplicationFactory<Program>` integration pattern, useful prior art for the migrated `Features/<Slice>/*EndpointTests.cs` files.
- Pre-commit hook (`dotnet husky run --group pre-commit`) enforces `dotnet format --verify-no-changes` and `dotnet build --no-restore` + Basket tests on every commit. Order tests are run manually per the root `CLAUDE.md` sandbox policy before pushing.

## Out of Scope

- Refactoring any other service (basket, product, auth, inventory, shipping, payment, saga, api-gateway). Propagation is a follow-up ADR.
- Modifying `ECommerce.Shared`. The pilot composes existing `AddSqlServerDatastore`, `AddOutbox`, `AddPlatformEventBus`, `AddEventHandler`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddPlatformOpenApi`, `AuthorizationPolicies.RequireServicePolicy`.
- Adding request validation (FluentValidation or DataAnnotations). Listed as a follow-up in ADR 0011.
- Introducing MediatR or any mediator-style dispatcher.
- Splitting `Order.Service.csproj` into multiple projects.
- Changing the `Order` / `OrderProduct` database schema. No new EF migrations.
- Changing integration event payload contracts. Only their location (folder + namespace) moves.
- Changing the outbox table, dispatcher, or retry/DLQ behavior in `ECommerce.Shared.Infrastructure.Outbox`.
- Changing `OrderApiEndpoint`'s public HTTP routes, response shapes, status codes, or auth requirements.
- Changing CI/CD pipelines, Docker Compose, Kubernetes manifests, or Azure pipelines.
- Performance optimization. The CQRS-lite read-path decision is structural, not performance-driven.

## Further Notes

- Order is the pilot because it has the richest mix of concerns: SQL + Redis, outbox, saga participation, multiple inbound triggers (HTTP + saga commands + product-catalog events), rich domain with state transitions, and pre-existing domain-event infrastructure. If the layout works here, it generalizes; if it fails here, narrower services would not have surfaced the failure.
- The `OrderContext.Translate` switch is the most concrete pre-existing smell the pilot must resolve. It mixes EF persistence with cross-service event translation in one infra class. Extracting `IIntegrationMap<,>` + `DomainEventOutboxInterceptor` is the deepest module change in the pilot; everything else is relocation + namespace renames.
- NetArchTest + Roslyn analyzers are intentionally redundant. NetArchTest is expressive but only fires during `dotnet test`; banned-symbol analyzers fire during build, giving fast feedback in editor. The "belt + suspenders" choice is justified by the AI-assisted contribution model—violations need to surface at the earliest possible moment.
- The "duplicate first, extract on third" rule is load-bearing. It is the single most common reason VSA codebases drift back into technical-layer organization (premature `Common/` folder). The NetArchTest slice-to-slice rule mechanically enforces it.
- After the pilot ADR (0011) lands and at least one follow-up review pass, a separate ADR will propose propagation. Candidate propagation order if approved: inventory (saga participant, similar shape) → payment (saga participant) → shipping → saga (orchestrator, different shape, last to validate the layout generalizes to non-CRUD services) → product → auth → basket (Redis-only, least benefit).
- Behavioral guidance from root `CLAUDE.md` applies: surgical changes only, no improving adjacent code, match existing style, push back on over-engineering. The pilot is large in line count but mechanical in intent.
