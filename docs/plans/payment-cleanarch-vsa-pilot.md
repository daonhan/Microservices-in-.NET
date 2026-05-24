# Plan: Payment.Service Clean Architecture + Vertical Slice Pilot

> Source PRD: `docs/prd/PRD-Payment-CleanArch-VSA-Pilot.md` (tracking issue [#226](https://github.com/daonhan/Microservices-in-.NET/issues/226))
> Branch: `refactor/payment-vsa` (already checked out)

## Context

`Payment.Service` is organized by technical type: 4 HTTP routes inline in `Endpoints/PaymentApiEndpoints.cs` (2 reads + 2 writes), 5 event/command consumers in `IntegrationEvents/EventHandlers/`, 8 domain types in `Models/` (`Payment` aggregate, `PaymentStatus`, `OrderCustomer`, `Entity`, `IDomainEvent`, five `Payment*DomainEvent`s), `IPaymentStore` in `Infrastructure/Data/`, `IPaymentGateway` + `InMemoryPaymentGateway` in `Infrastructure/Gateways/`, `PaymentMetrics` in a top-level `Observability/` folder. `PaymentContext : DbContext, IPaymentStore` wears two hats (DbContext + store impl), and worse, holds a `Translate(IDomainEvent)` switch that mixes EF persistence with cross-service event translation inside the `DbContext`. Worse still, individual saga command handlers (notably `AuthorizePaymentCommandHandler`) work around that switch by manually calling `payment.DequeueDomainEvents()` to suppress double-publish — a fragile hybrid the next contributor can easily get wrong.

This pilot (#7, after Order / Product / Basket / Auth / Inventory / Shipping) applies the same Clean Architecture + VSA layout to `Payment.Service`. Zero functional behavior change. Boundaries enforced twice (NetArchTest + Roslyn analyzer). Intended outcome: each feature owns one `Features/<Slice>/` folder; Domain has zero Infrastructure / Contracts references; `IPaymentStore` + `IPaymentGateway` live in Domain; `EfPaymentStore` lives in Infrastructure; metrics fold under `Infrastructure/Observability/`; saga commands continue to flow through the shared lib; `PaymentContext.Translate` is gone and replaced by per-slice `IIntegrationMap<,>` resolved by a generic `DomainEventOutboxInterceptor`; the saga-handler manual-drain workaround is gone.

## Architectural decisions

Durable decisions that apply across all phases:

- **Project shape**: single `Payment.Service.csproj` retained; boundaries enforced by namespace + Roslyn analyzer + NetArchTest, not by csproj split.
- **Folder topology**:
  - `Features/<Slice>/` — one folder per inbound trigger (HTTP route OR integration message). Self-contained: handler, endpoint or event/command consumer, DTOs, slice DI extension, slice-local `IIntegrationMap<,>` for first-producer slices.
  - `Domain/` — `Payment` aggregate + `PaymentStatus` + `OrderCustomer` + `Entity` + `IDomainEvent` + 5 `Payment*DomainEvent`s + `Abstractions/IPaymentStore.cs` + `Abstractions/IPaymentGateway.cs`. Zero references to Infrastructure / Features / Contracts.
  - `Contracts/Integration/` — cross-service event payload classes (5 outbound `Payment*Event`s + inbound `OrderCreatedEvent`). Saga commands (`AuthorizePaymentCommand`, `CapturePaymentCommand`, `VoidPaymentCommand`, `RefundPaymentCommand`) stay in `ECommerce.Shared.IntegrationEvents.Commands` (consumed, not owned).
  - `Infrastructure/Data/EntityFramework/` — pure `PaymentContext` (DbContext only) + new `EfPaymentStore` + EF configs + `PaymentContextDesignTimeFactory` + `PaymentContextSeed`.
  - `Infrastructure/Gateways/` — `InMemoryPaymentGateway` (impl of `Domain.Abstractions.IPaymentGateway`).
  - `Infrastructure/Observability/` — `PaymentMetrics`.
  - `Infrastructure/Outbox/` — `DomainEventOutboxInterceptor`, `IIntegrationMap<,>` abstraction, `InternalOutboxEndpoints`.
- **Namespaces**: `Payment.Service.Domain`, `Payment.Service.Domain.Abstractions`, `Payment.Service.Domain.Events`, `Payment.Service.Features.<Slice>`, `Payment.Service.Contracts.Integration`, `Payment.Service.Infrastructure.Data.EntityFramework`, `Payment.Service.Infrastructure.Gateways`, `Payment.Service.Infrastructure.Observability`, `Payment.Service.Infrastructure.Outbox`. The `Payment.Service.Models`, `Payment.Service.Observability`, `Payment.Service.Endpoints`, `Payment.Service.IntegrationEvents` namespaces are retired.
- **HTTP routes**: unchanged — `GET /by-order/{orderId:guid}` (auth, ownership), `GET /{paymentId:guid}` (auth, ownership), `POST /{paymentId:guid}/capture` (`Administrator`), `POST /{paymentId:guid}/refund` (`Administrator`, optional body `{ amount }`), `GET /health`, `GET /internal/outbox/failed`. Same verbs, paths, auth requirements, response shapes.
- **Schema**: unchanged. No new EF migrations. `Payment`, `OrderCustomer` tables preserved.
- **Event payloads**: unchanged shape — `PaymentAuthorizedEvent`, `PaymentFailedEvent`, `PaymentCapturedEvent`, `PaymentRefundedEvent`, `PaymentVoidedEvent`. Only folder + namespace moves.
- **Dispatch**: no MediatR. Endpoints / event consumers take handler via constructor injection, call `HandleAsync(...)` directly. Handlers `internal sealed`, one public async method.
- **Slice DI**: each slice exposes `AddXxxSlice(this IServiceCollection)`; event/command-consumer slices internally call `AddEventHandler<TEvent, THandler>()` from `ECommerce.Shared.Infrastructure.EventBus`.
- **Write path**: load via `IPaymentStore` → call aggregate domain method (`Authorize` / `Fail` / `Capture` / `Refund` / `Void`) → persist via `IOutboxUnitOfWork.ExecuteAsync` → `DomainEventOutboxInterceptor` translates each `IDomainEvent` to the matching integration event via the DI-resolved `IIntegrationMap<,>` and writes it to the outbox. Correlation metadata (`CorrelationId` / `CausationId` / `SagaId`) carried from the inbound saga command onto the outbox event.
- **Read path**: project directly from `PaymentContext` to `PaymentResponse` (bypass `IPaymentStore` and aggregate).
- **Outbox translation seam**: `IIntegrationMap<TDomainEvent, TIntegrationEvent>` + generic `DomainEventOutboxInterceptor` replace `PaymentContext.Translate(...)` switch. Unmapped domain-event type fails fast with descriptive `InvalidOperationException` mirroring current message wording. Each first-producer slice ships one `IIntegrationMap<,>` co-located.
- **Multi-producer mapper home (Payment-specific, new convention)**:
  - Single-producer maps live in the producing slice:
    - `PaymentAuthorizedIntegrationMap` → `Features/AuthorizePaymentCommand/`
    - `PaymentFailedIntegrationMap` → `Features/AuthorizePaymentCommand/`
    - `PaymentVoidedIntegrationMap` → `Features/VoidPaymentCommand/`
  - Multi-producer maps live in the HTTP slice (HTTP-first rule):
    - `PaymentCapturedIntegrationMap` → `Features/CapturePayment/` (also raised by `Features/CapturePaymentCommand/`)
    - `PaymentRefundedIntegrationMap` → `Features/RefundPayment/` (also raised by `Features/RefundPaymentCommand/`)
  - Registration done in the owning slice's `AddXxxSlice` only. Other producing slice does **not** duplicate registration. Interceptor resolves globally through DI. This is **not** a slice-to-slice source reference — the coupling flows through DI, satisfying the cross-slice NetArchTest rule.
- **Cross-slice rule**: duplicate first, extract on third. NetArchTest forbids `Features.<X>` ↔ `Features.<Y>`. HTTP `CapturePayment` and saga `CapturePaymentCommand` are deliberate duplicates at the handler level; same for `RefundPayment` vs `RefundPaymentCommand`.
- **Divergences from prior pilots** to honor:
  1. **Adopts** `IIntegrationMap<,>` + `DomainEventOutboxInterceptor` from Order (Inventory/Shipping skipped it). Justified because `PaymentContext.Translate` is a real smell with a real workaround (`AuthorizePaymentCommandHandler` manual `DequeueDomainEvents()`) to dissolve.
  2. `IPaymentStore` split from `DbContext` (matches Order / Inventory / Shipping shape).
  3. `IPaymentGateway` lifted to `Domain/Abstractions/` (matches Shipping `ICarrierGateway` shape); `InMemoryPaymentGateway` impl stays in `Infrastructure/Gateways/`.
  4. `Observability/PaymentMetrics` moves to `Infrastructure/Observability/` so the top-level folder count matches prior pilots exactly (Inventory, Shipping).
  5. HTTP `CapturePayment` and saga `CapturePaymentCommand` are two distinct slices (same for refund), mirroring Shipping's `CancelShipment` vs `CancelShipmentCommand` convention.
  6. **New convention** (no prior pilot needed it): multi-producer mapper home. HTTP slice owns the mapper file; saga slice raises the same domain event; interceptor resolves through DI.
  7. Saga commands consumed from `ECommerce.Shared.IntegrationEvents.Commands`, not owned in local `Contracts/Integration/`.
- **Composition**: composes ADR [0011](../adr/0011-order-cleanarch-vsa-pilot.md) by reference (no new ADR). Reuses [adding-a-new-slice.md](../runbooks/adding-a-new-slice.md) runbook unchanged. Root `CLAUDE.md` gets one new "Payment service exception" paragraph.
- **`GET /health`**: stays in `Program.cs` (one-line `MapHealthChecks`). No `Features/Health/` slice — matches Inventory/Auth/Shipping, avoids precedent that ops endpoints become slices.
- **Rollout**: 15 staged commits on `refactor/payment-vsa`, each green. Single PR for review. Pre-commit hook gates every commit (no `--no-verify`, no validation deferral).
- **Critical files to modify**:
  - `payment-microservice/Payment.Service/Endpoints/PaymentApiEndpoints.cs` (~170 LOC, dissolved by Phase 7d)
  - `payment-microservice/Payment.Service/Endpoints/InternalOutboxEndpoints.cs` (relocated Phase 8)
  - `payment-microservice/Payment.Service/Infrastructure/Data/EntityFramework/PaymentContext.cs` (two-hat split Phase 4; `Translate(...)` deleted Phase 6)
  - `payment-microservice/Payment.Service/Infrastructure/Data/IPaymentStore.cs` (relocated Phase 3)
  - `payment-microservice/Payment.Service/Infrastructure/Gateways/IPaymentGateway.cs` (relocated Phase 3)
  - `payment-microservice/Payment.Service/Models/*` (8 types relocated Phase 2b)
  - `payment-microservice/Payment.Service/IntegrationEvents/Events/*Event.cs` (6 payloads relocated Phase 2a)
  - `payment-microservice/Payment.Service/IntegrationEvents/EventHandlers/*` (5 handlers dissolved Phases 7c + 7d; `AuthorizePaymentCommandHandler` loses its manual-drain workaround Phase 6)
  - `payment-microservice/Payment.Service/Observability/PaymentMetrics.cs` (relocated Phase 5)
  - `payment-microservice/Payment.Service/Program.cs` (becomes slice manifest by Phase 8)
  - `payment-microservice/Payment.Tests/Api/*` (relocated Phase 9)
  - `payment-microservice/Payment.Tests/Models/PaymentStateMachineTests.cs` (relocated to `Domain/` Phase 9)
- **Critical files to copy/mirror** (prior pilots, do not modify):
  - `order-microservice/Order.Tests/Architecture/LayoutTests.cs` — closest prior-art NetArchTest layout (same outbox-seam shape)
  - `order-microservice/Order.Tests/Architecture/LayoutAnalyzerTests.cs` — analyzer test shape
  - `order-microservice/Order.Service.LayoutAnalyzer/LayoutAnalyzer.cs` — analyzer skeleton + diagnostic IDs (rename `ORDLAY***` → `PAYLAY***`)
  - `order-microservice/Order.Service/Infrastructure/Outbox/DomainEventOutboxInterceptor.cs` + `IIntegrationMap.cs` — closest prior art for the interceptor seam
  - `order-microservice/Order.Service/Features/CreateOrder/OrderCreatedIntegrationMap.cs` (or analog) — slice-local mapper shape
  - `order-microservice/Order.Service/Features/<Slice>/<Slice>SliceExtensions.cs` — slice DI extension shape
  - `order-microservice/Order.Service/Program.cs` — slice-manifest shape

---

## Phase 1: Scaffold NetArchTest + LayoutAnalyzer (rules off)

**User stories**: 16, 17 (boundary enforcement guardrails).

### What to build

Add new `Payment.Service.LayoutAnalyzer` csproj (copy Order analyzer skeleton, rename diagnostic IDs `ORDLAY***` → `PAYLAY***`, rules empty / disabled). Wire as `Analyzer` ProjectReference from `Payment.Service.csproj`. Add `Payment.Tests/Architecture/LayoutTests.cs` + `Payment.Tests/Architecture/LayoutAnalyzerTests.cs` with every test marked `[Fact(Skip="enabled in Phase 10")]`. No production code changes.

### Acceptance criteria

- [ ] `dotnet build payment-microservice` green
- [ ] `dotnet test payment-microservice/Payment.Tests` green (skipped tests count > 0)
- [ ] `dotnet format --verify-no-changes` green
- [ ] Commit: `refactor(payment): Phase 1 scaffold NetArchTest + LayoutAnalyzer`

---

## Phase 2a: Move integration-event payloads to `Contracts/Integration/`

**User stories**: 20 (namespace match folders), 26 (shared lib untouched).

### What to build

Move the 6 payload classes (`PaymentAuthorizedEvent`, `PaymentFailedEvent`, `PaymentCapturedEvent`, `PaymentRefundedEvent`, `PaymentVoidedEvent`, consumed `OrderCreatedEvent`) from `IntegrationEvents/Events/` to `Contracts/Integration/`. Rename namespace to `Payment.Service.Contracts.Integration`. Leave the 5 `EventHandlers/*Handler.cs` files in `IntegrationEvents/EventHandlers/` for now (Phases 7c + 7d dissolve them); fix their `using`s. Fix all other `using`s across `Endpoints/`, `Models/`, `Infrastructure/`, tests.

### Acceptance criteria

- [ ] Build green
- [ ] Full `dotnet test payment-microservice/Payment.Tests` green
- [ ] `dotnet format --verify-no-changes` green
- [ ] `IntegrationEvents/Events/` folder deleted
- [ ] Commit: `refactor(payment): Phase 2a move integration event payloads to Contracts/`

---

## Phase 2b: Move domain to `Domain/`

**User stories**: 5 (rich domain), 20 (namespaces).

### What to build

Move all 8 `Models/*` types to `Domain/` with namespace `Payment.Service.Domain`:

- `Payment` aggregate + `PaymentStatus` enum + `OrderCustomer` idempotency record → `Domain/`
- `Entity` base + `IDomainEvent` marker → `Domain/`
- The 5 `Payment*DomainEvent` records (`PaymentAuthorizedDomainEvent`, `PaymentFailedDomainEvent`, `PaymentCapturedDomainEvent`, `PaymentRefundedDomainEvent`, `PaymentVoidedDomainEvent`) → `Domain/Events/` under namespace `Payment.Service.Domain.Events`

No business-logic refactor — pure relocation + namespace rename. Update all consumer `using`s (`Endpoints/`, `Infrastructure/`, `IntegrationEvents/EventHandlers/`, `Payment.Tests/`).

### Acceptance criteria

- [ ] Build green
- [ ] Full `dotnet test payment-microservice/Payment.Tests` green
- [ ] `Models/` folder deleted
- [ ] Commit: `refactor(payment): Phase 2b move domain to Domain/`

---

## Phase 3: Move `IPaymentStore` + `IPaymentGateway` to `Domain/Abstractions/`

**User stories**: 11 (PaymentContext single-purpose), 15 (Domain abstractions), 20 (namespaces).

### What to build

Move `Infrastructure/Data/IPaymentStore.cs` to `Domain/Abstractions/IPaymentStore.cs` under namespace `Payment.Service.Domain.Abstractions`. Move `Infrastructure/Gateways/IPaymentGateway.cs` to `Domain/Abstractions/IPaymentGateway.cs` under the same namespace. Co-locate any companion result records / DTOs that travel with each abstraction (e.g. `AuthorizeAsync` result type) in the same file or under `Domain/Abstractions/`. `PaymentContext` still implements `IPaymentStore` — Phase 4 splits. `InMemoryPaymentGateway` stays in `Infrastructure/Gateways/`, now referencing the new namespace. Update all consumer `using`s.

### Acceptance criteria

- [ ] Build green
- [ ] Full `Payment.Tests` green
- [ ] `Infrastructure/Data/IPaymentStore.cs` deleted (now under `Domain/Abstractions/`)
- [ ] `Infrastructure/Gateways/IPaymentGateway.cs` deleted (now under `Domain/Abstractions/`)
- [ ] `InMemoryPaymentGateway.cs` still in `Infrastructure/Gateways/`, references `Payment.Service.Domain.Abstractions`
- [ ] Commit: `refactor(payment): Phase 3 IPaymentStore + IPaymentGateway to Domain/Abstractions/`

---

## Phase 4: Split `EfPaymentStore` from `PaymentContext`

**User stories**: 11 (DbContext single-purpose).

### What to build

Largest mechanical phase before Phase 6 — touches every store method. Strict in-commit migration order to keep build green between sub-steps:

1. Introduce `Infrastructure/Data/EntityFramework/EfPaymentStore.cs`. Constructor takes `(PaymentContext ctx, IOutboxUnitOfWork outboxUnitOfWork)` (current `PaymentContext` runtime constructor signature). Every `IPaymentStore` method (`Add`, `GetById`, `GetByOrder`, `SaveChangesAsync`, `ExecuteAsync`, `RecordOrderCustomer`, `TryGetOrderCustomer`) either delegates to the still-present `PaymentContext` method or re-implements using `ctx.Payments` / `ctx.OrderCustomers`. Two implementations coexist; build green.
2. Flip DI registration in `EntityFrameworkExtensions.AddSqlServerDatastore` (or local extension): `services.AddScoped<IPaymentStore, EfPaymentStore>();` replacing the prior `IPaymentStore` → `PaymentContext` resolution. Run full `Payment.Tests` locally.
3. Delete the now-orphaned `IPaymentStore` method bodies from `PaymentContext`; remove `, IPaymentStore` from class declaration; reduce class to `DbContext` base + `DbSet<Payment>` + `DbSet<OrderCustomer>` + `OnModelCreating` + the existing `ExecuteAsync(Func<Task>)` helper (still present at this phase — Phase 6 reroutes through the interceptor).

Single commit for all three sub-steps — splitting across commits would land a misleading "two impls coexist" or "DI flipped but methods still on context" state on bisect.

### Acceptance criteria

- [ ] Build green after each sub-step
- [ ] Full `dotnet test payment-microservice/Payment.Tests` green at end (manual — hook only runs Basket tests)
- [ ] `PaymentContext.cs` LOC drops; no `IPaymentStore` interface in declaration
- [ ] `PaymentContext` retains `Translate(...)` switch + `ExecuteAsync(Func<Task>)` — both removed/rerouted in Phase 6
- [ ] Commit: `refactor(payment): Phase 4 split EfPaymentStore from PaymentContext`

---

## Phase 5: Relocate `PaymentMetrics` to `Infrastructure/Observability/`

**User stories**: 18 (PaymentMetrics in Infrastructure/Observability/).

### What to build

Move `Observability/PaymentMetrics.cs` → `Infrastructure/Observability/PaymentMetrics.cs` with namespace `Payment.Service.Infrastructure.Observability`. Delete the empty top-level `Observability/` folder. Update all consumer `using`s: `Program.cs`, `Endpoints/PaymentApiEndpoints.cs`, `IntegrationEvents/EventHandlers/*`. Metric names / labels / counters (`payments_total`, authorize-latency histogram) preserved verbatim.

### Acceptance criteria

- [ ] Build green
- [ ] Full `Payment.Tests` green
- [ ] `Observability/` (top-level) folder deleted
- [ ] Prometheus exporter still emits identical counter and histogram names
- [ ] Commit: `refactor(payment): Phase 5 relocate PaymentMetrics to Infrastructure/Observability/`

---

## Phase 6: Extract `IIntegrationMap<,>` + `DomainEventOutboxInterceptor`; delete `PaymentContext.Translate`; drop manual `DequeueDomainEvents` workaround

**User stories**: 9 (manual-drain workaround removed), 11 (PaymentContext single-purpose), 12 (per-event mappers via DI).

### What to build

**Largest behavior-touching phase.** Strict in-commit migration order; single commit:

1. Introduce `Infrastructure/Outbox/IIntegrationMap.cs` declaring `IIntegrationMap<TDomainEvent, TIntegrationEvent>` with one `Map(TDomainEvent)` method returning the integration event. Namespace `Payment.Service.Infrastructure.Outbox`.
2. Introduce `Infrastructure/Outbox/DomainEventOutboxInterceptor.cs`. Resolves the right `IIntegrationMap<TDomain, TIntegration>` by domain-event runtime type via DI (`IServiceProvider.GetService(typeof(IIntegrationMap<,>).MakeGenericType(...))` or equivalent). For each domain event, calls the mapper and writes the integration event to `IOutboxStore.AddOutboxEvent(...)`. Unmapped domain-event type throws `InvalidOperationException` with the exact wording currently in `PaymentContext.Translate`: `$"No integration-event translation registered for domain event {domainEvent.GetType().Name}"`.
3. Author the 5 `IIntegrationMap<,>` implementations **temporarily co-located** in `Infrastructure/Outbox/Mappers/` (Phases 7b/7c will relocate them into their owning slice's folder):
   - `PaymentAuthorizedIntegrationMap : IIntegrationMap<PaymentAuthorizedDomainEvent, PaymentAuthorizedEvent>`
   - `PaymentFailedIntegrationMap : IIntegrationMap<PaymentFailedDomainEvent, PaymentFailedEvent>`
   - `PaymentCapturedIntegrationMap : IIntegrationMap<PaymentCapturedDomainEvent, PaymentCapturedEvent>`
   - `PaymentRefundedIntegrationMap : IIntegrationMap<PaymentRefundedDomainEvent, PaymentRefundedEvent>`
   - `PaymentVoidedIntegrationMap : IIntegrationMap<PaymentVoidedDomainEvent, PaymentVoidedEvent>`
   Each map's logic copy-pasted byte-identical from the current `PaymentContext.Translate` switch arm.
4. Register all 5 maps + the interceptor in `Program.cs` (will move to slice extensions Phases 7b/7c).
5. Reroute `EfPaymentStore.ExecuteAsync(...)` (or `PaymentContext.ExecuteAsync(...)` if Phase 4 left the helper there) to feed the interceptor instead of calling `Translate`. The captured `domainEvents.Select(Translate).ToList()` becomes `domainEvents.Select(_interceptor.Translate).ToList()` (or the interceptor accepts the list directly and writes to the outbox — concrete shape mirrors Order's `DomainEventOutboxInterceptor` integration).
6. Delete `PaymentContext.Translate(...)` static method.
7. **Remove the manual-drain workaround** from `AuthorizePaymentCommandHandler`: drop the `payment.DequeueDomainEvents();` line and the hand-crafted `reply = new PaymentAuthorizedEvent(...) { CorrelationId = ..., CausationId = ..., SagaId = ... }` / `reply = new PaymentFailedEvent(...)` construction. Replace with: call `payment.Authorize(...)` or `payment.Fail(...)`, let the interceptor publish through the outbox. Correlation metadata propagation (`CorrelationId` / `CausationId` / `SagaId` from inbound `AuthorizePaymentCommand` onto the outbox `PaymentAuthorizedEvent` / `PaymentFailedEvent`) must remain byte-identical. Mechanism: the interceptor reads correlation metadata from an ambient (`IServiceProvider`-scoped) carrier set by the saga-command consumer wrapper, OR `IIntegrationMap<,>` implementations receive a correlation context as a second parameter — concrete shape mirrors Order pilot's solution. Acceptance criterion is byte-identical event payload + headers, not the implementation route.
8. Audit `CapturePaymentCommandHandler`, `VoidPaymentCommandHandler`, `RefundPaymentCommandHandler` for the same manual-drain pattern — if present, remove identically. (HTTP `CapturePayment` / `RefundPayment` already use `ExecuteAsync` + `Translate`, so they get the interceptor automatically with no handler change.)

### Acceptance criteria

- [ ] Build green
- [ ] **Full `Payment.Tests` green — including all saga handler tests** (`AuthorizePaymentCommandHandlerTests`, `CapturePaymentCommandHandlerTests`, `VoidPaymentCommandHandlerTests`, `RefundPaymentCommandHandlerTests`) and HTTP endpoint tests (`PaymentEndpointsTests` capture + refund cases). Outbox events produced are byte-identical (payload + correlation metadata) to pre-refactor.
- [ ] `PaymentContext.Translate(...)` deleted
- [ ] `AuthorizePaymentCommandHandler` no longer calls `payment.DequeueDomainEvents()`
- [ ] No remaining `payment.DequeueDomainEvents()` call anywhere in `Payment.Service/`
- [ ] `Infrastructure/Outbox/Mappers/` contains 5 `IIntegrationMap<,>` implementations
- [ ] `Infrastructure/Outbox/DomainEventOutboxInterceptor.cs` + `IIntegrationMap.cs` exist
- [ ] Unmapped domain-event runtime type still throws `InvalidOperationException` with descriptive wording (test pinning this)
- [ ] Commit: `refactor(payment): Phase 6 extract outbox interceptor seam + drop manual drain`

---

## Phase 7a: Extract read slices

**User stories**: 3 (one folder per HTTP route), 6 (read slices project directly from EF).

### What to build

Carve `Features/GetPaymentById/` and `Features/GetPaymentByOrder/`. Each owns: endpoint class (returns `TypedResults.*`), `internal sealed` handler with one public async method that projects directly from `PaymentContext` to `PaymentResponse` (bypasses `IPaymentStore` and aggregate), response DTOs (reuse current `PaymentApiEndpoints.PaymentResponse` record — duplicate the record per slice rather than introducing `Features/Shared/`, honoring duplicate-first), `AddXxxSlice(this IServiceCollection)` extension. Wire each into `Program.cs`. Remove the corresponding 2 GET lambdas + their shared private helpers (`IsAuthorized`, `ToResponse`) from `Endpoints/PaymentApiEndpoints.cs` — duplicate the helpers per slice (rule-of-three; the third reuse triggers extraction). Preserve ownership/auth checks (`Administrator` role bypass + `customerId` claim match) byte-for-byte.

### Acceptance criteria

- [ ] Build green
- [ ] `PaymentEndpointsTests` read-side cases green, `PaymentOwnershipTests` green
- [ ] Full `Payment.Tests` green
- [ ] `Features/GetPaymentById/`, `Features/GetPaymentByOrder/` each contain endpoint + handler + DTO + slice extension
- [ ] Commit: `refactor(payment): Phase 7a extract read slices`

---

## Phase 7b: Extract HTTP write slices (`CapturePayment`, `RefundPayment`) + relocate multi-producer maps

**User stories**: 7 (write path through IPaymentStore + outbox UoW), 13 (slice owns its mapper), 14 (multi-producer mapper home in HTTP slice), 21 (HTTP slice distinct from saga slice).

### What to build

Carve `Features/CapturePayment/` and `Features/RefundPayment/`. Each owns: endpoint, handler, request DTO if present (`RefundPaymentRequest` for refund — duplicate into the slice), `AddXxxSlice` extension. Each write handler: load via `IPaymentStore.GetById` → call `IPaymentGateway.CaptureAsync` / `RefundAsync` (preserves current order: gateway side-effect **before** `ExecuteAsync` wrap, matching existing handler shape) → wrap aggregate mutation (`payment.Capture(...)` / `payment.Refund(...)`) in `outboxUnitOfWork.ExecuteAsync(() => { ... })` (or whatever path Phase 6 finalized — interceptor publishes the integration event automatically). Wire into `Program.cs`. Remove the corresponding 2 POST lambdas from `Endpoints/PaymentApiEndpoints.cs`. Preserve auth (`Administrator` policy) and state-conflict 409 response shape verbatim.

**Multi-producer map relocation**:
- Move `Infrastructure/Outbox/Mappers/PaymentCapturedIntegrationMap.cs` → `Features/CapturePayment/PaymentCapturedIntegrationMap.cs` (namespace `Payment.Service.Features.CapturePayment`). Registration moves into `AddCapturePaymentSlice(...)`.
- Move `Infrastructure/Outbox/Mappers/PaymentRefundedIntegrationMap.cs` → `Features/RefundPayment/PaymentRefundedIntegrationMap.cs` (namespace `Payment.Service.Features.RefundPayment`). Registration moves into `AddRefundPaymentSlice(...)`.

Remove the two map registrations from `Program.cs` (now done by slice extensions).

### Acceptance criteria

- [ ] Build green
- [ ] `PaymentEndpointsTests` capture + refund cases green
- [ ] Outbox emits `PaymentCapturedEvent` / `PaymentRefundedEvent` byte-identical to pre-refactor (interceptor resolves the slice-local map)
- [ ] Full `Payment.Tests` green
- [ ] `Features/CapturePayment/` contains endpoint + handler + slice extension + `PaymentCapturedIntegrationMap.cs`
- [ ] `Features/RefundPayment/` contains endpoint + handler + DTO + slice extension + `PaymentRefundedIntegrationMap.cs`
- [ ] Commit: `refactor(payment): Phase 7b extract Capture/Refund HTTP write slices`

---

## Phase 7c: Extract saga command-consumer slices (`AuthorizePaymentCommand`, `CapturePaymentCommand`, `VoidPaymentCommand`, `RefundPaymentCommand`) + relocate single-producer maps

**User stories**: 8 (saga handler follows write-slice rule, no manual drain), 9 (manual drain removed — verified in this phase too), 14 (multi-producer slices reference HTTP slice's map via DI), 21 (saga slice distinct from HTTP slice).

### What to build

Carve `Features/AuthorizePaymentCommand/`, `Features/CapturePaymentCommand/`, `Features/VoidPaymentCommand/`, `Features/RefundPaymentCommand/`. Each owns: event-handler class (implements `IEventHandler<TCommand>`), `internal sealed` slice handler with the business logic, `AddXxxSlice` extension that calls `AddEventHandler<TCommand, THandler>()`.

**Single-producer map relocation** (each map moves into its owning slice):
- Move `Infrastructure/Outbox/Mappers/PaymentAuthorizedIntegrationMap.cs` → `Features/AuthorizePaymentCommand/PaymentAuthorizedIntegrationMap.cs` (namespace `Payment.Service.Features.AuthorizePaymentCommand`). Registration in `AddAuthorizePaymentCommandSlice(...)`.
- Move `Infrastructure/Outbox/Mappers/PaymentFailedIntegrationMap.cs` → `Features/AuthorizePaymentCommand/PaymentFailedIntegrationMap.cs` (same namespace as above — two maps in one slice is fine; both produced from this slice). Registration in the same slice extension.
- Move `Infrastructure/Outbox/Mappers/PaymentVoidedIntegrationMap.cs` → `Features/VoidPaymentCommand/PaymentVoidedIntegrationMap.cs` (namespace `Payment.Service.Features.VoidPaymentCommand`). Registration in `AddVoidPaymentCommandSlice(...)`.

**Multi-producer slices do not duplicate maps**:
- `Features/CapturePaymentCommand/` does **not** ship `PaymentCapturedIntegrationMap.cs`. It raises `PaymentCapturedDomainEvent` (via `payment.Capture(...)`); the interceptor resolves the map from the HTTP-slice registration done in Phase 7b's `AddCapturePaymentSlice(...)`.
- `Features/RefundPaymentCommand/` does **not** ship `PaymentRefundedIntegrationMap.cs`. Analogous setup; mapper resolved from `AddRefundPaymentSlice(...)` registration.

**Each slice handler**:
- Load existing payment via `IPaymentStore.GetByOrder` (Authorize) or `IPaymentStore.GetById` (Capture/Void/Refund).
- Authorize handler preserves the idempotency-by-existing-payment short-circuit (returns existing reply if payment already exists) AND the order-customer-lookup race condition handling (return early without throwing when `TryGetOrderCustomer` returns null — await redelivery). These two behaviors **must remain byte-for-byte identical**.
- Call `IPaymentGateway` if applicable (`AuthorizeAsync` for Authorize; Capture/Void/Refund: check whether gateway is invoked from saga path today and preserve identical order).
- Wrap aggregate mutation (`payment.Authorize` / `Capture` / `Void` / `Refund` / `Fail`) in `outboxUnitOfWork.ExecuteAsync` so the interceptor publishes through the outbox. No manual `DequeueDomainEvents()` — already removed Phase 6.
- Correlation metadata (`CorrelationId` / `CausationId` / `SagaId` from inbound command onto outbox event) propagates per the Phase 6 mechanism.

Wire all 4 slice extensions into `Program.cs`. Remove the corresponding 4 `AddEventHandler<...>()` calls from `Program.cs` (now done by slice extensions).

### Acceptance criteria

- [ ] Build green
- [ ] `AuthorizePaymentCommandHandlerTests` green — idempotency-by-existing-payment + order-customer-lookup race both pinned
- [ ] `CapturePaymentCommandHandlerTests`, `VoidPaymentCommandHandlerTests`, `RefundPaymentCommandHandlerTests` green
- [ ] Outbox emits saga replies byte-identical (payload + `CorrelationId`/`CausationId`/`SagaId`) to pre-refactor
- [ ] Multi-producer wiring verified by integration test asserting HTTP `CapturePayment` and saga `CapturePaymentCommand` produce byte-identical `PaymentCapturedEvent` (and same for refund). Add this test if absent.
- [ ] Full `Payment.Tests` green
- [ ] `Features/AuthorizePaymentCommand/` contains handler + slice extension + 2 maps
- [ ] `Features/VoidPaymentCommand/` contains handler + slice extension + 1 map
- [ ] `Features/CapturePaymentCommand/`, `Features/RefundPaymentCommand/` each contain handler + slice extension only (maps resolved from HTTP slices via DI)
- [ ] `Infrastructure/Outbox/Mappers/` now empty (or deleted) — all maps relocated to slices
- [ ] Commit: `refactor(payment): Phase 7c extract saga command slices + relocate maps`

---

## Phase 7d: Extract `OrderCreated` event-consumer slice + retire `PaymentApiEndpoints.cs` + `IntegrationEvents/EventHandlers/`

**User stories**: 4 (event-driven features feel identical to HTTP), 10 (OrderCreated owns its idempotency-cache record).

### What to build

Carve `Features/OrderCreated/`. Owns: event-handler class (implements `IEventHandler<OrderCreatedEvent>`), `internal sealed` slice handler that calls `IPaymentStore.RecordOrderCustomer(orderId, customerId)` (preserves byte-for-byte the current `OrderCreatedEventHandler.Handle` behavior — idempotency-by-`OrderCustomers.AnyAsync` check is inside the store, not the slice, so it stays put), `AddOrderCreatedSlice` extension that calls `AddEventHandler<OrderCreatedEvent, OrderCreatedSliceHandler>()`. Wire into `Program.cs`.

Delete `IntegrationEvents/EventHandlers/OrderCreatedEventHandler.cs`. By this phase, all 5 handlers in `IntegrationEvents/EventHandlers/` are gone (4 saga handlers dissolved Phase 7c). Delete the empty `IntegrationEvents/EventHandlers/` folder. Delete the empty `IntegrationEvents/` folder (payloads moved Phase 2a, handlers now gone).

Delete `Endpoints/PaymentApiEndpoints.cs` — all 4 routes now live in slices. `Endpoints/` folder contains only `InternalOutboxEndpoints.cs` (Phase 8 relocates it).

### Acceptance criteria

- [ ] Build green
- [ ] `Features/OrderCreated/` contains handler + slice extension
- [ ] `Endpoints/PaymentApiEndpoints.cs` deleted
- [ ] `IntegrationEvents/EventHandlers/` folder deleted
- [ ] `IntegrationEvents/` folder deleted
- [ ] Full `Payment.Tests` green including `MessagingProviderBootTests` (provider switch still boots) and any OrderCreated-related test
- [ ] Commit: `refactor(payment): Phase 7d extract OrderCreated slice + retire PaymentApiEndpoints`

---

## Phase 8: Relocate `InternalOutboxEndpoints` + `Program.cs` becomes slice manifest

**User stories**: 23 (ops plumbing out of feature manifest), 32 (DLQ poller call still works).

### What to build

Move `Endpoints/InternalOutboxEndpoints.cs` to `Infrastructure/Outbox/InternalOutboxEndpoints.cs` with namespace `Payment.Service.Infrastructure.Outbox`. Delete the now-empty `Endpoints/` folder. Reshape `Program.cs` into a slice manifest: chained `AddXxxSlice()` registration block (9 slices) + `app.MapXxxSlice()` mapping block + `app.RegisterInternalOutboxEndpoints()` (or equivalent) + `MapHealthChecks` + shared-lib infra (`AddSqlServerDatastore`, `AddOutbox`, `AddPlatformEventBus`, etc.) retained as-is + `DomainEventOutboxInterceptor` registration (or it moves into a generic `AddOutboxInterception` shared-lib helper — defer to extraction). `RequireService` policy gate preserved on `/internal/outbox/failed`.

### Acceptance criteria

- [ ] Build green
- [ ] `InternalOutboxEndpointsTests` green (DLQ poller route still gated by `RequireService`)
- [ ] `Endpoints/` folder deleted
- [ ] `Program.cs` reads as a manifest (slice registrations + mappings + ops endpoints + shared-lib infra)
- [ ] `Program.cs` contains zero per-handler `AddScoped<...Handler>` or per-event `AddEventHandler<...>` calls — all in slice extensions
- [ ] Full `Payment.Tests` green
- [ ] Commit: `refactor(payment): Phase 8 relocate InternalOutboxEndpoints + Program.cs manifest`

---

## Phase 9: Reshape `Payment.Tests` to mirror slices

**User stories**: 22 (tests mirror Features/<Slice>/, Domain/ kept separate).

### What to build

Move existing test classes from `Payment.Tests/Api/` and `Payment.Tests/Models/` per PRD Testing Decisions mapping:

- `PaymentEndpointsTests.cs` → split across `Features/GetPaymentById/`, `Features/GetPaymentByOrder/`, `Features/CapturePayment/`, `Features/RefundPayment/` based on the route each test method exercises
- `PaymentOwnershipTests.cs` → split per slice that enforces ownership (`Features/GetPaymentById/` + `Features/GetPaymentByOrder/`)
- `AuthorizePaymentCommandHandlerTests.cs` → `Features/AuthorizePaymentCommand/`
- `CapturePaymentCommandHandlerTests.cs` → `Features/CapturePaymentCommand/`
- `RefundPaymentCommandHandlerTests.cs` → `Features/RefundPaymentCommand/`
- `VoidPaymentCommandHandlerTests.cs` → `Features/VoidPaymentCommand/`
- (Add OrderCreated test if absent) → `Features/OrderCreated/`
- `InternalOutboxEndpointsTests.cs` → `Infrastructure/Outbox/` (mirror)
- `HealthEndpointTests.cs`, `QaSeedPresenceTests.cs`, `Authentication/*` → top-level (cross-cutting)
- `Models/PaymentStateMachineTests.cs` → `Domain/PaymentStateMachineTests.cs`
- `IntegrationEvents/MessagingProviderBootTests.cs` → kept under `IntegrationEvents/` root (platform plumbing test — same call as Shipping)

**Add new tests** to pin Payment-specific seams:
- `Architecture/LayoutAnalyzerTests.cs` — already added Phase 1 with skipped tests; enabled Phase 10. No change here.
- One paired integration test per multi-producer event:
  - `Features/CapturePayment/MultiProducerMapWiringTests.cs` (or co-located in `Features/CapturePaymentCommand/`) asserting HTTP `POST /{id}/capture` and saga `CapturePaymentCommand` produce byte-identical `PaymentCapturedEvent` payload + headers.
  - Same for refund.
- Unit tests for each `IIntegrationMap<,>` (5 total) under their owning slice folder — small pure-function assertions on field-level preservation (`PaymentId`, `OrderId`, `CustomerId`, `Amount`, `Currency`, `Reason`).
- `Infrastructure/Outbox/DomainEventOutboxInterceptorTests.cs` (or under `Infrastructure/Outbox/`) covering: (a) N domain events → N outbox events with correct mapped payloads; (b) unmapped type throws `InvalidOperationException` with descriptive wording.

Keep `IntegrationTestBase.cs` + `PaymentWebApplicationFactory.cs` at project root. Delete the emptied `Api/` and `Models/` test folders. Namespace updates only on relocated tests — no behavior change on the pre-existing ones.

### Acceptance criteria

- [ ] Build green
- [ ] Full `dotnet test payment-microservice/Payment.Tests` green (zero behavior diff on pre-existing tests)
- [ ] `Payment.Tests/Api/` folder deleted
- [ ] `Payment.Tests/Models/` folder deleted
- [ ] `Payment.Tests/Features/` folder count = slice count (9)
- [ ] Multi-producer wiring integration tests present and green (capture + refund)
- [ ] 5 `IIntegrationMap<,>` unit-test files present and green
- [ ] `DomainEventOutboxInterceptorTests.cs` present and green
- [ ] Commit: `refactor(payment): Phase 9 reshape Payment.Tests into Features/`

---

## Phase 10: Enable NetArchTest + LayoutAnalyzer rules

**User stories**: 16, 17, 31 (boundaries enforced; AI edits cannot drift).

### What to build

Unskip `LayoutTests.cs` + `LayoutAnalyzerTests.cs`. Fill in NetArchTest rules:

- `Payment.Service.Domain.*` must not depend on `Payment.Service.Infrastructure.*`, `Payment.Service.Features.*`, `Payment.Service.Contracts.*`
- `Payment.Service.Features.<X>` must not depend on `Payment.Service.Features.<Y>` for distinct slices
- `Payment.Service.Infrastructure.*` may reference only `Domain` + `Contracts` (+ allowed shared-lib namespaces)
- `Payment.Service.Contracts.*` must not reference anything internal beyond `Payment.Service.Contracts.*`

Promote `Payment.Service.LayoutAnalyzer` diagnostics from hidden to error severity (`.editorconfig` or analyzer manifest). Fill in analyzer banned-namespace / banned-symbol diagnostics mirroring `Order.Service.LayoutAnalyzer` with `PAYLAY***` IDs.

### Acceptance criteria

- [ ] `dotnet build payment-microservice` green (analyzer doesn't fire on existing code — proves the refactor satisfies the rules)
- [ ] Full `Payment.Tests` green including all unskipped Architecture tests
- [ ] `LayoutAnalyzerTests.cs` proves each rule fires on synthetic violation input
- [ ] Commit: `refactor(payment): Phase 10 enforce layout boundaries`

---

## Phase 11: Docs — root `CLAUDE.md` Payment exception paragraph

**User stories**: 28 (composes ADR 0011 by reference), 29 (root CLAUDE.md exception paragraph documents divergences).

### What to build

Add one paragraph to root `CLAUDE.md` under the existing pilot-exception block (after the Shipping paragraph), matching the Order/Product/Basket/Auth/Inventory/Shipping style:

> **Payment service exception** — seventh Clean Architecture + Vertical Slices pilot, same layout as Order/Product/Basket/Inventory/Shipping: `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/`. Boundaries enforced by NetArchTest (`Payment.Tests/Architecture/LayoutTests.cs`) and the Roslyn `Payment.Service.LayoutAnalyzer`. Composes ADR [0011](docs/adr/0011-order-cleanarch-vsa-pilot.md) by reference (no new ADR); reuses the [adding-a-new-slice.md](docs/runbooks/adding-a-new-slice.md) runbook unchanged. **Diverges from Shipping/Inventory (and re-adopts the Order pattern): `IIntegrationMap<,>` + `DomainEventOutboxInterceptor` seam reintroduced because `PaymentContext.Translate` was a real smell with a real workaround (`AuthorizePaymentCommandHandler` manual `DequeueDomainEvents()`) to dissolve; `IPaymentStore` lives in `Domain/Abstractions/` with `EfPaymentStore` in Infrastructure (matches Order/Inventory/Shipping); `IPaymentGateway` lifted to `Domain/Abstractions/` with `InMemoryPaymentGateway` impl in `Infrastructure/Gateways/` (mirrors Shipping `ICarrierGateway` shape); `PaymentMetrics` moved to `Infrastructure/Observability/` (no peer-layer `Observability/` folder); HTTP `CapturePayment`/`RefundPayment` and saga `CapturePaymentCommand`/`RefundPaymentCommand` are distinct slices that share the integration-event mapper through DI (multi-producer convention new to Payment: HTTP slice owns the `IIntegrationMap<,>` file, saga slice raises the same domain event and the interceptor resolves the map globally — not a slice-to-slice source reference); saga commands (`AuthorizePaymentCommand`/`CapturePaymentCommand`/`VoidPaymentCommand`/`RefundPaymentCommand`) consumed from `ECommerce.Shared.IntegrationEvents.Commands`, not owned in local `Contracts/Integration/`; `OrderCustomer` idempotency record is a Domain type co-located with `Payment` aggregate, written by `Features/OrderCreated/` and read by `Features/AuthorizePaymentCommand/`.** Propagation to remaining service (saga) is a separate ADR.

No new ADR. No runbook changes.

### Acceptance criteria

- [ ] `CLAUDE.md` contains the new paragraph; existing pilot paragraphs unchanged
- [ ] `dotnet format --verify-no-changes` green
- [ ] Markdown links resolve (ADR 0011 + adding-a-new-slice)
- [ ] Commit: `refactor(payment): Phase 11 docs root CLAUDE.md Payment exception`

---

## Verification (end-to-end, after Phase 11)

Run each from a clean `dotnet restore`:

1. **Format + build + test full Payment stack**
   ```bash
   find payment-microservice -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
   cd payment-microservice && dotnet restore && dotnet format --verify-no-changes && dotnet build && dotnet test
   ```
   Expected: all green.

2. **Pre-commit hook on the final commit**
   ```bash
   dotnet husky run --group pre-commit
   ```
   Expected: green (format + build + Basket tests).

3. **End-to-end stack smoke**
   ```bash
   docker compose up --build
   ```
   Then via Bruno/curl against `http://localhost:8007`:
   - `GET /by-order/{orderId}` (user auth, customer-id ownership) → 200 / 404
   - `GET /{paymentId}` (user auth, customer-id ownership) → 200 / 404 / 404-on-foreign-customer
   - `POST /{paymentId}/capture` (Admin) → 200 from Authorized; 409 from Pending/Voided/Failed; idempotent 200 from already-Captured. Verify `PaymentCapturedEvent` published (RabbitMQ Mgmt UI or service-bus explorer).
   - `POST /{paymentId}/refund` (Admin, optional body `{ amount }`) → 200 from Captured; 409 from other states. Verify `PaymentRefundedEvent` published.
   - Saga path: produce an `AuthorizePaymentCommand` (via saga service or test publisher) → verify `PaymentAuthorizedEvent` (gateway accept) OR `PaymentFailedEvent` (gateway decline) carries identical `CorrelationId` / `CausationId` / `SagaId`.
   - Saga `CapturePaymentCommand` → verify identical `PaymentCapturedEvent` payload shape as HTTP capture (multi-producer wiring proof).
   - Saga `VoidPaymentCommand` → verify `PaymentVoidedEvent`.
   - Saga `RefundPaymentCommand` → verify identical `PaymentRefundedEvent` payload shape as HTTP refund.
   - `OrderCreatedEvent` consumption → verify `OrderCustomers` row appears (idempotent on redelivery).
   - `AuthorizePaymentCommand` arriving before `OrderCreatedEvent` for the same order → handler returns early (no payment row created), awaits redelivery; redelivery after `OrderCreated` → payment created and reply emitted.
   - `GET /internal/outbox/failed` with user token → 403; with service token → 200.
   - `GET /health` → 200.

4. **Boundary regression check**
   Add a deliberate violation locally (e.g. `Domain/Payment.cs` adds `using Payment.Service.Contracts.Integration;`); confirm:
   - `dotnet build` fails with `PAYLAY***` analyzer diagnostic
   - `dotnet test Payment.Tests --filter LayoutTests` fails the matching NetArchTest assertion
   Revert.

5. **DLQ poller still ingests Payment failures**
   In a stack run, induce a poison-message scenario and confirm the API gateway DLQ poller still persists Payment rows from `/internal/outbox/failed`.

6. **Outbox interceptor regression check**
   Construct a synthetic domain-event type with no registered `IIntegrationMap<,>`; raise it from a test aggregate; confirm `DomainEventOutboxInterceptor` throws `InvalidOperationException` with `"No integration-event translation registered for domain event ..."` wording, matching the pre-refactor `PaymentContext.Translate` behavior exactly.

7. **Manual-drain regression check**
   Grep the entire `payment-microservice/Payment.Service/` for `DequeueDomainEvents`. Only matches should be in `Domain/Entity.cs` (the producer) and `Infrastructure/Outbox/DomainEventOutboxInterceptor.cs` (the consumer). Zero matches in `Features/*` slice handlers. (If `AuthorizePaymentCommandHandler` still has the manual drain, Phase 6 didn't finish.)

8. **Metrics parity**
   Hit Prometheus `/metrics` endpoint on Payment and confirm `payments_total` counter + authorize-latency histogram still emit with identical names / labels.

9. **PR open + bisect spot-check**
   Open single PR `refactor/payment-vsa` → `main`. `git bisect` any 3 random commits in the branch range and confirm each builds + tests green in isolation.

## Phases needing manual `dotnet test payment-microservice/Payment.Tests` before commit

Pre-commit hook only runs Basket tests. Run Payment tests locally before staging on every phase, but pay especially close attention to behavior-touching phases:

- **Phase 4** — `EfPaymentStore` split (largest mechanical surface before Phase 6)
- **Phase 6** — outbox interceptor seam + manual-drain removal (**largest behavior-touching phase of the pilot**; saga reply parity + correlation propagation + idempotency-on-existing-payment + order-customer-lookup race all hinge on this)
- **Phase 7b** — HTTP write slices + multi-producer map relocation (mapper resolution path changes; capture/refund outbox emission must stay byte-identical)
- **Phase 7c** — saga command slices (`AuthorizePaymentCommand` is the most behavior-rich slice; verify the two short-circuit paths — existing-payment idempotency and missing-customer race — are pinned by tests)
- **Phase 9** — multi-producer wiring integration tests added (capture + refund); these are the convention-pinning tests for the entire pilot
- **Phase 10** — rule enablement (NetArchTest only fires under `dotnet test`)

If hook fails with `MSB3248`: clean `bin`/`obj` → `dotnet restore --force` → rerun hook (per root `CLAUDE.md` sandbox policy). Do not `--no-verify`, do not defer validation. If still failing, **STOP and hand off to user — do not commit**.
