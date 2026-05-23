# Payment Service Clean Architecture + Vertical Slices Pilot PRD

> Tracking issue: [#226](https://github.com/daonhan/Microservices-in-.NET/issues/226). Modeled on epic [#152](https://github.com/daonhan/Microservices-in-.NET/issues/152) (Order pilot). Composes ADR [0011](../adr/0011-order-cleanarch-vsa-pilot.md) by reference; no new ADR.
> Branch: `refactor/payment-vsa`. Single PR for review.

## Problem Statement

The `Payment.Service` codebase is organized by technical type, like every pre-pilot service in this repo: all HTTP routes in one `Endpoints/PaymentApiEndpoints.cs` file, every saga command consumer in `IntegrationEvents/EventHandlers/`, domain models in `Models/`, persistence in `Infrastructure/Data/`, the payment-gateway abstraction in `Infrastructure/Gateways/`, metrics in a top-level `Observability/` folder. To understand or change one feature ("what happens when a saga authorizes a payment?") a developer must hop across `IntegrationEvents/EventHandlers/AuthorizePaymentCommandHandler.cs`, `Models/Payment.cs`, `Infrastructure/Data/EntityFramework/PaymentContext.cs`, `Infrastructure/Gateways/InMemoryPaymentGateway.cs`, and `IntegrationEvents/Events/PaymentAuthorizedEvent.cs`, then reconstruct the feature mentally.

The service also carries a concrete architectural smell inherited from the pre-pilot era: `PaymentContext.Translate(...)` mixes EF persistence with cross-service event translation inside the `DbContext`, and individual saga command handlers (e.g. `AuthorizePaymentCommandHandler`) work around it by manually calling `payment.DequeueDomainEvents()` to drain the queue before publishing a hand-crafted reply event. The result is a hybrid path: HTTP writes (`Capture`, `Refund`) flow through `Translate`; saga writes bypass it. Behavior is correct today, but the seam is fragile — any future contributor adding a saga handler can forget the manual drain and silently duplicate-publish.

Boundaries between domain, application, and infrastructure exist only as conventions. Nothing prevents `Models/Payment.cs` from picking up EF Core or `IPaymentGateway` references; nothing prevents a future contributor (human or AI) from adding a new endpoint inside `PaymentApiEndpoints.cs` that bypasses the outbox or skips the gateway adapter.

The team wants:

1. A codebase grouped by *what the application does* (one inbound trigger per folder), not by technical type.
2. Enforceable Clean Architecture boundaries: Domain has no infrastructure dependencies; Features depend on Domain + Contracts; Infrastructure implements abstractions declared in Domain.
3. The `PaymentContext.Translate` smell resolved and the saga-handler manual-drain workaround removed, replaced by a single, uniform outbox-translation seam.
4. A pattern consistent with the prior six pilots (Order, Product, Basket, Auth, Inventory, Shipping) so the project's mental model stays uniform.

## Solution

Pilot Clean Architecture + Vertical Slice Architecture (VSA) on `Payment.Service` only, with zero behavior change. Inside a single `Payment.Service.csproj`, reorganize source into:

- `Features/<Slice>/` — one folder per inbound trigger (HTTP route or integration message). Each slice owns its endpoint or consumer, request/response DTOs, slice DI extension, slice handler, and (for slices that produce a payment domain event for the first time) a co-located `IIntegrationMap<,>` implementation.
- `Domain/` — `Payment` aggregate, `PaymentStatus` enum, `OrderCustomer` (idempotency record), domain events (`PaymentAuthorizedDomainEvent`, `PaymentFailedDomainEvent`, `PaymentCapturedDomainEvent`, `PaymentRefundedDomainEvent`, `PaymentVoidedDomainEvent`), `IDomainEvent` marker, `Entity` base, and `Abstractions/IPaymentStore` + `Abstractions/IPaymentGateway`. No EF, HTTP, or payment-SDK references.
- `Contracts/Integration/` — cross-service event payloads (`PaymentAuthorizedEvent`, `PaymentFailedEvent`, `PaymentCapturedEvent`, `PaymentRefundedEvent`, `PaymentVoidedEvent`, inbound `OrderCreatedEvent`).
- `Infrastructure/Data/EntityFramework/` — `PaymentContext`, `EfPaymentStore` (impl of `IPaymentStore`), EF configurations, `PaymentContextDesignTimeFactory`, `PaymentContextSeed`.
- `Infrastructure/Gateways/` — `InMemoryPaymentGateway` (impl of `IPaymentGateway`).
- `Infrastructure/Observability/` — `PaymentMetrics`.
- `Infrastructure/Outbox/` — `DomainEventOutboxInterceptor`, `InternalOutboxEndpoints` (`RequireService`-gated ops endpoint).

Slice handlers are invoked through plain DI (constructor injection into the endpoint or event consumer). No MediatR, no in-house dispatcher. Read slices project directly from the EF context to response DTOs (CQRS-lite); write slices go through `IPaymentStore`, call methods on the `Payment` aggregate, and rely on the outbox interceptor to translate domain events into integration events. The existing `PaymentContext.Translate` switch is extracted into per-slice `IIntegrationMap<TDomainEvent, TIntegrationEvent>` implementations resolved by a generic `DomainEventOutboxInterceptor`. Saga command handlers drop their manual `DequeueDomainEvents()` workaround and let the interceptor handle event publication uniformly — including carrying `CorrelationId` / `CausationId` / `SagaId` from the inbound command through to the outbox event.

Boundaries enforced with both NetArchTest assertions (in `Payment.Tests/Architecture/LayoutTests.cs`) and a Roslyn `Payment.Service.LayoutAnalyzer`. Tests are reshaped to mirror slices, with aggregate-level unit tests kept separate under `Payment.Tests/Domain/`. Namespaces are renamed to match the new folder layout so the architecture is grep-able and analyzer-targetable. The work lands as staged commits on a single branch and merges via one PR. The CLAUDE.md "Payment service exception" entry composes ADR [0011](../adr/0011-order-cleanarch-vsa-pilot.md) by reference (no new ADR) and reuses the existing [adding-a-new-slice.md](../runbooks/adding-a-new-slice.md) runbook unchanged.

## User Stories

1. As a Payment service developer, I want to open a single folder to see everything the "authorize payment" feature does, so that I do not have to reconstruct the feature from `AuthorizePaymentCommandHandler.cs`, `Payment.cs`, `InMemoryPaymentGateway.cs`, `PaymentContext.cs`, and `PaymentAuthorizedEvent.cs` separately.
2. As a Payment service developer, I want each slice to register its own dependencies via an `AddXxxSlice()` extension, so that adding a new feature is a drop-in change and `Program.cs` reads like a manifest.
3. As a Payment service developer, I want to add a new HTTP endpoint by creating one new `Features/<Name>/` folder, so that I never need to touch unrelated handlers or DTOs in `PaymentApiEndpoints.cs`.
4. As a Payment service developer, I want to add a new saga command consumer or integration-event consumer by creating one new `Features/<Name>/` folder, so that message-driven features feel identical to HTTP features.
5. As a Payment service developer, I want `Domain/Payment.cs` to contain all business invariants (status state machine: Pending → Authorized → Captured → Refunded, terminal Failed / Voided branches, idempotent transition rules) and `Features/<Slice>/Handler.cs` to be thin orchestration only, so that business rules cannot be silently bypassed by a slice handler taking shortcuts.
6. As a Payment service developer, I want read slices (`GetPaymentById`, `GetPaymentByOrder`) to project directly from `PaymentContext` to `PaymentResponse`, so that reads do not pay the cost of hydrating the `Payment` aggregate.
7. As a Payment service developer, I want HTTP write slices (`CapturePayment`, `RefundPayment`) to load the aggregate through `IPaymentStore`, mutate it via domain methods (`Capture`, `Refund`), persist via `outboxUnitOfWork.ExecuteAsync(...)`, and rely on the outbox interceptor to emit the integration event, so that the write path always enforces invariants and never duplicate-publishes.
8. As a Payment service developer, I want saga command consumers (`AuthorizePaymentCommand`, `CapturePaymentCommand`, `VoidPaymentCommand`, `RefundPaymentCommand`) to follow the same write-slice rule — load aggregate, call domain method, persist, let the interceptor publish — so that the saga reply (`PaymentAuthorizedEvent` / `PaymentFailedEvent` / `PaymentCapturedEvent` / `PaymentVoidedEvent` / `PaymentRefundedEvent`) carrying `CorrelationId` / `CausationId` / `SagaId` is uniform across HTTP and saga paths.
9. As a Payment service developer, I want the existing `AuthorizePaymentCommandHandler` manual `payment.DequeueDomainEvents()` workaround removed, so that no slice handler has to know about the translation seam to avoid duplicate-publish.
10. As a Payment service developer, I want `OrderCreated` to be its own slice (`Features/OrderCreated/`) that records the order's customer id into the `OrderCustomers` idempotency table, so that the customer-id-lookup-on-authorize race condition stays localized in one folder rather than scattered across infra + handler.
11. As a Payment service maintainer, I want `PaymentContext` to remain a single-purpose persistence module after the refactor — no `Translate(...)` switch, no event-translation logic — so that the DbContext stays a deep module focused on persistence and unit-of-work only.
12. As a Payment service maintainer, I want a generic `DomainEventOutboxInterceptor` that resolves per-event mappers via DI, so that adding a new payment domain event requires only adding a new `IIntegrationMap<,>` implementation, not touching a central switch or any handler.
13. As a Payment service maintainer, I want each `IIntegrationMap<,>` co-located with the slice that produces the domain event for the first time, so that "what does this slice publish?" is answerable by reading one folder.
14. As a Payment service maintainer, I want a documented convention for multi-producer domain events: the `IIntegrationMap<,>` lives in the HTTP slice when both an HTTP slice and a saga slice raise the same domain event; the saga slice raises the same domain event and the interceptor resolves the shared map globally via DI, so that "where does this mapper live?" has one rule for `PaymentCapturedDomainEvent` (CapturePayment HTTP + CapturePaymentCommand saga) and `PaymentRefundedDomainEvent` (RefundPayment HTTP + RefundPaymentCommand saga).
15. As a Payment service maintainer, I want `IPaymentStore` and `IPaymentGateway` declared in `Domain/Abstractions/`, with EF and gateway implementations under `Infrastructure/Data/EntityFramework/` and `Infrastructure/Gateways/`, so that slices and tests depend on the abstraction without pulling EF or gateway SDK references into Domain.
16. As a Payment service maintainer, I want NetArchTest rules that fail the test suite if `Domain` references infrastructure, if any slice references another slice, or if infrastructure leaks past Domain + Contracts, so that boundary violations are caught in CI rather than in code review.
17. As a Payment service maintainer, I want a Roslyn `Payment.Service.LayoutAnalyzer` as a second guardrail beside NetArchTest, so that violations surface as compiler errors during development — not only when tests run.
18. As a Payment service maintainer, I want `PaymentMetrics` moved from top-level `Observability/` to `Infrastructure/Observability/`, so that the top-level folder layout matches prior pilots exactly (Inventory, Shipping).
19. As a Payment service contributor, I want the cross-slice sharing rule documented as "duplicate first, extract on third" with a NetArchTest rule forbidding slice-to-slice references, so that I do not accidentally create hidden coupling between two slices.
20. As a Payment service contributor, I want namespaces to match the new folder layout (`Payment.Service.Domain`, `Payment.Service.Features.AuthorizePaymentCommand`, `Payment.Service.Contracts.Integration`, `Payment.Service.Infrastructure.Data.EntityFramework`, `Payment.Service.Infrastructure.Gateways`, `Payment.Service.Infrastructure.Observability`, `Payment.Service.Infrastructure.Outbox`), so that I can grep for layer membership and analyzer rules can target namespaces.
21. As a Payment service contributor, I want HTTP `CapturePayment` (POST `/{paymentId}/capture`) and saga `CapturePaymentCommand` consumer to be two distinct slices (`Features/CapturePayment/` + `Features/CapturePaymentCommand/`), and likewise `RefundPayment` (POST `/{paymentId}/refund`) and `RefundPaymentCommand`, mirroring Shipping's `CancelShipment` vs `CancelShipmentCommand` convention, so that "one inbound trigger = one slice" stays true.
22. As a Payment service contributor, I want `Payment.Tests` reshaped to mirror `Features/<Slice>/` while keeping `Payment.Tests/Domain/` aggregate tests separate, so that feature tests and domain unit tests are each easy to locate.
23. As a Payment service contributor, I want `InternalOutboxEndpoints` (the DLQ-poller ops surface) under `Infrastructure/Outbox/`, not under `Features/`, so that operational plumbing does not pollute the feature manifest.
24. As a reviewer, I want the pilot to land as staged commits on one branch and a single PR, with each commit building and tests passing, so that the refactor is bisectable and reviewable end-to-end.
25. As a reviewer, I want zero behavior change from the pilot — every existing `Payment.Tests` test passes unchanged (modulo namespace updates), so that the layout migration cannot regress functional behavior. In particular, the saga reply correlation-id propagation, the `AuthorizePaymentCommand` idempotency-by-existing-payment path, the `OrderCreated` → `OrderCustomers` cache, the gateway latency metric, the admin-role gating on `Capture`/`Refund`, and the customer-id ownership check on read endpoints remain byte-for-byte identical.
26. As a release engineer, I want the pilot to leave `ECommerce.Shared` untouched (no nupkg version bump), so that other services are not forced to consume a new shared package version.
27. As a release engineer, I want the pre-commit hook (`dotnet format`, `dotnet build`, then Basket tests) to gate every commit on the refactor branch, so that the branch cannot accumulate partial-validation commits. Payment tests run manually before pushing.
28. As an architect, I want a CLAUDE.md "Payment service exception" entry that composes ADR-0011 by reference (no new ADR) and reuses the existing adding-a-new-slice runbook unchanged, so that documentation stays DRY across pilots.
29. As an architect, I want the CLAUDE.md entry to explicitly call out payment-specific divergences vs prior pilots (multi-producer mapper convention; `IIntegrationMap<,>`/`DomainEventOutboxInterceptor` reintroduced after the Inventory/Shipping inline skip; `IPaymentGateway` lifted to `Domain/Abstractions/`; saga handler manual drain removed), so that future contributors understand why payment looks slightly different.
30. As an architect, I want the decision on whether to continue propagating to the remaining service (saga) to be a separate ADR after this pilot lands, so that propagation stays informed by pilot learnings — saga is the last pilot because its orchestrator shape stresses the layout differently than participant services.
31. As an AI-assisted contributor, I want layout, namespaces, and architecture rules self-describing and analyzer-enforced, so that AI edits cannot silently drift across boundaries.
32. As an operator, I want the DLQ poller's call to `/internal/outbox/failed` (gated by `RequireService`) to continue working after the refactor, so that DLQ ingestion is not interrupted.
33. As an operator, I want trace IDs and correlation IDs to propagate identically through HTTP/saga inbound → `Payment` mutation → outbox `Payment*` events after the refactor, so that observability dashboards do not break. `PaymentMetrics` counter and histogram names + tags stay identical (`payments_total`, authorize-latency histogram).

## Implementation Decisions

### Pilot scope

- Pilot is `Payment.Service` only. No other service changes.
- Propagation to the remaining service (saga) handled by a follow-up ADR.

### Project shape

- Single `Payment.Service.csproj`. No split into `Payment.Domain` / `Payment.Application` / `Payment.Infrastructure` projects.
- Boundaries enforced by namespace conventions + analyzer rules + architecture tests, not csproj references.

### Folder topology

- `Features/<Slice>/` — one folder per inbound trigger. Final slice list (9):
  - **Read (2):** `GetPaymentById/`, `GetPaymentByOrder/`.
  - **HTTP write (2):** `CapturePayment/`, `RefundPayment/`.
  - **Saga command consumers (4):** `AuthorizePaymentCommand/`, `CapturePaymentCommand/`, `VoidPaymentCommand/`, `RefundPaymentCommand/`.
  - **Integration event consumer (1):** `OrderCreated/`.
- `Domain/` — `Payment` aggregate, `PaymentStatus` enum, `OrderCustomer` idempotency record, `IDomainEvent`, `Entity` base, domain events (`PaymentAuthorizedDomainEvent`, `PaymentFailedDomainEvent`, `PaymentCapturedDomainEvent`, `PaymentRefundedDomainEvent`, `PaymentVoidedDomainEvent`), `Abstractions/IPaymentStore`, `Abstractions/IPaymentGateway`. No EF / HTTP / gateway-SDK references.
- `Contracts/Integration/` — `PaymentAuthorizedEvent`, `PaymentFailedEvent`, `PaymentCapturedEvent`, `PaymentRefundedEvent`, `PaymentVoidedEvent`, inbound `OrderCreatedEvent`.
- `Infrastructure/Data/EntityFramework/` — `PaymentContext`, `EfPaymentStore` (impl), `PaymentConfiguration`, `OrderCustomerConfiguration`, `PaymentContextDesignTimeFactory`, `PaymentContextSeed`, `EntityFrameworkExtensions`.
- `Infrastructure/Gateways/` — `InMemoryPaymentGateway` (impl of `Domain.Abstractions.IPaymentGateway`).
- `Infrastructure/Observability/` — `PaymentMetrics`.
- `Infrastructure/Outbox/` — `DomainEventOutboxInterceptor`, `IIntegrationMap<,>` abstraction, `InternalOutboxEndpoints` (`RequireService`-gated ops endpoint).
- `Migrations/` — unchanged; `generated_code = true`.

### Dispatch model

- No MediatR. No in-house mediator.
- Endpoints and message consumers take their slice handler class via constructor injection and call `HandleAsync(...)` directly.
- Slice handler classes are `internal sealed` with one public async method.

### Domain richness rule

- Rich domain: `Payment` aggregate owns invariants and state transitions. Existing methods (`Create`, `Authorize`, `Fail`, `Capture`, `Refund`, `Void`) preserved verbatim. Idempotent re-entry rules (e.g. `Capture` returns `false` when already captured, throws on illegal source state) preserved.
- Write-slice handlers (HTTP + saga) are orchestration only: load aggregate via `IPaymentStore`, call domain method, persist via `outboxUnitOfWork.ExecuteAsync`, rely on interceptor to publish.
- Read-slice handlers bypass the aggregate and project directly from `PaymentContext` to `PaymentResponse`.

### Persistence

- `IPaymentStore` moves from `Infrastructure/Data/IPaymentStore.cs` to `Domain/Abstractions/IPaymentStore.cs`. Surface is unchanged: `Add`, `GetById`, `GetByOrder`, `SaveChangesAsync`, `ExecuteAsync`, `RecordOrderCustomer`, `TryGetOrderCustomer`.
- EF implementation in `Infrastructure/Data/EntityFramework/EfPaymentStore.cs` (extracted from `PaymentContext` if needed, or `PaymentContext` itself retains the `IPaymentStore` implementation as today — decision deferred to extraction phase, both options preserve behavior). `PaymentContext` remains persistence-only after the refactor.

### Gateway

- `IPaymentGateway` moves from `Infrastructure/Gateways/IPaymentGateway.cs` to `Domain/Abstractions/IPaymentGateway.cs`. Surface is unchanged (`AuthorizeAsync`, `CaptureAsync`, `RefundAsync`, `VoidAsync` — whichever subset exists today is preserved).
- `InMemoryPaymentGateway` implementation stays under `Infrastructure/Gateways/InMemoryPaymentGateway.cs`. Mirrors Shipping's `ICarrierGateway` / `Infrastructure/Carriers/` pattern.

### Outbox translation seam — adopts Order pattern, diverges from Inventory/Shipping

- New abstraction `IIntegrationMap<TDomainEvent, TIntegrationEvent>` in `Infrastructure/Outbox/`.
- New generic `DomainEventOutboxInterceptor` resolves mappers by domain-event runtime type via DI and calls `IOutboxStore.AddOutboxEvent` with the translated integration event. Unmapped domain-event type fails fast with a descriptive error mirroring the current `PaymentContext.Translate` `InvalidOperationException` wording.
- `PaymentContext.ExecuteAsync` delegates domain-event publication to the interceptor rather than calling `Translate` itself. The `Translate(...)` switch is deleted.
- `AuthorizePaymentCommandHandler` (and any other saga handler that manually calls `payment.DequeueDomainEvents()` to suppress double-publish today) drops the manual drain. The interceptor publishes uniformly. Correlation propagation (`CorrelationId` / `CausationId` / `SagaId` from the inbound saga command onto the outbox event) is preserved — the slice handler attaches the correlation metadata to the `Payment` aggregate or to a per-request ambient via the existing outbox plumbing (concrete mechanism deferred to extraction phase; acceptance criterion is byte-identical event payload + headers).
- Each producing slice ships one `IIntegrationMap<,>` implementation co-located with the slice.

### Mapper home for multi-producer domain events

- For domain events with a single producing slice, the mapper lives in that slice's folder. Single-producer events: `PaymentAuthorizedDomainEvent` (only `AuthorizePaymentCommand` raises it), `PaymentFailedDomainEvent` (only `AuthorizePaymentCommand` raises it on gateway decline), `PaymentVoidedDomainEvent` (only `VoidPaymentCommand` raises it).
- For domain events with multiple producing slices, the mapper lives in the HTTP slice (rule: HTTP first when both HTTP + saga produce; otherwise the first slice extracted). Multi-producer events:
  - `PaymentCapturedDomainEvent` → mapper in `Features/CapturePayment/PaymentCapturedIntegrationMap.cs`; also raised by `Features/CapturePaymentCommand/`.
  - `PaymentRefundedDomainEvent` → mapper in `Features/RefundPayment/PaymentRefundedIntegrationMap.cs`; also raised by `Features/RefundPaymentCommand/`.
- Registration: the slice that owns the mapper file registers it in its `AddXxxSlice(...)` extension. The other producing slice does not duplicate the registration. The interceptor resolves the map globally from DI regardless of which slice's handler triggered the domain event.
- Cross-slice rule clarification: this is **not** a slice-to-slice code reference (no `using Payment.Service.Features.CapturePayment` from `Features/CapturePaymentCommand/`). The shared coupling flows through DI, not source.

### Slice DI

- Each slice exposes a static class with `AddXxxSlice(this IServiceCollection)` extension. The extension registers the handler, any slice-specific options, the slice's `IIntegrationMap<,>` if any, and (for saga / event consumer slices) calls `AddEventHandler<TEvent, THandler>` from `ECommerce.Shared.Infrastructure.EventBus`.
- `Program.cs` chains slice extensions in a fluent manifest. Per-handler `AddScoped` and per-event `AddEventHandler` calls move into slice extensions.

### Namespaces

- `Payment.Service.Domain`, `Payment.Service.Domain.Abstractions`, `Payment.Service.Domain.Events`.
- `Payment.Service.Features.<Slice>` (one per slice).
- `Payment.Service.Contracts.Integration`.
- `Payment.Service.Infrastructure.Data.EntityFramework`, `Payment.Service.Infrastructure.Gateways`, `Payment.Service.Infrastructure.Observability`, `Payment.Service.Infrastructure.Outbox`.

### Cross-slice sharing rule

- Rule of three: duplicate freely between slices; extract to `Domain/` (behavioral) or `Features/Shared/` (helper) only on the third use.
- NetArchTest rule forbids `Payment.Service.Features.<X>` referencing `Payment.Service.Features.<Y>` for any `X != Y`. (Multi-producer mapper sharing flows through DI, not source — see above.)

### Boundary enforcement (belt + suspenders)

- **NetArchTest** in `Payment.Tests/Architecture/LayoutTests.cs`. Four rules, all enabled:
  1. `Payment.Service.Domain.*` may not reference `Payment.Service.Infrastructure.*` or `Payment.Service.Features.*`.
  2. `Payment.Service.Features.<X>.*` may not reference `Payment.Service.Features.<Y>.*` for distinct slices.
  3. `Payment.Service.Infrastructure.*` may not reference `Payment.Service.Features.*`.
  4. `Payment.Service.Contracts.*` may not reference any other internal `Payment.Service.*` namespace.
- **Roslyn `Payment.Service.LayoutAnalyzer`** raises the same four rules as build-time compiler errors via `.editorconfig`.
- Both must fail on an intentional violation spike before being marked done.

### Internal ops endpoints

- `InternalOutboxEndpoints` moves from `Endpoints/InternalOutboxEndpoints.cs` to `Infrastructure/Outbox/InternalOutboxEndpoints.cs`.
- Wiring in `Program.cs` after slice registration.
- `RequireService` policy gate preserved on `/internal/outbox/failed`.

### Routes / contracts / payloads

- Public HTTP routes, response shapes, status codes, and auth requirements of `PaymentApiEndpoints` preserved unchanged: `GET /by-order/{orderId}` (authenticated, customer-id ownership check), `GET /{paymentId}` (authenticated, customer-id ownership check), `POST /{paymentId}/capture` (`Administrator` policy), `POST /{paymentId}/refund` (`Administrator` policy, optional body `{ amount }`).
- Integration event payload classes preserved — only their location (folder + namespace) moves.
- Saga command payloads preserved unchanged.

### Shared library

- `ECommerce.Shared` not modified. No `dotnet pack`, no nupkg version bump.

### Validation

- Out of scope. Existing absence of `FluentValidation` / `DataAnnotations` preserved. Listed as a follow-up in the CLAUDE.md exception entry.

### Rollout

- Branch `refactor/payment-vsa` (already current).
- Staged commits land in this order, each green:
  1. Scaffold NetArchTest project dependency + `Payment.Tests/Architecture/LayoutTests.cs` with rules initially skipped.
  2. Move domain types (`Payment`, `PaymentStatus`, `OrderCustomer`, `Entity`, `IDomainEvent`, `Payment*DomainEvent`) into `Domain/`; move `IPaymentStore` to `Domain/Abstractions/IPaymentStore.cs`; move `IPaymentGateway` to `Domain/Abstractions/IPaymentGateway.cs`; rename namespaces.
  3. Move integration event payloads (`PaymentAuthorizedEvent`, `PaymentFailedEvent`, `PaymentCapturedEvent`, `PaymentRefundedEvent`, `PaymentVoidedEvent`, `OrderCreatedEvent`) into `Contracts/Integration/`; rename namespaces.
  4. Move `Infrastructure/Gateways/InMemoryPaymentGateway` to point at the new `Domain.Abstractions.IPaymentGateway`. Move `Observability/PaymentMetrics` → `Infrastructure/Observability/PaymentMetrics`; rename namespaces.
  5. Extract `IIntegrationMap<,>` + `DomainEventOutboxInterceptor` in `Infrastructure/Outbox/`. Register every existing domain-event-to-integration-event mapping as an `IIntegrationMap<,>` implementation temporarily co-located in `Infrastructure/Outbox/Mappers/`. Delete `PaymentContext.Translate(...)` and route `PaymentContext.ExecuteAsync` through the interceptor. Drop `AuthorizePaymentCommandHandler`'s manual `payment.DequeueDomainEvents()` workaround (and any other handler with the same pattern). Verify byte-identical outbox payloads on the existing test suite.
  6. Extract slices one at a time, each a green commit, in order:
     - read slices: `GetPaymentByOrder`, `GetPaymentById`
     - HTTP write slices: `CapturePayment`, `RefundPayment` (each takes ownership of the corresponding mapper from `Infrastructure/Outbox/Mappers/`)
     - saga command slices: `AuthorizePaymentCommand` (takes `PaymentAuthorizedIntegrationMap` + `PaymentFailedIntegrationMap`), `VoidPaymentCommand` (takes `PaymentVoidedIntegrationMap`), `CapturePaymentCommand`, `RefundPaymentCommand` (no mapper file — references the HTTP-slice mapper through DI per multi-producer rule)
     - integration event consumer slice: `OrderCreated`
  7. Move `InternalOutboxEndpoints` to `Infrastructure/Outbox/`. `Infrastructure/Outbox/Mappers/` should be empty after step 6 finishes.
  8. Reshape `Payment.Tests` to mirror `Features/<Slice>/`; keep `Payment.Tests/Domain/PaymentStateMachineTests` separate.
  9. Unskip NetArchTest rules; ship Roslyn `Payment.Service.LayoutAnalyzer` project + `.editorconfig` rules.
  10. Add CLAUDE.md "Payment service exception" entry composing ADR-0011 by reference.
- Single PR for review. Pre-commit hook gates every commit (no `--no-verify`, no deferral, no partial validation).

## Testing Decisions

### Test philosophy

- A good test verifies external behavior of a module through its public interface, not internal implementation details.
- Refactor must produce zero behavior change. Every existing `Payment.Tests` test continues to pass without modification beyond namespace updates required by the rename.
- New tests added only for the new seams (the outbox interceptor + integration maps) and for the architecture rules themselves.

### Modules to test

- **`Payment` aggregate (unchanged tests)** — existing `Payment.Tests/Models/PaymentStateMachineTests.cs` covers Pending → Authorized, Authorized → Captured, Captured → Refunded, Pending → Failed, Pending/Authorized → Voided, illegal-source-state throws, idempotent re-entry. Moves to `Payment.Tests/Domain/PaymentStateMachineTests.cs`, namespace only.
- **Per-slice handler / endpoint tests** — existing `Payment.Tests/Api/*` tests migrate into `Payment.Tests/Features/<Slice>/` without behavioral changes. Continue to use `PaymentWebApplicationFactory` and `IntegrationTestBase`. Specifically:
  - `PaymentEndpointsTests` → split across `Features/GetPaymentById/`, `Features/GetPaymentByOrder/`, `Features/CapturePayment/`, `Features/RefundPayment/` based on the route each test exercises.
  - `PaymentOwnershipTests` → split across the two read slices.
  - `AuthorizePaymentCommandHandlerTests` → `Features/AuthorizePaymentCommand/`.
  - `CapturePaymentCommandHandlerTests` → `Features/CapturePaymentCommand/`.
  - `RefundPaymentCommandHandlerTests` → `Features/RefundPaymentCommand/`.
  - `VoidPaymentCommandHandlerTests` → `Features/VoidPaymentCommand/`.
  - `InternalOutboxEndpointsTests` → stays under `Infrastructure/Outbox/` mirror in tests project.
  - `HealthEndpointTests`, `QaSeedPresenceTests`, `Authentication/*` → unchanged location (cross-cutting).
  - `IntegrationEvents/MessagingProviderBootTests` → stays under `IntegrationEvents/` or moves to `Infrastructure/Outbox/` mirror (decision deferred to implementation; about platform plumbing, not slices).
- **`DomainEventOutboxInterceptor`** — new unit tests covering: (a) tracked entity with N domain events of mapped types emits N outbox events with correct mapped payloads; (b) tracked entity with a domain event of an unmapped type throws `InvalidOperationException` with a descriptive message naming the unmapped type; (c) correlation metadata (`CorrelationId` / `CausationId` / `SagaId`) propagates from the inbound saga command onto the emitted integration event.
- **Per-slice `IIntegrationMap<TDomainEvent, TIntegrationEvent>` implementations** — small pure-function tests per map asserting the mapping preserves `PaymentId`, `OrderId`, `CustomerId`, `Amount`, `Currency`, `Reason` (where applicable). One test class per mapper. Five total: `PaymentAuthorizedIntegrationMapTests` (in `AuthorizePaymentCommand`), `PaymentFailedIntegrationMapTests` (in `AuthorizePaymentCommand`), `PaymentCapturedIntegrationMapTests` (in `CapturePayment`), `PaymentRefundedIntegrationMapTests` (in `RefundPayment`), `PaymentVoidedIntegrationMapTests` (in `VoidPaymentCommand`).
- **Multi-producer wiring tests** — one integration test per multi-producer event asserting both the HTTP slice and the saga slice produce byte-identical integration-event payloads through the shared mapper (covers the "mapper home" convention end-to-end): one for `PaymentCapturedEvent`, one for `PaymentRefundedEvent`.
- **`Payment.Tests/Architecture/LayoutTests.cs`** — new NetArchTest rules acting as executable specification of the boundary policy. Fails if any future contributor (human or AI) introduces a cross-boundary reference.
- **`EfPaymentStore` / `PaymentContext`** — covered indirectly by integration tests through `WebApplicationFactory<Program>`. No new tests unless impl changes beyond the rename and the `Translate` removal.

### Prior art in the codebase

- `Payment.Tests/IntegrationTestBase.cs` + `Payment.Tests/PaymentWebApplicationFactory.cs` — existing factory + base used by all current integration tests. Refactor preserves both at the root of the tests project.
- `Payment.Tests/Models/PaymentStateMachineTests.cs` — existing aggregate-level unit tests. `Given_When_Then` underscored display names preserved (`CA1707` suppressed via `Directory.Build.props`).
- `Order.Tests/Architecture/LayoutTests.cs` — closest prior-art layout-test file (same outbox-seam shape as the Payment pilot); copy structure and adapt namespaces.
- `Order.Service.LayoutAnalyzer` — closest prior-art Roslyn analyzer; copy structure and rename namespaces.
- `Order.Tests/Features/*` — closest prior-art reshape of feature tests (same outbox-seam shape); copy structure.
- `Inventory.Tests/Features/*` and `Shipping.Tests/Features/*` — useful prior art for saga-command-consumer slice tests despite their inline-event divergence.
- Pre-commit hook (`dotnet husky run --group pre-commit`) enforces `dotnet format --verify-no-changes` + `dotnet build --no-restore` + Basket tests on every commit. Payment tests run manually per root `CLAUDE.md` sandbox policy before pushing.

## Out of Scope

- Refactoring any other service (basket, product, auth, inventory, order, shipping, saga, api-gateway). Propagation to saga is a follow-up ADR.
- Modifying `ECommerce.Shared`. The pilot composes existing `AddSqlServerDatastore`, `AddOutbox`, `AddPlatformEventBus`, `AddEventHandler`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddPlatformOpenApi`, `AddJwtAuthentication`, `AddRequireServicePolicy`.
- Adding request validation (FluentValidation / DataAnnotations). Listed as follow-up in CLAUDE.md exception entry.
- Introducing MediatR or any mediator-style dispatcher.
- Splitting `Payment.Service.csproj` into multiple projects.
- Changing the `Payment` / `OrderCustomer` database schema. No new EF migrations.
- Changing integration event payload contracts. Only their location (folder + namespace) moves.
- Changing saga command payload contracts. Only their location (folder + namespace) moves where applicable.
- Changing the outbox table, dispatcher, or retry/DLQ behavior in `ECommerce.Shared.Infrastructure.Outbox`.
- Changing `PaymentApiEndpoints`'s public HTTP routes, response shapes, status codes, or auth requirements.
- Changing CI/CD pipelines, Docker Compose, Kubernetes manifests, or Azure pipelines.
- Performance optimization. The CQRS-lite read-path decision is structural, not performance-driven.
- Replacing `InMemoryPaymentGateway` with a real gateway integration. Listed as a separate follow-up unrelated to this pilot.
- Writing a new ADR. The CLAUDE.md "Payment service exception" entry composes ADR-0011 by reference.
- Writing a new "adding-a-new-slice" runbook. The existing runbook is reused unchanged.
- Promoting the `OrderCustomer` idempotency table into its own slice. Stays an internal store responsibility consumed by `Features/OrderCreated/` and `Features/AuthorizePaymentCommand/`.

## Further Notes

- Payment is the **seventh** pilot. Order / Product / Basket / Auth / Inventory / Shipping pilots are landed. After payment, only saga remains (per ADR-0011's candidate propagation order, saga last because its orchestrator shape stresses the layout differently than participant services).
- The payment refactor is conceptually **between** Order and Shipping/Inventory:
  - It **adopts** the `IIntegrationMap<,>` + `DomainEventOutboxInterceptor` seam from Order (Inventory/Shipping skipped it).
  - It **resolves a real smell** (`PaymentContext.Translate` switch + `AuthorizePaymentCommandHandler` manual `DequeueDomainEvents` workaround) that doesn't exist in Inventory/Shipping.
  - It **introduces** a multi-producer mapper convention (HTTP slice owns the mapper file; saga slices raise the same domain event and resolve through DI) that none of the prior six pilots needed.
- The multi-producer convention is the most interesting design choice and the load-bearing one to test. The "`CapturePaymentCommand` + `CapturePayment` HTTP produce byte-identical `PaymentCapturedEvent`" integration test pins the convention down so future contributors can't quietly drift toward two divergent mappers.
- The `AuthorizePaymentCommand` slice deserves extra reviewer attention: it both creates the `Payment` aggregate (when none exists for the order yet) and emits the saga reply. The order-customer-lookup race condition (return early when `TryGetOrderCustomer` is null, await redelivery) must be preserved in the slice handler — it's a subtle correctness property masked by the size of the handler today.
- The "duplicate first, extract on third" rule is load-bearing here too. The 4 saga command slices share mechanical shape (load payment → call domain method → persist → publish). Expect zero `Features/Shared/` extraction in the pilot itself; the slice-to-slice NetArchTest rule prevents accidental coupling.
- After the pilot lands and at least one review pass, the propagation ADR will propose saga as the final pilot. Saga is the most different of the remaining services (orchestrator, not participant), so its layout adoption is the truest test of the pattern's generality.
- Behavioral guidance from root `CLAUDE.md` applies: surgical changes only, no improving adjacent code, match existing style, push back on over-engineering. The pilot is large in line count but mechanical in intent — the only genuinely new thinking is the multi-producer mapper convention.
