# ADR-0011 — Order service Clean Architecture + Vertical Slice pilot

- **Status**: Accepted
- **Date**: 2026-05-22

## Context

`Order.Service` — and every other service in this repo — was organized by technical type: all endpoints in `Endpoints/`, all DTOs in `ApiModels/`, all domain models in `Models/`, all integration-event handlers in `IntegrationEvents/EventHandlers/`, all persistence in `Infrastructure/Data/`. Understanding or changing a single feature ("what happens when an order is created?") required hopping across four or five folders. Cross-cutting concerns leaked across files: most visibly, `OrderContext.Translate` mixed EF persistence with domain-event-to-integration-event mapping inside the DbContext. Boundaries between domain, application, and infrastructure existed only as conventions, with nothing to stop them eroding under AI-assisted edits.

The replacement is specified in [PRD #152](https://github.com/daonhan/Microservices-in-.NET/issues/152). The pilot lands on `Order.Service` only so the team can learn the shape on the service with the richest mix of concerns (SQL + Redis, outbox, saga participation, HTTP + event triggers, rich domain) before deciding whether to propagate.

## Decision

Reorganize `Order.Service` into a Clean Architecture + Vertical Slice (VSA) layout, with zero behavior change, inside the existing single `Order.Service.csproj`. The new layout, the dispatch model, the outbox seam, and the cross-slice rules are listed below. Source lives under [`order-microservice/Order.Service/`](../../order-microservice/Order.Service/); tests under [`order-microservice/Order.Tests/`](../../order-microservice/Order.Tests/).

### Pilot scope

- The pilot is `Order.Service` only. No other service changes in this ADR. Propagation to basket, product, auth, inventory, shipping, payment, saga is **out of scope here** and will be decided in a separate ADR informed by pilot learnings.
- `ECommerce.Shared` public API is unchanged. One incidental fix landed during Phase 5 (commit `dcbc29c`): `RabbitMqStartupExtensions` switched eager `AddSingleton<IRabbitMqConnection>(new RabbitMqConnection(...))` to lazy `AddSingleton<IRabbitMqConnection>(_ => new RabbitMqConnection(...))` so the test host does not eagerly open a RabbitMQ connection during `WebApplicationFactory<Program>` boot. Package version bumped 2.23.0 → 2.24.0; no consumer behavior change in production.

### Project shape — single csproj

- The pilot keeps a single `Order.Service.csproj`. No split into `Order.Domain` / `Order.Application` / `Order.Infrastructure` projects.
- Boundaries are enforced by namespace conventions + analyzer rules + architecture tests, not by csproj references. The "belt and suspenders" reason is given below.

### Folder topology

- `Features/<Slice>/` — one folder per inbound trigger (HTTP route or integration message). Each slice is self-contained: endpoint or event consumer, request/response DTOs, sealed handler, slice DI extension, and (when the slice emits an integration event) co-located `IIntegrationMap<,>` mapper. Existing slices: `CreateOrder`, `GetOrder`, `ListOrders`, `ConfirmOrder`, `CancelOrder`, `ProductCreated`.
- `Domain/` — aggregates (`Order`, `OrderProduct`), value objects, `OrderStatus`, domain events under `Domain/Events/`, `IDomainEvent`, `Entity` base, and `Domain/Abstractions/IOrderStore`. No EF, HTTP, or Redis references.
- `Contracts/Integration/` — cross-service integration event payloads (`OrderCreatedEvent`, `OrderConfirmedEvent`, `OrderCancelledEvent`, `ProductCreatedEvent`). Inbound payloads received from other services live here; outbound payloads produced by this service live here.
- `Infrastructure/Data/EntityFramework/` — `OrderContext`, `EfOrderStore`, EF configurations.
- `Infrastructure/Providers/` — HTTP product-catalog client, Redis product-price provider.
- `Infrastructure/Outbox/` — generic `DomainEventOutboxInterceptor`, `IIntegrationMap<,>`, `InternalOutboxEndpoints` (ops surface gated by `RequireService`).
- `Migrations/` — unchanged; `generated_code = true`.

### Dispatch model — direct DI, no MediatR

- No MediatR. No in-house mediator. No reflection-based pipeline.
- Endpoints and integration-event consumers take their slice handler class via constructor injection and call `HandleAsync(...)` directly.
- Slice handler classes are `internal sealed` with one public async method.
- Rationale: at one handler per slice the indirection of a mediator buys nothing the compiler can't already enforce, and direct calls keep stack traces and DI registration legible.

### Domain richness rule + CQRS-lite read split

- The `Order` aggregate owns all invariants and state transitions. Existing methods (`AddOrderProduct`, `Submit`, `TryConfirm`, `TryCancel`) are preserved and remain the only legitimate way to mutate state.
- **Write slices** (`CreateOrder`, `ConfirmOrder`, `CancelOrder`): load the aggregate through `IOrderStore`, call domain methods, persist. Handlers are orchestration only.
- **Read slices** (`GetOrder`, `ListOrders`): bypass the aggregate and project directly from `OrderContext` to response DTOs. Reads do not pay the cost of hydrating the aggregate and including child collections they do not need.
- **Event slices** (`ProductCreated`): take the integration-event payload and apply it, going through the aggregate when state mutates.

### Outbox translation seam — `IIntegrationMap<,>` + `DomainEventOutboxInterceptor`

- Domain-event-to-integration-event translation moves out of `OrderContext` and into per-slice mappers.
- `IIntegrationMap<TDomainEvent, TIntegrationEvent>` is declared under `Infrastructure/Outbox/`. Each producing slice ships one mapper implementation **co-located with the slice** (e.g. `Features/CreateOrder/OrderCreatedIntegrationMap.cs`).
- `DomainEventOutboxInterceptor` resolves mappers by domain-event runtime type via DI and calls `IOutboxStore.AddOutboxEvent` with the translated integration event. Unmapped domain-event types fail fast with a descriptive error (mirrors the pre-refactor `OrderContext.Translate` `InvalidOperationException`).
- `OrderContext` is now persistence-only; the `Translate` switch is removed. Adding a new domain event requires adding a new mapper, not editing a central switch.

### Cross-slice rule — duplicate first, extract on third

- Slices may **not** reference one another. `Order.Service.Features.<X>` never references `Order.Service.Features.<Y>` for any `X != Y`.
- Duplicate freely between slices. Extract to `Domain/` (behavioral) or `Features/Shared/` (helper) only on the **third** use.
- Rationale: this is the single most common reason VSA codebases drift back into technical-layer organization (premature `Common/` folder). The slice-to-slice rule mechanically prevents the drift.

### Boundary enforcement — belt and suspenders

Two redundant guardrails run on every commit; the redundancy is intentional, because they fire at different times.

- **NetArchTest** ([`Order.Tests/Architecture/LayoutTests.cs`](../../order-microservice/Order.Tests/Architecture/LayoutTests.cs)). Four rules, all enabled (no `Skip`):
  1. `Order.Service.Domain.*` may not reference `Order.Service.Infrastructure.*` or `Order.Service.Features.*`.
  2. `Order.Service.Features.<X>.*` may not reference `Order.Service.Features.<Y>.*` for any `X != Y`.
  3. `Order.Service.Infrastructure.*` may not reference `Order.Service.Features.*`.
  4. `Order.Service.Contracts.*` may not reference any other internal `Order.Service.*` namespace.
- **Roslyn `LayoutAnalyzer`** in `Order.Service` raises the same four rules as build-time compiler errors via `.editorconfig` configuration.
- NetArchTest is expressive but only fires during `dotnet test`; the analyzer fires during `dotnet build`, including in IDE quick-feedback. AI-assisted edits surface violations at the earliest possible moment.

### Namespace conventions

Namespaces match folders so the architecture is grep-able and analyzer-targetable:

- `Order.Service.Domain`, `Order.Service.Domain.Events`, `Order.Service.Domain.Abstractions`
- `Order.Service.Features.<Slice>`
- `Order.Service.Contracts.Integration`
- `Order.Service.Infrastructure.Data.EntityFramework`, `Order.Service.Infrastructure.Providers`, `Order.Service.Infrastructure.Outbox`

### Composition root as manifest

Each slice exposes `AddXxxSlice(this IServiceCollection)`. `Program.cs` chains these into a fluent manifest; per-handler `AddScoped` and per-event `AddEventHandler` calls move into the slice extension. Reading `Program.cs` answers "what features does this service expose?" in one screen.

## Follow-ups (explicitly not in pilot scope)

- **Per-slice FluentValidation.** The pilot preserves the existing absence of `FluentValidation` / `DataAnnotations`. Adding per-slice request validation is a follow-up, tracked separately.
- **Propagation to other services.** A separate ADR will propose whether to propagate the layout to basket, product, auth, inventory, shipping, payment, and saga, informed by pilot learnings. If approved, the candidate propagation order is **inventory → payment → shipping → saga → product → auth → basket** (saga participants first because they share Order's shape; saga itself last because its orchestrator shape stresses the layout differently; basket last because Redis-only services benefit least).
- **Domain events on read slices.** Read slices currently do not emit domain events. Behavior is unchanged from pre-refactor.

## Consequences

- One folder per feature: opening `Features/CreateOrder/` shows everything the slice does, including its outbound integration-event mapping.
- `OrderContext` is now a single-purpose persistence module; adding a new domain event no longer requires editing a central switch.
- Two guardrails (NetArchTest + Roslyn analyzer) catch boundary violations at test time and at build time. AI-assisted edits cannot silently cross boundaries.
- Cost: contributors need to internalize the rule-of-three to avoid premature `Features/Shared/`. The slice-to-slice NetArchTest rule enforces this mechanically.
- Cost: slice DI extensions add one file per slice. Justified by the manifest-style `Program.cs` and the locality benefit.
- Rollback within the pilot is straightforward: revert the staged commits on `refactor/order-cleanarch-vsa`. No data, no schema, no contract changes were made.
- Propagation is now a single follow-up decision rather than a multi-service refactor commitment.
