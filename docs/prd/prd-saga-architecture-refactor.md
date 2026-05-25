# Saga Service Clean Architecture + Vertical Slices Pilot PRD

> Modeled on epic [#152](https://github.com/daonhan/Microservices-in-.NET/issues/152) (Order pilot) and [#226](https://github.com/daonhan/Microservices-in-.NET/issues/226) (Payment pilot). Composes ADR [0011](../adr/0011-order-cleanarch-vsa-pilot.md) by reference; no new ADR.
> Branch: `refactor/saga-vsa`. Single PR for review.
> Saga is the **eighth and final** Clean Architecture + Vertical Slices pilot. After this lands, every service in the monorepo has migrated to the layout and the follow-up ADR can promote the convention from "per-service pilot exception" to "default service shape".

## Problem Statement

The `Saga.Service` codebase is organized by technical type, like every pre-pilot service in this repo: all integration-event consumers in `IntegrationEvents/EventHandlers/`, two large fan-out router classes (`OrderSagaReplyProcessor`, `RefundSagaReplyProcessor`) that dispatch sixteen inbound event types to two state machines, all HTTP routes in `Endpoints/OperatorSagaEndpoints.cs` + `Endpoints/InternalOutboxEndpoints.cs`, all domain types (saga aggregates, enums, transition records, reaper options) in a flat `Models/` folder, both state machines in `StateMachines/`, EF in `Infrastructure/Data/`, the reaper in `Infrastructure/Reaper/`, telemetry in a top-level `Observability/`. To understand or change one step of the order saga ("what happens when stock is reserved?") a developer must hop across `IntegrationEvents/EventHandlers/StockReservedEventHandler.cs`, `IntegrationEvents/EventHandlers/OrderSagaReplyProcessor.cs`, `StateMachines/OrderSagaStateMachine.cs`, `Models/OrderSagaState.cs`, and `Infrastructure/Data/EntityFramework/SagaContext.cs`, then reconstruct the step mentally.

Saga is structurally different from prior pilots in three ways that the current layout actively obscures:

1. **Two saga aggregates coexist in one service** (`OrderSagaState`, `RefundSagaState`) with mostly disjoint inbound triggers and a small set of shared refund-continuation replies (`PaymentFailedEvent`, `ShipmentFailedEvent`, `ShipmentCancelledEvent`, `OrderCancelledEvent`, `PaymentRefundedEvent`). The current layout flattens both sagas into the same folders, so "is this for Order or Refund?" is answerable only by reading the handler body.
2. **There are no HTTP write endpoints driving business state**; the saga is event-driven by design. The only HTTP write surface (`AbortSaga`) is a single route buried inside `OperatorSagaEndpoints.cs` alongside two read routes. Its blast radius (forcing compensation) is invisible from the folder layout.
3. **The reaper is a cross-cutting hosted service** that escalates timeouts into the state machine for both sagas. It is operational plumbing, not a feature, but the current layout offers no convention for distinguishing the two.

Boundaries between domain, application, and infrastructure exist only as conventions. Nothing prevents `Models/OrderSagaState.cs` from picking up EF Core references; nothing prevents a future contributor (human or AI) from adding a new event handler inside `IntegrationEvents/EventHandlers/` that bypasses the state machine or skips the saga-transition persistence row.

The team wants:

1. A codebase grouped by *what the service does at each inbound trigger* (one slice per inbound message or HTTP route), not by technical type.
2. The two saga aggregates surfaced as first-class folders, so `OrderSaga/StockReserved/` and `RefundSaga/PaymentRefunded/` answer "which saga, which step" by folder path alone.
3. Enforceable Clean Architecture boundaries: Domain has no infrastructure dependencies; Features depend on Domain + Contracts; Infrastructure implements abstractions declared in Domain.
4. The `OrderSagaReplyProcessor` + `RefundSagaReplyProcessor` fan-out routers dissolved into per-slice handlers, with the shared "load state → apply transition → persist → publish commands" loop lifted into a Domain-level abstraction so slice handlers stay thin.
5. A pattern consistent with the prior seven pilots (Order, Product, Basket, Auth, Inventory, Shipping, Payment) so the project's mental model stays uniform.

## Solution

Pilot Clean Architecture + Vertical Slice Architecture (VSA) on `Saga.Service` only, with zero behavior change. Inside a single `Saga.Service.csproj`, reorganize source into:

- `Features/<Saga>/<Trigger>/` — one folder per inbound trigger, grouped under the owning saga aggregate (`OrderSaga/`, `RefundSaga/`). Each slice owns its event handler, slice DI extension, and slice handler class. Operator HTTP slices live under `Features/Operator/{GetSaga,ListSagas,AbortSaga}/`.
- `Domain/` — saga aggregates and pure state machines: `Domain/OrderSaga/` (`OrderSagaState`, `OrderSagaStateMachine`, `OrderSagaStep`, `OrderSagaStateSnapshot`, `OrderSagaTransitionResult`), `Domain/RefundSaga/` (parallel set), `Domain/SagaInstance.cs`, `Domain/SagaTransition.cs`, `Domain/SagaStatus.cs`, `Domain/SagaTriggerKind.cs`, and `Domain/Abstractions/ISagaInstanceStore.cs` + `Domain/Abstractions/ISagaTransitionRunner.cs`. No EF, HTTP, or broker references.
- `Contracts/Integration/InboundEvents/` — local copies of the inbound integration event payloads (`OrderCreatedEvent`, `StockReservedEvent`, …, `RefundRequestedEvent`). Outbound commands (`ReserveStockCommand`, `AuthorizePaymentCommand`, …) are consumed from `ECommerce.Shared.IntegrationEvents.Commands` per the existing monorepo convention; saga does not own command payloads locally.
- `Infrastructure/Data/EntityFramework/` — `SagaContext`, `SagaContextDesignTimeFactory`, `EntityFrameworkExtensions`, `EfSagaInstanceStore` (impl of `ISagaInstanceStore`), `EfOrderSagaTransitionRunner` + `EfRefundSagaTransitionRunner` (impls of `ISagaTransitionRunner<TState, TEvent>`).
- `Infrastructure/Reaper/` — `SagaReaperService` (hosted), `OrderSagaTimeoutScheduler`, `SagaReaperOptions`. Matches Shipping's `Infrastructure/Carriers/CarrierPollingService` shape.
- `Infrastructure/Observability/` — `SagaTelemetry`.
- `Infrastructure/Outbox/` — `InternalOutboxEndpoints` (`RequireService`-gated ops endpoint).

Slice handlers are invoked through plain DI (constructor injection into the integration-event consumer or endpoint). No MediatR, no in-house dispatcher. Read slices project directly from `SagaContext` to response DTOs (CQRS-lite); write slices and event-driven slices call `ISagaTransitionRunner<TState, TEvent>.RunAsync(sagaCorrelationId, evt, transitionFn)` which encapsulates the load-state → pure-transition → persist-with-transition-row → enqueue-commands-via-outbox loop. The existing `OrderSagaReplyProcessor` + `RefundSagaReplyProcessor` fan-out routers are deleted; their dispatch logic dissolves into the per-slice handlers, and their shared persistence loop moves into `EfOrderSagaTransitionRunner` + `EfRefundSagaTransitionRunner`.

Saga emits commands (not domain-event-derived integration events) directly from the state-machine result. There is no `Translate(...)` switch in `SagaContext` to dissolve, and no `IIntegrationMap<,>` + `DomainEventOutboxInterceptor` seam is introduced — matches Inventory + Shipping (which also skipped the seam), diverges from Order + Payment (which had a concrete smell to dissolve).

Shared refund-continuation replies are handled by registering **two** slice handlers: one under `Features/OrderSaga/<Trigger>/` and one under `Features/RefundSaga/<Trigger>/`. The `ECommerce.Shared` event bus dispatches the message to both handlers; each loads its own saga state by id and no-ops if the message does not belong to its saga. This costs one extra store lookup for shared replies but keeps the "one inbound trigger per slice" rule intact and avoids slice-to-slice references.

Boundaries enforced with both NetArchTest assertions (in `Saga.Tests/Architecture/LayoutTests.cs`) and a Roslyn `Saga.Service.LayoutAnalyzer`. Tests reshape to mirror slices, with state-machine + reaper unit tests kept under `Saga.Tests/Domain/` and end-to-end fixture tests kept under `Saga.Tests/EndToEnd/`. Namespaces renamed to match the new folder layout (two-level under `Features/`: `Saga.Service.Features.OrderSaga.StockReserved`, etc.). The work lands as staged commits on a single branch `refactor/saga-vsa` and merges via one PR. The root `CLAUDE.md` gains a "Saga service exception" entry that composes ADR [0011](../adr/0011-order-cleanarch-vsa-pilot.md) by reference (no new ADR) and reuses the [adding-a-new-slice.md](../runbooks/adding-a-new-slice.md) runbook unchanged. Because saga is the eighth and final pilot, the CLAUDE.md entry also flags that a follow-up ADR can now promote the convention from "per-service pilot exception" to "default service shape".

## User Stories

1. As a Saga service developer, I want to open `Features/OrderSaga/StockReserved/` to see everything that happens when an order saga receives a stock-reservation reply, so that I do not have to follow the trail from `StockReservedEventHandler.cs` into `OrderSagaReplyProcessor.cs` into `OrderSagaStateMachine.cs` to reconstruct the step.
2. As a Saga service developer, I want both saga aggregates surfaced as first-class folders (`Features/OrderSaga/`, `Features/RefundSaga/`), so that "is this for Order or Refund?" is answerable by folder path without reading handler bodies.
3. As a Saga service developer, I want each slice to register its own dependencies via an `AddXxxSlice()` extension, so that adding a new saga step is a drop-in change and `Program.cs` reads like a manifest.
4. As a Saga service developer, I want to add a new inbound trigger (new reply event from a participant) by creating one new `Features/<Saga>/<EventName>/` folder, so that I never need to touch the other fifteen slices or a central router.
5. As a Saga service developer, I want to add a new operator HTTP action by creating one new `Features/Operator/<Action>/` folder, so that operator concerns and event-driven concerns share one slice convention.
6. As a Saga service developer, I want `Domain/OrderSaga/OrderSagaStateMachine.cs` and `Domain/RefundSaga/RefundSagaStateMachine.cs` to remain pure transition functions (snapshot + trigger → next snapshot + commands), with no EF or broker references, so that the saga lifecycle stays unit-testable in isolation as it is today.
7. As a Saga service developer, I want a single `ISagaTransitionRunner<TState, TEvent>` abstraction in `Domain/Abstractions/` that encapsulates the "load → apply pure transition → persist with transition row → enqueue commands via outbox" loop, so that each slice handler is a one-line orchestration (`await runner.RunAsync(sagaCorrelationId, evt, OrderSagaStateMachine.Transition, ct)`).
8. As a Saga service developer, I want the existing `OrderSagaReplyProcessor` + `RefundSagaReplyProcessor` fan-out routers deleted, with their dispatch logic dissolved into per-slice handlers and their persistence loop lifted into `EfOrderSagaTransitionRunner` + `EfRefundSagaTransitionRunner`, so that no class in the codebase knows about more than one saga step at a time.
9. As a Saga service developer, I want operator read slices (`GetSaga`, `ListSagas`) to project directly from `SagaContext` to `OperatorSagaResponses`, so that reads do not pay the cost of hydrating the saga aggregate or its transition collection.
10. As a Saga service developer, I want the operator `AbortSaga` slice to call into the state machine through `ISagaTransitionRunner` (or a saga-specific `BeginCompensation` entry on the runner) to force compensation, so that operator-driven aborts use the same persistence + outbox path as event-driven transitions.
11. As a Saga service developer, I want shared refund-continuation replies (`PaymentFailedEvent`, `ShipmentFailedEvent`, `ShipmentCancelledEvent`, `OrderCancelledEvent`, `PaymentRefundedEvent`) to be handled by two distinct owning-saga slices, each registered as `IEventHandler<TEvent>`, each looking up its own saga state by id and no-oping if the message is not its own, so that "one inbound trigger per slice" stays true and no cross-slice reference is introduced.
12. As a Saga service maintainer, I want `SagaContext` to remain a single-purpose persistence module after the refactor — no fan-out logic, no event-translation logic — so that the DbContext stays a deep module focused on persistence and unit-of-work only.
13. As a Saga service maintainer, I want **no** `IIntegrationMap<,>` + `DomainEventOutboxInterceptor` seam introduced for saga, because saga emits commands directly from the state-machine result and there is no domain-event-to-command translation switch to extract, so that the refactor stays "remove smells you have" rather than "speculatively add abstractions". Matches Inventory + Shipping; diverges from Order + Payment with rationale documented in the CLAUDE.md exception entry.
14. As a Saga service maintainer, I want `SagaReaperService` + `OrderSagaTimeoutScheduler` to live in `Infrastructure/Reaper/` (mirroring Shipping's `Infrastructure/Carriers/CarrierPollingService` placement), so that operational hosted services stay out of the feature manifest.
15. As a Saga service maintainer, I want `SagaTelemetry` moved from top-level `Observability/` to `Infrastructure/Observability/`, so that the top-level folder layout matches prior pilots exactly (Inventory, Shipping, Payment).
16. As a Saga service maintainer, I want NetArchTest rules that fail the test suite if `Domain` references infrastructure, if any slice references another slice (including across `OrderSaga`/`RefundSaga`/`Operator`), or if infrastructure leaks past Domain + Contracts, so that boundary violations are caught in CI rather than in code review.
17. As a Saga service maintainer, I want a Roslyn `Saga.Service.LayoutAnalyzer` as a second guardrail beside NetArchTest, so that violations surface as compiler errors during development — not only when tests run.
18. As a Saga service contributor, I want the cross-slice sharing rule documented as "duplicate first, extract on third" with a NetArchTest rule forbidding slice-to-slice references, so that I do not accidentally create hidden coupling between two saga steps.
19. As a Saga service contributor, I want namespaces to match the new two-level folder layout (`Saga.Service.Domain`, `Saga.Service.Domain.OrderSaga`, `Saga.Service.Domain.Abstractions`, `Saga.Service.Features.OrderSaga.StockReserved`, `Saga.Service.Features.RefundSaga.RefundRequested`, `Saga.Service.Features.Operator.AbortSaga`, `Saga.Service.Contracts.Integration.InboundEvents`, `Saga.Service.Infrastructure.Data.EntityFramework`, `Saga.Service.Infrastructure.Reaper`, `Saga.Service.Infrastructure.Observability`, `Saga.Service.Infrastructure.Outbox`), so that I can grep for layer membership and analyzer rules can target namespaces.
20. As a Saga service contributor, I want `Saga.Tests` reshaped to mirror `Features/<Saga>/<Trigger>/` while keeping `Saga.Tests/Domain/` state-machine + reaper unit tests separate and `Saga.Tests/EndToEnd/` fixture-driven tests at the top level, so that feature tests, domain unit tests, and end-to-end tests are each easy to locate.
21. As a Saga service contributor, I want `InternalOutboxEndpoints` (the DLQ-poller ops surface) under `Infrastructure/Outbox/`, not under `Features/`, so that operational plumbing does not pollute the feature manifest.
22. As a Saga service contributor, I want local inbound integration event copies (`OrderCreatedEvent`, `StockReservedEvent`, …, `RefundRequestedEvent`) moved to `Contracts/Integration/InboundEvents/` with no payload changes, and outbound commands continued to be consumed from `ECommerce.Shared.IntegrationEvents.Commands`, so that the wire deserialization shape is preserved and the "no `ECommerce.Shared` changes" rule from prior pilots is honored.
23. As a reviewer, I want the pilot to land as staged commits on one branch (`refactor/saga-vsa`) and a single PR, with each commit building and tests passing, so that the refactor is bisectable and reviewable end-to-end. Saga is larger than prior pilots (two sagas, ~19 slices, reaper, operator); commits will be more granular but the single-PR shape is preserved.
24. As a reviewer, I want zero behavior change from the pilot — every existing `Saga.Tests` test passes unchanged (modulo namespace updates), so that the layout migration cannot regress functional behavior. In particular: order-saga happy-path command sequence (`ReserveStock` → `AuthorizePayment` → `ConfirmOrder` → `CommitStock` → `CreateShipment`), compensation sequences (StockReservationFailed → none; PaymentFailed → ReleaseStock; OrderConfirmFailure paths → VoidPayment/ReleaseStock; ShipmentFailed → RefundPayment/CancelOrder/ReleaseStock), refund-saga happy path (`RefundRequested` → `RefundPaymentCommand` → `PaymentRefunded` → terminal), reaper-driven escalation (`SagaReaperService` polling cadence + `OrderSagaTimeoutScheduler` thresholds), correlation propagation (every emitted command carries the inbound event's `Id` as `CausationId` and the saga's id as `SagaId`), and operator endpoints (`/operator/api/sagas` list/detail/abort behavior + auth) remain byte-for-byte identical.
25. As a release engineer, I want the pilot to leave `ECommerce.Shared` untouched (no nupkg version bump), so that other services are not forced to consume a new shared package version.
26. As a release engineer, I want the pre-commit hook (`dotnet format`, `dotnet build`, then Basket tests) to gate every commit on the refactor branch, so that the branch cannot accumulate partial-validation commits. Saga tests run manually before pushing per the root `CLAUDE.md` sandbox policy.
27. As an architect, I want a root `CLAUDE.md` "Saga service exception" entry that composes ADR-0011 by reference (no new ADR) and reuses the existing adding-a-new-slice runbook unchanged, so that documentation stays DRY across pilots.
28. As an architect, I want the CLAUDE.md entry to explicitly call out saga-specific divergences vs prior pilots (two-level `Features/<Saga>/<Trigger>/` namespace nesting; `ISagaTransitionRunner<TState, TEvent>` Domain abstraction new to saga; reply-processor fan-out routers dissolved; no `IIntegrationMap<,>` seam; dual-subscription convention for refund saga shared replies; reaper as `Infrastructure/Reaper/` hosted service mirroring Shipping's `CarrierPollingService`; absence of any HTTP write endpoint outside `Features/Operator/AbortSaga/`), so that future contributors understand why saga looks slightly different.
29. As an architect, I want the CLAUDE.md entry to note that saga is the **eighth and final** pilot, so that the follow-up ADR can promote VSA from "per-service pilot exception" to "default service shape" rather than continuing to add per-service exception paragraphs.
30. As an AI-assisted contributor, I want layout, namespaces, and architecture rules self-describing and analyzer-enforced (and the two-level `Features/<Saga>/` shape explicit in the analyzer rule), so that AI edits cannot silently drift a slice from one saga into another.
31. As an operator, I want the DLQ poller's call to `/internal/outbox/failed` (gated by `RequireService`) to continue working after the refactor, so that DLQ ingestion is not interrupted.
32. As an operator, I want trace IDs and correlation IDs to propagate identically through inbound participant event → `ISagaTransitionRunner.RunAsync` → outbox `*Command` after the refactor, so that observability dashboards do not break. `SagaTelemetry` activity-source name + meter name + counter / histogram names stay identical.
33. As an operator, I want the `/operator/api/sagas` read + list + abort surface to keep its existing routes, response shapes, auth requirements (Bearer + operator policy), and status codes, so that the operator runbook and any external tooling do not need updates.

## Implementation Decisions

### Pilot scope

- Pilot is `Saga.Service` only. No other service changes.
- Saga is the eighth and final Clean Architecture + Vertical Slices pilot. After this lands, the follow-up ADR can move from per-service exceptions to default convention.

### Project shape

- Single `Saga.Service.csproj`. No split into `Saga.Domain` / `Saga.Application` / `Saga.Infrastructure` projects.
- Boundaries enforced by namespace conventions + analyzer rules + architecture tests, not csproj references.

### Folder topology

- `Features/<Saga>/<Trigger>/` — two-level nesting under `Features/`. Final slice list (19 total):
  - **OrderSaga (14 slices):** `OrderCreated/`, `StockReserved/`, `StockReservationFailed/`, `PaymentAuthorized/`, `PaymentFailed/`, `OrderConfirmed/`, `StockCommitted/`, `ShipmentCreated/`, `ShipmentFailed/`, `StockReleased/`, `PaymentVoided/`, `PaymentRefunded/`, `OrderCancelled/`, `ShipmentCancelled/`.
  - **RefundSaga (6 slices):** `RefundRequested/`, `PaymentFailed/`, `PaymentRefunded/`, `ShipmentFailed/`, `ShipmentCancelled/`, `OrderCancelled/`.
  - **Operator (3 slices):** `GetSaga/`, `ListSagas/`, `AbortSaga/`.
- `Domain/` — saga aggregates and pure state machines.
  - `Domain/OrderSaga/` — `OrderSagaState`, `OrderSagaStateMachine`, `OrderSagaStep`, `OrderSagaStateSnapshot`, `OrderSagaTransitionResult`, `OrderSagaTimeoutOptions`.
  - `Domain/RefundSaga/` — `RefundSagaState`, `RefundSagaStateMachine`, `RefundSagaStep`, `RefundSagaStateSnapshot`, `RefundSagaTransitionResult`.
  - `Domain/SagaInstance.cs`, `Domain/SagaTransition.cs`, `Domain/SagaStatus.cs`, `Domain/SagaTriggerKind.cs`, `Domain/SagaReaperOptions.cs`.
  - `Domain/Abstractions/ISagaInstanceStore.cs`, `Domain/Abstractions/ISagaTransitionRunner.cs`. No EF / HTTP / broker references.
- `Contracts/Integration/InboundEvents/` — local copies of inbound integration event payloads: `OrderCreatedEvent`, `StockReservedEvent`, `StockReservationFailedEvent`, `PaymentAuthorizedEvent`, `PaymentFailedEvent`, `OrderConfirmedEvent`, `StockCommittedEvent`, `ShipmentCreatedEvent`, `ShipmentFailedEvent`, `StockReleasedEvent`, `PaymentVoidedEvent`, `PaymentRefundedEvent`, `OrderCancelledEvent`, `ShipmentCancelledEvent`, `RefundRequestedEvent`. Payloads unchanged.
- `Contracts/` — no outbound command types. Outbound saga commands continue to be consumed from `ECommerce.Shared.IntegrationEvents.Commands` per the existing monorepo convention.
- `Infrastructure/Data/EntityFramework/` — `SagaContext`, `SagaContextDesignTimeFactory`, `EntityFrameworkExtensions`, `EfSagaInstanceStore` (impl of `ISagaInstanceStore`), `EfOrderSagaTransitionRunner` + `EfRefundSagaTransitionRunner` (impls of `ISagaTransitionRunner<TState, TEvent>`).
- `Infrastructure/Reaper/` — `SagaReaperService` (hosted), `OrderSagaTimeoutScheduler`.
- `Infrastructure/Observability/` — `SagaTelemetry`.
- `Infrastructure/Outbox/` — `InternalOutboxEndpoints` (`RequireService`-gated ops endpoint).
- `Migrations/` — unchanged; `generated_code = true`.

### Dispatch model

- No MediatR. No in-house mediator.
- Integration-event consumers and HTTP endpoints take their slice handler class via constructor injection and call `HandleAsync(...)` directly.
- Slice handler classes are `internal sealed` with one public async method.

### Domain richness rule

- Rich domain: `OrderSagaStateMachine` and `RefundSagaStateMachine` remain pure transition functions (snapshot + trigger → next snapshot + commands). All existing transitions and compensation sequences preserved verbatim.
- Saga aggregates (`OrderSagaState`, `RefundSagaState`) own their invariants: status (`Running` / `Compensating` / `Completed` / `Failed`), current step, last-step-result, compensation origin, timestamps. Existing rules preserved.
- Slice handlers (event-driven and HTTP) are orchestration only: take inbound trigger, call `ISagaTransitionRunner.RunAsync(sagaCorrelationId, trigger, transitionFn)`, return.

### Persistence

- `ISagaInstanceStore` (new abstraction) declared in `Domain/Abstractions/`. Surface covers what today's `SagaContext` exposes for saga lookup + persistence (load by saga id, load by correlation id, save).
- EF implementation `EfSagaInstanceStore` in `Infrastructure/Data/EntityFramework/`. `SagaContext` remains persistence-only after the refactor.

### Saga transition runner — new Domain abstraction

- New abstraction `ISagaTransitionRunner<TState, TEvent>` in `Domain/Abstractions/`. Surface:
  - `Task RunAsync(string sagaCorrelationId, TEvent trigger, Func<TState, TEvent, TransitionResult<TState>> transitionFn, CancellationToken ct)` — loads state, applies pure transition, persists state + transition row, enqueues result commands via the outbox unit-of-work, all in one EF transaction.
  - Saga-specific overload for `BeginCompensation` (operator-driven abort, reaper-driven timeout escalation) takes the explicit origin step.
- EF implementations `EfOrderSagaTransitionRunner` and `EfRefundSagaTransitionRunner` in `Infrastructure/Data/EntityFramework/`. They consume `ISagaInstanceStore`, the `Outbox` unit-of-work, and `SagaTelemetry`.
- Slice handlers become one-liners. Example: `Features/OrderSaga/StockReserved/Handler.cs` calls `runner.RunAsync(evt.OrderId, evt, OrderSagaStateMachine.Transition, ct)`.

### Outbox / command emission seam

- **No `IIntegrationMap<,>` + `DomainEventOutboxInterceptor` seam is introduced.** Saga emits commands directly from `OrderSagaTransitionResult.Commands` / `RefundSagaTransitionResult.Commands`. There is no domain-event-to-integration-event translation switch in `SagaContext` today and no smell to dissolve. Matches Inventory + Shipping; diverges from Order + Payment.
- `ISagaTransitionRunner` implementations call `IOutboxStore.AddOutboxEvent(command)` for each command returned by the transition result, inside the same EF transaction as the state persistence.

### Reaper

- `SagaReaperService` (hosted) and `OrderSagaTimeoutScheduler` stay together under `Infrastructure/Reaper/`. Mirrors Shipping's `Infrastructure/Carriers/CarrierPollingService` placement.
- Reaper invokes the saga state machines via `ISagaTransitionRunner.BeginCompensation(...)`. No direct `SagaContext` access from reaper after refactor.

### Operator endpoints

- Three slices under `Features/Operator/`: `GetSaga/` (read), `ListSagas/` (read), `AbortSaga/` (write — drives `ISagaTransitionRunner.BeginCompensation`).
- Public routes (`/operator/api/sagas`, `/operator/api/sagas/{id}`, `/operator/api/sagas/{id}/abort`), auth (Bearer + operator policy), and response shapes preserved unchanged.

### Dual subscription for shared refund replies

- Two slices register for each shared refund-continuation reply: `Features/OrderSaga/<Trigger>/` and `Features/RefundSaga/<Trigger>/`.
- Shared replies are `PaymentFailedEvent`, `ShipmentFailedEvent`, `ShipmentCancelledEvent`, `OrderCancelledEvent`, and `PaymentRefundedEvent`.
- `ECommerce.Shared` event bus dispatches the message to both handlers.
- Each handler attempts to load its saga state by `SagaId` and no-ops if the message does not belong to its saga.
- This costs one extra store lookup per refund vs today's router but preserves "one inbound trigger per slice" and avoids slice-to-slice references.

### Slice DI

- Each slice exposes a static class with `AddXxxSlice(this IServiceCollection)` extension. The extension registers the handler, any slice-specific options, and (for event-driven slices) calls `AddEventHandler<TEvent, THandler>` from `ECommerce.Shared.Infrastructure.EventBus`.
- `Program.cs` chains slice extensions in a fluent manifest. Per-event `AddEventHandler` calls in today's `Program.cs` move into slice extensions.

### Namespaces

- `Saga.Service.Domain`, `Saga.Service.Domain.OrderSaga`, `Saga.Service.Domain.RefundSaga`, `Saga.Service.Domain.Abstractions`.
- `Saga.Service.Features.OrderSaga.<Trigger>`, `Saga.Service.Features.RefundSaga.<Trigger>`, `Saga.Service.Features.Operator.<Action>` (two-level nesting under `Features/`).
- `Saga.Service.Contracts.Integration.InboundEvents`.
- `Saga.Service.Infrastructure.Data.EntityFramework`, `Saga.Service.Infrastructure.Reaper`, `Saga.Service.Infrastructure.Observability`, `Saga.Service.Infrastructure.Outbox`.

### Cross-slice sharing rule

- Rule of three: duplicate freely between slices; extract to `Domain/` (behavioral) or `Features/Shared/` (helper) only on the third use.
- NetArchTest rule forbids `Saga.Service.Features.X.*` referencing `Saga.Service.Features.Y.*` for any distinct slices, including across `OrderSaga` / `RefundSaga` / `Operator`. Dual-subscription slices each stand alone — neither references the other.

### Boundary enforcement (belt + suspenders)

- **NetArchTest** in `Saga.Tests/Architecture/LayoutTests.cs`. Four rules, all enabled:
  1. `Saga.Service.Domain.*` may not reference `Saga.Service.Infrastructure.*` or `Saga.Service.Features.*`.
  2. `Saga.Service.Features.<X>.*` may not reference `Saga.Service.Features.<Y>.*` for distinct slices (two-level slice identity: `OrderSaga.StockReserved` and `OrderSaga.PaymentAuthorized` are distinct).
  3. `Saga.Service.Infrastructure.*` may not reference `Saga.Service.Features.*`.
  4. `Saga.Service.Contracts.*` may not reference any other internal `Saga.Service.*` namespace.
- **Roslyn `Saga.Service.LayoutAnalyzer`** raises the same four rules as build-time compiler errors via `.editorconfig`.
- Both must fail on an intentional violation spike before being marked done.

### Internal ops endpoints

- `InternalOutboxEndpoints` moves from `Endpoints/InternalOutboxEndpoints.cs` to `Infrastructure/Outbox/InternalOutboxEndpoints.cs`.
- Wiring in `Program.cs` after slice registration.
- `RequireService` policy gate preserved on `/internal/outbox/failed`.

### Routes / contracts / payloads

- Public HTTP routes, response shapes, status codes, and auth requirements of `OperatorSagaEndpoints` preserved unchanged.
- Inbound integration event payloads preserved — only their location (folder + namespace) moves.
- Outbound command payloads unchanged (consumed from `ECommerce.Shared.IntegrationEvents.Commands`).

### Shared library

- `ECommerce.Shared` not modified. No `dotnet pack`, no nupkg version bump.

### Validation

- Out of scope. Existing absence of `FluentValidation` / `DataAnnotations` preserved. Listed as a follow-up in the CLAUDE.md exception entry.

### Rollout

- Branch `refactor/saga-vsa`.
- Staged commits land in this order, each green:
  1. Scaffold NetArchTest project dependency + `Saga.Tests/Architecture/LayoutTests.cs` with rules initially skipped.
  2. `Domain/` move: split `Models/` into `Domain/OrderSaga/`, `Domain/RefundSaga/`, top-level `Domain/`; move `StateMachines/*` into `Domain/<Saga>/`; declare `Domain/Abstractions/ISagaInstanceStore.cs` + `Domain/Abstractions/ISagaTransitionRunner.cs`.
  3. `Contracts/Integration/InboundEvents/` move: relocate the 15 local event copies; rename namespaces.
  4. Infrastructure moves: `Infrastructure/Data/EntityFramework/EfSagaInstanceStore.cs` extraction (or `SagaContext` retains the impl); `Infrastructure/Reaper/` cleanup (move from today's location, already close); `Infrastructure/Observability/SagaTelemetry.cs` move; `Infrastructure/Outbox/InternalOutboxEndpoints.cs` move.
  5. `ISagaTransitionRunner` extraction: implement `EfOrderSagaTransitionRunner` + `EfRefundSagaTransitionRunner`; have today's reply processors delegate to them (intermediate step) so each commit is green.
  6. OrderSaga slices: extract one slice per inbound trigger (14 total). Each slice deletes its existing `*EventHandler.cs` and the corresponding `OrderSagaReplyProcessor` branch. Land in grouped commits (e.g., happy-path slices, compensation reply slices) for review readability.
  7. RefundSaga slices: extract `RefundRequested/`, `PaymentFailed/`, `PaymentRefunded/`, `ShipmentFailed/`, `ShipmentCancelled/`, and `OrderCancelled/`. Delete `RefundSagaReplyProcessor`.
  8. Operator slices: extract `GetSaga/`, `ListSagas/`, `AbortSaga/`; retire `OperatorSagaEndpoints.cs`.
  9. Reshape `Saga.Tests`: move `Api/*EndpointTests.cs` into `Saga.Tests/Features/<Saga>/<Trigger>/`; keep `Domain/`, `EndToEnd/`, `Authentication/` at top level.
  10. Enable NetArchTest rules; add `.editorconfig` / Roslyn `Saga.Service.LayoutAnalyzer` rules.
  11. Root `CLAUDE.md` "Saga service exception" entry; mention saga is the eighth and final pilot.
- Single PR for review. Pre-commit hook gates every commit (no `--no-verify`, no validation deferral).

## Testing Decisions

### Test philosophy

- A good test verifies external behavior of a module through its public interface, not internal implementation details.
- Refactor must produce zero behavior change. Every existing `Saga.Tests` test must continue to pass without modification beyond namespace updates required by the rename.
- New tests are added only for new seams (`ISagaTransitionRunner` implementations + dual-subscription dispatch) and for the architecture rules themselves.

### Modules to test

- **`OrderSagaStateMachine` + `RefundSagaStateMachine` (unchanged tests)** — existing `Saga.Tests/Domain/OrderSagaStateMachineTests.cs`, `RefundSagaStateMachineTests.cs`, `OrderSagaCompensationTests.cs`. Kept verbatim, only namespace touched. These are the load-bearing unit tests of saga behavior; their coverage stays.
- **`SagaReaperServiceTests`** — existing `Saga.Tests/Domain/SagaReaperServiceTests.cs`. Kept verbatim; updated to reference `Infrastructure/Reaper/` namespace and to inject the new `ISagaTransitionRunner` abstraction where today it pokes `SagaContext` directly.
- **Per-slice handler tests** — existing `Saga.Tests/Api/OrderSagaOrchestratorTests.cs` + `RefundSagaOrchestratorTests.cs` + `OperatorEndpointTests.cs` + `SagaObservabilityTests.cs` are reshaped into `Saga.Tests/Features/<Saga>/<Trigger>/` (per-event tests) and `Saga.Tests/Features/Operator/<Action>/` (per-operator-action tests). They continue to use `SagaWebApplicationFactory`. Test bodies unchanged beyond import paths.
- **`EfOrderSagaTransitionRunner` + `EfRefundSagaTransitionRunner`** — new integration tests covering: given an inbound trigger, the runner loads state, applies the pure transition, persists the new snapshot + a `SagaTransition` row, and enqueues all returned commands into the outbox in the same transaction; on transition-function returning `Changed: false`, no persistence or outbox writes occur; on `BeginCompensation`, the correct compensation sequence is initiated. Mirror Order's prior-art integration tests for the outbox interceptor (`Order.Tests/Infrastructure/Outbox/DomainEventOutboxInterceptorTests.cs`).
- **Dual-subscription dispatch for shared refund replies** — new integration tests/assertions covering registration of both `Features/OrderSaga/<Trigger>/` and `Features/RefundSaga/<Trigger>/` handlers for shared refund-continuation replies, plus behavior coverage for `PaymentRefundedEvent`.
- **`Saga.Tests/Architecture/LayoutTests.cs`** — new NetArchTest rules tests that act as the executable specification of the boundary policy. Includes the two-level slice-identity rule (`OrderSaga.StockReserved` vs `OrderSaga.PaymentAuthorized` are distinct). Fail intentionally before enabling.
- **`Saga.Tests/EndToEnd/OrderSagaEndToEndTests.cs`** — kept verbatim; the fixture and end-to-end happy-path / compensation-path coverage are unchanged.

### Prior art in the codebase

- `Saga.Tests/SagaWebApplicationFactory.cs` + `Saga.Tests/EndToEnd/SagaEndToEndWebApplicationFactory.cs` — existing factories used by all current Api + EndToEnd tests. Refactor preserves both.
- `Saga.Tests/Domain/OrderSagaStateMachineTests.cs` — existing pure-function unit tests of the state machine. Pattern of `Given_When_Then` underscored display names preserved (`CA1707` suppressed via `Directory.Build.props`).
- `Order.Tests/Architecture/LayoutTests.cs`, `Payment.Tests/Architecture/LayoutTests.cs`, `Shipping.Tests/Architecture/LayoutTests.cs` — prior-pilot NetArchTest layout rules. Use as templates; saga's rule set is the same four rules with the two-level slice-identity twist.
- `Inventory.Tests/Features/<Slice>/*` and `Shipping.Tests/Features/<Slice>/*` — prior-art "tests mirror feature folders" shape. Saga adds one more nesting level: `Features/<Saga>/<Trigger>/`.
- Pre-commit hook (`dotnet husky run --group pre-commit`) enforces `dotnet format --verify-no-changes` and `dotnet build --no-restore` + Basket tests on every commit. Saga tests run manually per the root `CLAUDE.md` sandbox policy before pushing.

## Out of Scope

- Refactoring any other service. Saga is the last pilot; the follow-up ADR for promoting VSA from per-service exception to default convention is a separate document.
- Modifying `ECommerce.Shared`. The pilot composes existing `AddSqlServerDatastore`, `AddOutbox`, `AddPlatformEventBus`, `AddEventHandler`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddPlatformOpenApi`, `AuthorizationPolicies.RequireServicePolicy`, `IntegrationEvents.Commands.*`.
- Adding request validation (FluentValidation or DataAnnotations). Listed as a follow-up in the CLAUDE.md exception entry.
- Introducing MediatR or any mediator-style dispatcher.
- Introducing the `IIntegrationMap<,>` + `DomainEventOutboxInterceptor` seam. Saga has no `Translate(...)` smell to dissolve.
- Splitting `Saga.Service.csproj` into multiple projects.
- Changing the `SagaInstance` / `OrderSagaState` / `RefundSagaState` / `SagaTransition` database schema. No new EF migrations.
- Changing inbound integration event payload contracts or outbound command payload contracts. Only the location (folder + namespace) of inbound event copies moves.
- Changing the outbox table, dispatcher, or retry/DLQ behavior in `ECommerce.Shared.Infrastructure.Outbox`.
- Changing `OperatorSagaEndpoints`'s public HTTP routes, response shapes, status codes, or auth requirements.
- Changing reaper polling cadence, timeout thresholds, escalation rules, or `OrderSagaTimeoutOptions` / `SagaReaperOptions` defaults.
- Changing `SagaTelemetry` activity-source / meter / counter / histogram names or tags.
- Changing `OrderSagaStateMachine` / `RefundSagaStateMachine` transitions, compensation sequences, or terminal status rules.
- Adding a third saga (e.g., refund-from-customer-claim saga). The current Refund saga is preserved as-is.
- Migrating the refund saga rollout flag (`SAGA_ORCHESTRATOR_*` env vars) or the refund-saga allowlist/percentage scheme.
- Performance optimization. The dual-subscription `PaymentRefunded` slice's extra store lookup is a structural cost accepted for VSA purity, not a performance concern.
- Changing CI/CD pipelines, Docker Compose, Kubernetes manifests, or Azure pipelines.

## Further Notes

- Saga is the pilot's largest single refactor (~19 slices across two saga aggregates plus three operator slices plus a hosted reaper) but mechanically the most pattern-driven. The `ISagaTransitionRunner` abstraction does most of the new work; every reply slice becomes a one-line `runner.RunAsync(...)` call. The bulk of the diff is folder/namespace movement and the deletion of the two fan-out reply-processor classes.
- The `OrderSagaReplyProcessor` + `RefundSagaReplyProcessor` deletion is the most concrete pre-existing smell the pilot must resolve. They are the only fan-out routers in the monorepo and the only place where a single class knows about more than one saga step. Dissolving them is structurally analogous to dissolving `OrderContext.Translate` + the `AuthorizePaymentCommandHandler.DequeueDomainEvents()` workaround in the Payment pilot, although the mechanism differs (a per-saga runner, not a per-event mapper).
- The decision to skip `IIntegrationMap<,>` is deliberate and consistent with the "remove smells you have, do not add abstractions speculatively" rule. Saga emits commands, not events; the state machine returns concrete command instances; there is no central translation switch in `SagaContext`. Reintroducing the seam would add cost without removing pain.
- The two-level `Features/<Saga>/<Trigger>/` namespace nesting is new to the pilot. Prior pilots had flat `Features/<Slice>/`. The nesting is justified by the existence of two saga aggregates in one service — Order/Payment/Shipping/Inventory each had only one aggregate. The NetArchTest slice-to-slice rule generalizes naturally: any two distinct two-level paths are distinct slices.
- Shared refund reply dual-subscription is the only place in the monorepo where a single integration event is consumed by two slices that must both be offered the message (rather than one slice routing based on payload). The two-handler model is preferable to a router because the alternative requires a slice that references both saga slices, which the NetArchTest rule forbids. The cost (one extra store lookup for shared replies) is bounded and observable.
- The reaper-in-`Infrastructure/Reaper/` placement mirrors Shipping's `Infrastructure/Carriers/CarrierPollingService` precedent. Both are hosted polling services that drive Domain abstractions; neither is a feature in the VSA sense. The earlier alternative ("a `Features/<Saga>/TimeoutEscalation/` slice that the reaper polls") was considered and rejected because the reaper does not present an inbound trigger from outside the service — the timeout is internal scheduling.
- After this pilot lands, every service in the monorepo will be on the Clean Architecture + Vertical Slices layout. The next architectural decision (separate ADR) is whether the "per-service exception" paragraphs in root `CLAUDE.md` should be collapsed into a single "default service shape" paragraph plus a short divergence list per service, or left as-is. That is a documentation decision, not a code change.
- Behavioral guidance from root `CLAUDE.md` applies: surgical changes only, no improving adjacent code, match existing style, push back on over-engineering. The pilot is large in line count but mechanical in intent.
