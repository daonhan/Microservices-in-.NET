# Plan: Saga.Service Clean Architecture + Vertical Slice Pilot

> Source PRD: `docs/prd/prd-saga-architecture-refactor.md` (tracking issue [#243](https://github.com/daonhan/Microservices-in-.NET/issues/243))
> Branch: `refactor/saga-vsa`

## Context

`Saga.Service` is organized by technical type. 16 inbound integration-event consumers in `IntegrationEvents/EventHandlers/`, plus two large fan-out router classes (`OrderSagaReplyProcessor`, `RefundSagaReplyProcessor`) that dispatch sixteen inbound event types into two state machines. Two HTTP endpoints in `Endpoints/` (`OperatorSagaEndpoints.cs` exposes 3 routes — operator read/list/abort; `InternalOutboxEndpoints.cs` exposes the DLQ poller ops surface). 10 domain types flat under `Models/` (`OrderSagaState`, `OrderSagaStep`, `OrderSagaTimeoutOptions`, `RefundSagaState`, `RefundSagaStep`, `SagaInstance`, `SagaReaperOptions`, `SagaStatus`, `SagaTransition`, `SagaTriggerKind`). Both state machines in `StateMachines/` (`OrderSagaStateMachine`, `OrderSagaStateSnapshot`, `OrderSagaTransitionResult`, `RefundSagaStateMachine`, `RefundSagaStateSnapshot`, `RefundSagaTransitionResult`). `Infrastructure/Data/EntityFramework/SagaContext.cs` (+ design-time factory + EF extensions). `Infrastructure/Reaper/` (hosted `SagaReaperService` + `OrderSagaTimeoutScheduler`). `Observability/SagaTelemetry.cs` peer-level. Operator response DTOs in `ApiModels/OperatorSagaResponses.cs`. `Program.cs` chains 16 `AddEventHandler<TEvent, THandler>` calls.

Pilot #8 (after Order / Product / Basket / Auth / Inventory / Shipping / Payment). Eighth and final pilot — every service in monorepo on VSA after this lands. Zero functional behavior change. Boundaries enforced twice (NetArchTest + Roslyn analyzer). Two saga aggregates surface as first-class folders (`Features/OrderSaga/`, `Features/RefundSaga/`). Reply-processor fan-out routers dissolve; per-slice handlers replace processor branches; shared "load → transition → persist → publish-commands" loop lifts into new Domain abstraction `ISagaTransitionRunner<TState, TEvent>`. No `IIntegrationMap<,>` seam — saga emits commands, no `Translate` smell exists. `PaymentRefundedEvent` dual-subscription handled by registering two distinct slice handlers (one per saga); each no-ops if message not its own.

## Architectural decisions

Durable decisions that apply across all phases:

- **Project shape**: single `Saga.Service.csproj` retained; boundaries enforced by namespace + Roslyn analyzer + NetArchTest, not csproj split.
- **Folder topology**:
  - `Features/<Saga>/<Trigger>/` — two-level nesting under `Features/`. Self-contained per slice: event-handler class (implements `IEventHandler<TEvent>`), `internal sealed` handler, slice DI extension. Operator slices under `Features/Operator/<Action>/`.
  - `Domain/` — saga aggregates and pure state machines.
    - `Domain/OrderSaga/` — `OrderSagaState`, `OrderSagaStateMachine`, `OrderSagaStep`, `OrderSagaStateSnapshot`, `OrderSagaTransitionResult`, `OrderSagaTimeoutOptions`.
    - `Domain/RefundSaga/` — `RefundSagaState`, `RefundSagaStateMachine`, `RefundSagaStep`, `RefundSagaStateSnapshot`, `RefundSagaTransitionResult`.
    - `Domain/SagaInstance.cs`, `Domain/SagaTransition.cs`, `Domain/SagaStatus.cs`, `Domain/SagaTriggerKind.cs`, `Domain/SagaReaperOptions.cs`.
    - `Domain/Abstractions/ISagaInstanceStore.cs`, `Domain/Abstractions/ISagaTransitionRunner.cs`. Zero EF / HTTP / broker references.
  - `Contracts/Integration/InboundEvents/` — local copies of 15 inbound integration event payloads. Outbound saga commands consumed from `ECommerce.Shared.IntegrationEvents.Commands` (not owned locally).
  - `Infrastructure/Data/EntityFramework/` — `SagaContext` (DbContext only), `SagaContextDesignTimeFactory`, `EntityFrameworkExtensions`, `EfSagaInstanceStore`, `EfOrderSagaTransitionRunner`, `EfRefundSagaTransitionRunner`.
  - `Infrastructure/Reaper/` — `SagaReaperService` (hosted), `OrderSagaTimeoutScheduler`.
  - `Infrastructure/Observability/` — `SagaTelemetry`.
  - `Infrastructure/Outbox/` — `InternalOutboxEndpoints` (`RequireService`-gated).
- **Namespaces**: `Saga.Service.Domain`, `Saga.Service.Domain.OrderSaga`, `Saga.Service.Domain.RefundSaga`, `Saga.Service.Domain.Abstractions`, `Saga.Service.Features.OrderSaga.<Trigger>`, `Saga.Service.Features.RefundSaga.<Trigger>`, `Saga.Service.Features.Operator.<Action>`, `Saga.Service.Contracts.Integration.InboundEvents`, `Saga.Service.Infrastructure.Data.EntityFramework`, `Saga.Service.Infrastructure.Reaper`, `Saga.Service.Infrastructure.Observability`, `Saga.Service.Infrastructure.Outbox`. The `Saga.Service.Models`, `Saga.Service.Observability`, `Saga.Service.Endpoints`, `Saga.Service.IntegrationEvents`, `Saga.Service.StateMachines`, `Saga.Service.ApiModels` namespaces are retired. Two-level slice namespaces under `Features/` are **new to this pilot** (prior pilots flat).
- **HTTP routes**: unchanged — `GET /operator/api/sagas` (Bearer + operator policy), `GET /operator/api/sagas/{id}` (same), `POST /operator/api/sagas/{id}/abort` (same), `GET /internal/outbox/failed` (`RequireService`), `GET /health`.
- **Schema**: unchanged. No new EF migrations. `SagaInstance`, `OrderSagaState`, `RefundSagaState`, `SagaTransition` tables preserved.
- **Inbound event payloads**: unchanged shape (`OrderCreatedEvent`, `StockReservedEvent`, `StockReservationFailedEvent`, `PaymentAuthorizedEvent`, `PaymentFailedEvent`, `OrderConfirmedEvent`, `StockCommittedEvent`, `ShipmentCreatedEvent`, `ShipmentFailedEvent`, `StockReleasedEvent`, `PaymentVoidedEvent`, `PaymentRefundedEvent`, `OrderCancelledEvent`, `ShipmentCancelledEvent`, `RefundRequestedEvent`). Only folder + namespace moves.
- **Outbound saga commands**: continue to consume `ECommerce.Shared.IntegrationEvents.Commands.*` (`ReserveStockCommand`, `CommitStockCommand`, `ReleaseStockCommand`, `AuthorizePaymentCommand`, `CapturePaymentCommand`, `VoidPaymentCommand`, `RefundPaymentCommand`, `ConfirmOrderCommand`, `CancelOrderCommand`, `CreateShipmentCommand`, `CancelShipmentCommand`). Zero shared-lib changes.
- **Dispatch**: no MediatR. Event consumers take handler via constructor injection, call `HandleAsync(...)` directly. Handlers `internal sealed`, one public async method.
- **Slice DI**: each slice exposes `AddXxxSlice(this IServiceCollection)`; event-driven slices internally call `AddEventHandler<TEvent, THandler>()` from `ECommerce.Shared.Infrastructure.EventBus`.
- **Saga transition runner — new Domain abstraction**: `ISagaTransitionRunner<TState, TEvent>` in `Domain/Abstractions/`. Surface:
  - `Task RunAsync(string sagaCorrelationId, TEvent trigger, Func<TState, TEvent, TransitionResult<TState>> transitionFn, CancellationToken ct)` — loads state via `ISagaInstanceStore`, applies pure transition, persists new snapshot + `SagaTransition` row, enqueues each result command via `IOutboxStore.AddOutboxEvent(command)`, all in one EF transaction.
  - `BeginCompensation(...)` overload for operator-driven abort + reaper-driven timeout escalation; takes explicit origin step.
  - EF implementations `EfOrderSagaTransitionRunner` + `EfRefundSagaTransitionRunner` in `Infrastructure/Data/EntityFramework/`. Consume `ISagaInstanceStore`, the `Outbox` unit-of-work, and `SagaTelemetry`.
- **Outbox / command emission seam**: **no `IIntegrationMap<,>` + `DomainEventOutboxInterceptor` seam introduced.** Saga emits concrete commands directly from `OrderSagaTransitionResult.Commands` / `RefundSagaTransitionResult.Commands`. There is no `Translate(...)` switch in `SagaContext` today and no smell to dissolve. Matches Inventory + Shipping; diverges from Order + Payment.
- **Read path**: operator read slices (`GetSaga`, `ListSagas`) project directly from `SagaContext` to `OperatorSagaResponses` (bypass `ISagaInstanceStore` and saga aggregates).
- **Write path** (event-driven reply slices + Operator `AbortSaga`): one-line `await runner.RunAsync(sagaCorrelationId, trigger, OrderSagaStateMachine.Transition, ct)` (or `RefundSagaStateMachine.Transition`, or `runner.BeginCompensation(...)` for `AbortSaga`).
- **Dual subscription for `PaymentRefundedEvent`**: two slices register as `IEventHandler<PaymentRefundedEvent>` — `Features/OrderSaga/PaymentRefunded/` AND `Features/RefundSaga/PaymentRefunded/`. `ECommerce.Shared` event bus dispatches to both. Each handler loads its own saga state by id (`OrderSagaState` by `evt.OrderId`; `RefundSagaState` by `evt.RefundId`) and no-ops if message not its saga. One extra store lookup per refund vs today's router; preserves "one inbound trigger per slice" and avoids slice-to-slice references.
- **Cross-slice rule**: duplicate first, extract on third. NetArchTest forbids `Features.<X>.*` ↔ `Features.<Y>.*` for any distinct two-level slice paths (`OrderSaga.StockReserved` and `OrderSaga.PaymentAuthorized` are distinct).
- **Divergences from prior pilots** to honor:
  1. **Two-level namespace nesting** under `Features/` (`Features.OrderSaga.<Trigger>`, `Features.RefundSaga.<Trigger>`, `Features.Operator.<Action>`). New to this pilot — prior pilots flat. Justified by two saga aggregates coexisting in one service.
  2. **`ISagaTransitionRunner<TState, TEvent>` Domain abstraction** new to saga. Encapsulates the saga-specific lifecycle (load → pure transition → persist with transition row → outbox-publish commands in one EF transaction). No prior pilot needed this — saga is the only orchestrator service.
  3. **Reply-processor fan-out routers dissolved** (`OrderSagaReplyProcessor`, `RefundSagaReplyProcessor` deleted at end of Phases 6b and 7 respectively). Their dispatch logic dissolves into per-slice handlers; their shared persistence loop moves into `EfOrderSagaTransitionRunner` + `EfRefundSagaTransitionRunner`.
  4. **No `IIntegrationMap<,>` + `DomainEventOutboxInterceptor` seam** (matches Inventory + Shipping; diverges from Order + Payment).
  5. **Dual-subscription convention** for `PaymentRefundedEvent` (two slices, each looks up its saga, no-ops if not its own). Only place in monorepo where a single integration event is consumed by two slices that must both act on it.
  6. **Reaper as `Infrastructure/Reaper/` hosted service** mirroring Shipping's `Infrastructure/Carriers/CarrierPollingService`. No `Features/<Saga>/TimeoutEscalation/` slice — reaper does not present an inbound trigger from outside the service.
  7. **No HTTP write endpoint outside `Features/Operator/AbortSaga/`.** Saga is event-driven by design; the only write surface is the operator abort.
  8. **Eighth and final pilot.** After this lands, the follow-up ADR can promote VSA from per-service exception to default convention.
- **Composition**: composes ADR [0011](../adr/0011-order-cleanarch-vsa-pilot.md) by reference (no new ADR). Reuses [adding-a-new-slice.md](../runbooks/adding-a-new-slice.md) runbook unchanged. Root `CLAUDE.md` gets one new "Saga service exception" paragraph + a line noting saga is the eighth and final pilot.
- **`GET /health`**: stays in `Program.cs` (one-line `MapPlatformHealthChecks`). No `Features/Health/` slice — matches Inventory/Auth/Shipping/Payment.
- **Rollout**: 13 staged commits on `refactor/saga-vsa`, each green. Single PR for review. Pre-commit hook gates every commit (no `--no-verify`, no validation deferral).
- **Critical files to modify**:
  - `saga-microservice/Saga.Service/Endpoints/OperatorSagaEndpoints.cs` (~3 routes, dissolved by Phase 8)
  - `saga-microservice/Saga.Service/Endpoints/InternalOutboxEndpoints.cs` (relocated Phase 4)
  - `saga-microservice/Saga.Service/Infrastructure/Data/EntityFramework/SagaContext.cs` (becomes persistence-only Phase 3)
  - `saga-microservice/Saga.Service/Infrastructure/Reaper/SagaReaperService.cs` (rerouted through `ISagaTransitionRunner.BeginCompensation` Phase 5)
  - `saga-microservice/Saga.Service/Infrastructure/Reaper/OrderSagaTimeoutScheduler.cs` (unchanged location; namespace touched Phase 4)
  - `saga-microservice/Saga.Service/Models/*` (10 types relocated Phase 2b)
  - `saga-microservice/Saga.Service/StateMachines/*` (6 types relocated Phase 2b into `Domain/{OrderSaga,RefundSaga}/`)
  - `saga-microservice/Saga.Service/IntegrationEvents/*Event.cs` (15 payloads relocated Phase 2a)
  - `saga-microservice/Saga.Service/IntegrationEvents/EventHandlers/*` (17 handlers + 2 reply processors dissolved Phases 5/6a/6b/7)
  - `saga-microservice/Saga.Service/Observability/SagaTelemetry.cs` (relocated Phase 4)
  - `saga-microservice/Saga.Service/ApiModels/OperatorSagaResponses.cs` (split per operator slice Phase 8; duplicate first)
  - `saga-microservice/Saga.Service/Program.cs` (becomes slice manifest by Phase 8)
  - `saga-microservice/Saga.Tests/Api/*` (relocated Phase 9)
  - `saga-microservice/Saga.Tests/Domain/*` (kept; namespace touched Phase 9)
  - `saga-microservice/Saga.Tests/EndToEnd/*` (kept verbatim)
- **Critical files to copy/mirror** (prior pilots, do not modify):
  - `payment-microservice/Payment.Tests/Architecture/LayoutTests.cs` — closest prior-art NetArchTest layout (most recent pilot)
  - `payment-microservice/Payment.Tests/Architecture/LayoutAnalyzerTests.cs` — analyzer test shape
  - `payment-microservice/Payment.Service.LayoutAnalyzer/LayoutAnalyzer.cs` — analyzer skeleton + diagnostic IDs (rename `PAYLAY***` → `SAGLAY***`)
  - `shipping-microservice/Shipping.Service/Infrastructure/Carriers/CarrierPollingService.cs` — closest prior-art hosted-service-in-Infrastructure shape (reaper mirror)
  - `inventory-microservice/Inventory.Service/Features/<Slice>/<Slice>SliceExtensions.cs` — slice DI extension shape from a pilot that also skipped `IIntegrationMap<,>`
  - `payment-microservice/Payment.Service/Program.cs` — slice-manifest shape (most recent pilot)

---

## Phase 1: Scaffold NetArchTest + LayoutAnalyzer (rules off)

**User stories**: 16, 17, 30.

### What to build

Add new `Saga.Service.LayoutAnalyzer` csproj (copy Payment analyzer skeleton, rename diagnostic IDs `PAYLAY***` → `SAGLAY***`, rules empty / disabled). Wire as `Analyzer` ProjectReference from `Saga.Service.csproj`. Add `Saga.Tests/Architecture/LayoutTests.cs` + `Saga.Tests/Architecture/LayoutAnalyzerTests.cs` with every test marked `[Fact(Skip="enabled in Phase 10")]`. No production code changes.

### Acceptance criteria

- [ ] `dotnet build saga-microservice` green
- [ ] `dotnet test saga-microservice/Saga.Tests` green (skipped tests count > 0)
- [ ] `dotnet format --verify-no-changes` green
- [ ] Commit: `refactor(saga): Phase 1 scaffold NetArchTest + LayoutAnalyzer`

---

## Phase 2a: Move inbound integration event payloads to `Contracts/Integration/InboundEvents/`

**User stories**: 19, 22, 25.

### What to build

Move the 15 inbound payload classes (`OrderCreatedEvent`, `StockReservedEvent`, `StockReservationFailedEvent`, `PaymentAuthorizedEvent`, `PaymentFailedEvent`, `OrderConfirmedEvent`, `StockCommittedEvent`, `ShipmentCreatedEvent`, `ShipmentFailedEvent`, `StockReleasedEvent`, `PaymentVoidedEvent`, `PaymentRefundedEvent`, `OrderCancelledEvent`, `ShipmentCancelledEvent`, `RefundRequestedEvent`) from `IntegrationEvents/*.cs` (top-level) to `Contracts/Integration/InboundEvents/`. Rename namespace to `Saga.Service.Contracts.Integration.InboundEvents`. Leave the 17 `EventHandlers/*.cs` files + 2 reply processors in `IntegrationEvents/EventHandlers/` for now (Phases 5/6a/6b/7 dissolve them); fix their `using`s. Fix all other `using`s across `StateMachines/`, `Infrastructure/`, tests. Wire-deserialization shape preserved — broker subscription still resolves these types from the same `Event` base.

### Acceptance criteria

- [ ] Build green
- [ ] Full `dotnet test saga-microservice/Saga.Tests` green
- [ ] `dotnet format --verify-no-changes` green
- [ ] 15 payload files now under `Contracts/Integration/InboundEvents/`
- [ ] Commit: `refactor(saga): Phase 2a move inbound event payloads to Contracts/`

---

## Phase 2b: Move domain to `Domain/{OrderSaga,RefundSaga,}/` (incl. state machines)

**User stories**: 6, 19.

### What to build

Move all 10 `Models/*` types + 6 `StateMachines/*` types into `Domain/` per topology:

- `Domain/OrderSaga/` ← `Models/OrderSagaState.cs`, `Models/OrderSagaStep.cs`, `Models/OrderSagaTimeoutOptions.cs`, `StateMachines/OrderSagaStateMachine.cs`, `StateMachines/OrderSagaStateSnapshot.cs`, `StateMachines/OrderSagaTransitionResult.cs`. Namespace `Saga.Service.Domain.OrderSaga`.
- `Domain/RefundSaga/` ← `Models/RefundSagaState.cs`, `Models/RefundSagaStep.cs`, `StateMachines/RefundSagaStateMachine.cs`, `StateMachines/RefundSagaStateSnapshot.cs`, `StateMachines/RefundSagaTransitionResult.cs`. Namespace `Saga.Service.Domain.RefundSaga`.
- `Domain/` (top) ← `Models/SagaInstance.cs`, `Models/SagaTransition.cs`, `Models/SagaStatus.cs`, `Models/SagaTriggerKind.cs`, `Models/SagaReaperOptions.cs`. Namespace `Saga.Service.Domain`.

Pure relocation + namespace rename. State-machine `static` classes stay `static`. Transition function signatures unchanged. Update all consumer `using`s (`Endpoints/`, `Infrastructure/`, `IntegrationEvents/EventHandlers/`, `Infrastructure/Reaper/`, `Saga.Tests/`).

### Acceptance criteria

- [ ] Build green
- [ ] Full `dotnet test saga-microservice/Saga.Tests` green — `OrderSagaStateMachineTests`, `RefundSagaStateMachineTests`, `OrderSagaCompensationTests` continue passing on pure-function inputs
- [ ] `Models/` folder deleted
- [ ] `StateMachines/` folder deleted
- [ ] Commit: `refactor(saga): Phase 2b move domain to Domain/{OrderSaga,RefundSaga,}/`

---

## Phase 3: Extract `ISagaInstanceStore` + `EfSagaInstanceStore` to split `SagaContext`

**User stories**: 12, 19.

### What to build

Declare `Domain/Abstractions/ISagaInstanceStore.cs` under namespace `Saga.Service.Domain.Abstractions`. Surface covers what today's `SagaContext` exposes for saga lookup + persistence (load `OrderSagaState` by saga id, load `OrderSagaState` by correlation id / `OrderId`, load `RefundSagaState` by saga id / `RefundId`, save changes, transactional execute). Concrete surface mirrors prior-art `IPaymentStore` / `IOrderStore`.

Introduce `Infrastructure/Data/EntityFramework/EfSagaInstanceStore.cs` (impl). Constructor takes `(SagaContext ctx)`; methods delegate to `ctx.OrderSagas` / `ctx.RefundSagas` / `ctx.SagaInstances`. Register `services.AddScoped<ISagaInstanceStore, EfSagaInstanceStore>()`. Update reply processors + reaper to depend on `ISagaInstanceStore` instead of `SagaContext` directly. `SagaContext` retains only `DbContext` base + `DbSet<>` declarations + `OnModelCreating` after this phase.

Single commit (split would leave a "two impls coexist" bisect state).

### Acceptance criteria

- [ ] Build green
- [ ] Full `Saga.Tests` green (manual — hook only runs Basket tests)
- [ ] `SagaContext` contains no saga-lookup helper methods after the phase
- [ ] `ISagaInstanceStore` in `Domain/Abstractions/`; `EfSagaInstanceStore` in `Infrastructure/Data/EntityFramework/`
- [ ] Reply processors + reaper compile against `ISagaInstanceStore`
- [ ] Commit: `refactor(saga): Phase 3 extract ISagaInstanceStore + EfSagaInstanceStore`

---

## Phase 4: Relocate `SagaTelemetry`; reaper folder cleanup; `InternalOutboxEndpoints` → `Infrastructure/Outbox/`

**User stories**: 14, 15, 21, 31.

### What to build

Three relocations in one commit:

1. Move `Observability/SagaTelemetry.cs` → `Infrastructure/Observability/SagaTelemetry.cs` under namespace `Saga.Service.Infrastructure.Observability`. Delete the empty top-level `Observability/` folder. Update consumer `using`s (`Program.cs`, reaper, reply processors). Activity-source name + meter name + counter/histogram names preserved verbatim.
2. Reaper folder already at `Infrastructure/Reaper/`; rename namespaces (`Saga.Service.Infrastructure.Reaper`) and fix usings touched by Phase 2b. No file moves.
3. Move `Endpoints/InternalOutboxEndpoints.cs` → `Infrastructure/Outbox/InternalOutboxEndpoints.cs` under namespace `Saga.Service.Infrastructure.Outbox`. `Endpoints/` still contains `OperatorSagaEndpoints.cs` (Phase 8 dissolves it). `RequireService` policy gate preserved on `/internal/outbox/failed`.

### Acceptance criteria

- [ ] Build green
- [ ] Full `Saga.Tests` green
- [ ] Prometheus exporter still emits identical `SagaTelemetry` counters / histograms
- [ ] `Observability/` top-level folder deleted
- [ ] `Infrastructure/Outbox/InternalOutboxEndpoints.cs` present
- [ ] `Endpoints/` folder still contains `OperatorSagaEndpoints.cs` only
- [ ] Commit: `refactor(saga): Phase 4 relocate SagaTelemetry + InternalOutboxEndpoints; reaper namespace cleanup`

---

## Phase 5: Extract `ISagaTransitionRunner<TState, TEvent>` + EF impls; reply processors delegate

**User stories**: 7, 8, 13, 14.

### What to build

**Behavior-touching phase but kept narrow.** Strict in-commit migration order; single commit:

1. Declare `Domain/Abstractions/ISagaTransitionRunner.cs` under namespace `Saga.Service.Domain.Abstractions`. Generic over `TState`, `TEvent`. Surface:
   - `Task RunAsync(string sagaCorrelationId, TEvent trigger, Func<TState, TEvent, TransitionResult<TState>> transitionFn, CancellationToken ct)`.
   - Saga-specific `BeginCompensation(string sagaCorrelationId, OrderSagaStep origin, TEvent trigger, CancellationToken ct)` overload (separate runner pair or saga-typed overload — concrete shape decided in extraction; mirrors today's `OrderSagaStateMachine.BeginCompensation` entry).
   - `TransitionResult<TState>` shape: `(TState NextState, IReadOnlyList<Event> Commands, bool Changed)` — pulled from existing `OrderSagaTransitionResult` / `RefundSagaTransitionResult` field shape.
2. Implement `Infrastructure/Data/EntityFramework/EfOrderSagaTransitionRunner.cs : ISagaTransitionRunner<OrderSagaState, Event>` and `EfRefundSagaTransitionRunner.cs : ISagaTransitionRunner<RefundSagaState, Event>`. Each:
   - Loads saga state via `ISagaInstanceStore.LoadOrderSagaByCorrelationId(...)` (or refund equivalent).
   - Applies the supplied transition fn to a `*SagaStateSnapshot` projected from the loaded state.
   - On `Changed: true`: writes the new snapshot back to the aggregate, appends a `SagaTransition` row (trigger type name, previous step, next step, last-step-result, timestamp, correlation metadata), and enqueues each `Command` via `IOutboxStore.AddOutboxEvent(command)`. All in one `SagaContext` `SaveChangesAsync` transaction (mirrors current `OrderSagaReplyProcessor` persistence shape — extract, don't redesign).
   - On `Changed: false`: no writes (mirrors today's `OrderSagaReplyProcessor` `NoChange` short-circuit).
   - Emits `SagaTelemetry` activity + counters identical to today.
3. **Reply processors delegate (intermediate dual path)**: `OrderSagaReplyProcessor.Process(Event)` reduces to `runner.RunAsync(correlationId, evt, OrderSagaStateMachine.Transition, ct)`. Same for `RefundSagaReplyProcessor`. Existing event handlers (`StockReservedEventHandler`, etc.) still call the reply processor — no slice carve-out yet. Build green between sub-steps.
4. Reaper (`SagaReaperService`) reroutes through `runner.BeginCompensation(...)` instead of poking `SagaContext` + `OrderSagaStateMachine.BeginCompensation` directly. `OrderSagaTimeoutScheduler` unchanged.
5. Register both runners in `Program.cs` (will move into slice extensions Phase 6a).

Single commit. Bisect of intermediate sub-steps would land misleading "runners exist but unused" / "processors gutted but slices not yet" states.

### Acceptance criteria

- [ ] Build green
- [ ] Full `Saga.Tests` green including: `OrderSagaOrchestratorTests`, `RefundSagaOrchestratorTests`, `OperatorEndpointTests`, `SagaReaperServiceTests`, `OrderSagaEndToEndTests`. Outbox commands produced are byte-identical (payload + `CausationId` + `SagaId` + `CorrelationId`).
- [ ] `OrderSagaReplyProcessor` body reduced to one-line `runner.RunAsync` call (still alive)
- [ ] `RefundSagaReplyProcessor` body reduced to one-line `runner.RunAsync` call (still alive)
- [ ] `SagaReaperService` calls `runner.BeginCompensation` not `SagaContext` directly
- [ ] `EfOrderSagaTransitionRunner` + `EfRefundSagaTransitionRunner` present in `Infrastructure/Data/EntityFramework/`
- [ ] Commit: `refactor(saga): Phase 5 extract ISagaTransitionRunner + EF impls; processors delegate`

---

## Phase 6a: Extract OrderSaga forward-path slices (9 slices)

**User stories**: 1, 2, 3, 4.

### What to build

Carve 9 slices under `Features/OrderSaga/` for the forward-path triggers:

- `OrderCreated/` — saga start. Handler loads-or-creates `OrderSagaState`, calls `runner.RunAsync(evt.OrderId, evt, OrderSagaStateMachine.Transition, ct)`.
- `StockReserved/`, `StockReservationFailed/`, `PaymentAuthorized/`, `PaymentFailed/`, `OrderConfirmed/`, `StockCommitted/`, `ShipmentCreated/`, `ShipmentFailed/` — each a one-line `runner.RunAsync(...)` call.

Per slice:
- `Handler.cs` (`internal sealed class StockReservedHandler : IEventHandler<StockReservedEvent>` etc.); constructor injects `ISagaTransitionRunner<OrderSagaState, Event>`.
- `<Slice>SliceExtensions.cs` exposes `AddXxxSlice(this IServiceCollection)` that calls `AddEventHandler<TEvent, THandler>()`.
- Namespace `Saga.Service.Features.OrderSaga.<Trigger>`.

Wire each into `Program.cs`. **For each slice extracted, delete the corresponding branch in `OrderSagaReplyProcessor` `Process` switch + delete the corresponding `*EventHandler.cs` under `IntegrationEvents/EventHandlers/`** (the slice's `Handler.cs` is now the registered `IEventHandler<TEvent>`). `OrderSagaReplyProcessor` shrinks but remains alive (still handles compensation-reply branches — Phase 6b finishes it).

Remove the 9 `AddEventHandler<...>()` calls from `Program.cs` for these triggers (now done by slice extensions).

### Acceptance criteria

- [ ] Build green
- [ ] `OrderSagaOrchestratorTests` happy-path cases green
- [ ] `OrderSagaEndToEndTests` happy-path green
- [ ] Outbox commands byte-identical (payload + `CausationId` + `SagaId` + `CorrelationId`) to pre-refactor
- [ ] Full `Saga.Tests` green
- [ ] 9 folders under `Features/OrderSaga/` each contain handler + slice extension
- [ ] 9 `*EventHandler.cs` files deleted from `IntegrationEvents/EventHandlers/`
- [ ] `OrderSagaReplyProcessor` shrunk to compensation-reply branches only (5 remaining)
- [ ] Commit: `refactor(saga): Phase 6a extract OrderSaga forward-path slices`

---

## Phase 6b: Extract OrderSaga compensation-reply slices (5 slices); delete `OrderSagaReplyProcessor`

**User stories**: 1, 8, 11.

### What to build

Carve 5 slices under `Features/OrderSaga/` for the compensation-reply triggers:

- `StockReleased/`, `PaymentVoided/`, `PaymentRefunded/`, `OrderCancelled/`, `ShipmentCancelled/`.

Same per-slice shape as Phase 6a — one-line `runner.RunAsync(...)` call with `OrderSagaStateMachine.Transition`.

**`PaymentRefunded/` slice note**: this slice loads `OrderSagaState` by `evt.OrderId`. If `OrderSagaState` not found (i.e. message belongs to a `RefundSaga` instance), handler returns early without throwing. The companion `Features/RefundSaga/PaymentRefunded/` slice extracted in Phase 7 does the symmetric thing for `RefundSagaState`. Dual-subscription convention: both registered, both invoked, each no-ops if not its own saga.

Wire each into `Program.cs`. Delete corresponding 5 branches from `OrderSagaReplyProcessor` `Process` switch + delete 5 `*EventHandler.cs` files. `OrderSagaReplyProcessor.Process` switch is now empty → **delete `OrderSagaReplyProcessor.cs`**. Remove the 5 `AddEventHandler<...>()` calls + the `AddScoped<OrderSagaReplyProcessor>()` registration from `Program.cs`.

### Acceptance criteria

- [ ] Build green
- [ ] `OrderSagaCompensationTests` green
- [ ] `OrderSagaEndToEndTests` compensation paths green (StockReservationFailed → none; PaymentFailed → ReleaseStock; ShipmentFailed → RefundPayment/CancelOrder/ReleaseStock sequence)
- [ ] Full `Saga.Tests` green
- [ ] 5 additional folders under `Features/OrderSaga/` (14 total)
- [ ] 5 additional `*EventHandler.cs` files deleted from `IntegrationEvents/EventHandlers/`
- [ ] **`OrderSagaReplyProcessor.cs` deleted**
- [ ] Commit: `refactor(saga): Phase 6b extract OrderSaga compensation slices + delete OrderSagaReplyProcessor`

---

## Phase 7: Extract RefundSaga slices (`RefundRequested`, `PaymentRefunded`); delete `RefundSagaReplyProcessor`

**User stories**: 2, 11.

### What to build

Carve 2 slices under `Features/RefundSaga/`:

- `RefundRequested/` — saga start. Handler creates `RefundSagaState`, calls `runner.RunAsync(evt.RefundId, evt, RefundSagaStateMachine.Transition, ct)`.
- `PaymentRefunded/` — reply. Handler loads `RefundSagaState` by `evt.RefundId`; if not found, returns early (message belonged to `OrderSagaState`, handled by `Features/OrderSaga/PaymentRefunded/` from Phase 6b). On found: `runner.RunAsync(...)`.

Per-slice shape identical to Phase 6a/6b.

**Dual-subscription verification**: both `Features/OrderSaga/PaymentRefunded/AddSlice(...)` AND `Features/RefundSaga/PaymentRefunded/AddSlice(...)` register as `IEventHandler<PaymentRefundedEvent>`. `ECommerce.Shared` event bus dispatches to both. Each no-ops if not its saga. Verify wiring through test in Phase 9.

Delete `RefundSagaReplyProcessor` `Process` switch branches; **delete `RefundSagaReplyProcessor.cs`**. Delete corresponding 2 `*EventHandler.cs` files (`RefundRequestedEventHandler.cs`, the refund-side `PaymentRefundedEventHandler.cs` if separately filed; otherwise the 6b deletion already removed it). Remove 2 `AddEventHandler<...>()` + the `AddScoped<RefundSagaReplyProcessor>()` from `Program.cs`.

After this phase: `IntegrationEvents/EventHandlers/` folder is empty → delete it. `IntegrationEvents/` (top-level, after Phase 2a + this) is empty → delete it.

### Acceptance criteria

- [ ] Build green
- [ ] `RefundSagaOrchestratorTests` green
- [ ] Full `Saga.Tests` green
- [ ] `Features/RefundSaga/RefundRequested/` + `Features/RefundSaga/PaymentRefunded/` each contain handler + slice extension
- [ ] **`RefundSagaReplyProcessor.cs` deleted**
- [ ] `IntegrationEvents/EventHandlers/` folder deleted
- [ ] `IntegrationEvents/` folder deleted (top-level)
- [ ] Commit: `refactor(saga): Phase 7 extract RefundSaga slices + delete RefundSagaReplyProcessor`

---

## Phase 8: Extract Operator slices; retire `OperatorSagaEndpoints`; `Program.cs` → manifest

**User stories**: 5, 9, 10, 21, 33.

### What to build

Carve 3 slices under `Features/Operator/`:

- `GetSaga/` (read) — endpoint `GET /operator/api/sagas/{id}` projects directly from `SagaContext` to a `GetSagaResponse` record (duplicated from `OperatorSagaResponses` — duplicate first, rule-of-three).
- `ListSagas/` (read) — endpoint `GET /operator/api/sagas` projects to `ListSagasResponse` record (duplicated).
- `AbortSaga/` (write) — endpoint `POST /operator/api/sagas/{id}/abort` resolves `ISagaTransitionRunner` for the appropriate saga type and calls `BeginCompensation(...)`. Preserves today's behavior: loads `SagaInstance` to determine saga type, then dispatches to the correct runner. (Saga-type-dispatch helper duplicated per-slice if needed; runner pair is two separate DI types.)

Per slice:
- `Endpoint.cs` (Minimal API delegate, `TypedResults.*`).
- `Handler.cs` (`internal sealed`); constructor injects `SagaContext` (reads) or `ISagaInstanceStore` + `ISagaTransitionRunner` pair (write).
- Slice-local response DTOs (duplicated from `OperatorSagaResponses` — `ApiModels/OperatorSagaResponses.cs` deleted at end of phase since both reads + the write now own their own DTOs).
- `<Slice>SliceExtensions.cs` registers handler.
- Namespace `Saga.Service.Features.Operator.<Action>`.

Auth (Bearer + operator policy), routes, response shapes, status codes preserved byte-for-byte.

Delete `Endpoints/OperatorSagaEndpoints.cs`. Delete the empty `Endpoints/` folder (Phase 4 already moved `InternalOutboxEndpoints`). Delete `ApiModels/OperatorSagaResponses.cs` + empty `ApiModels/` folder.

Reshape `Program.cs` into a slice manifest: chained `AddXxxSlice()` registration block (~19 slices) + `app.MapXxxSlice()` mapping block + `app.RegisterInternalOutboxEndpoints()` + `MapPlatformHealthChecks` + shared-lib infra (`AddSqlServerDatastore`, `AddOutbox`, `AddPlatformEventBus`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddPlatformOpenApi`, `AddJwtAuthentication`, `AddRequireServicePolicy`) retained as-is + `AddSingleton<OrderSagaTimeoutScheduler>` + `AddHostedService<SagaReaperService>` + `Configure<SagaReaperOptions>` + `Configure<OrderSagaTimeoutOptions>` + `AddSingleton(TimeProvider.System)`. Zero per-handler `AddScoped<...>` or per-event `AddEventHandler<...>` calls — all in slice extensions.

### Acceptance criteria

- [ ] Build green
- [ ] `OperatorEndpointTests` green (read + abort routes byte-identical behavior + auth)
- [ ] Full `Saga.Tests` green
- [ ] `Features/Operator/GetSaga/`, `Features/Operator/ListSagas/`, `Features/Operator/AbortSaga/` each contain endpoint + handler + slice extension + slice-local DTOs
- [ ] `Endpoints/` folder deleted
- [ ] `ApiModels/` folder deleted
- [ ] `Program.cs` reads as manifest (~19 `AddXxxSlice()` chained + mappings + ops + shared-lib infra)
- [ ] `Program.cs` zero per-handler `AddScoped<...Handler>` and zero per-event `AddEventHandler<...>` calls
- [ ] Commit: `refactor(saga): Phase 8 extract Operator slices + Program.cs manifest`

---

## Phase 9: Reshape `Saga.Tests` to mirror slices; add runner + dual-subscription tests

**User stories**: 20, 24.

### What to build

Move existing test classes from `Saga.Tests/Api/` per PRD Testing Decisions:

- `OrderSagaOrchestratorTests.cs` → split per slice into `Saga.Tests/Features/OrderSaga/<Trigger>/EndpointTests.cs`. Test bodies unchanged beyond import paths.
- `RefundSagaOrchestratorTests.cs` → split into `Saga.Tests/Features/RefundSaga/RefundRequested/EndpointTests.cs` + `Saga.Tests/Features/RefundSaga/PaymentRefunded/EndpointTests.cs`.
- `OperatorEndpointTests.cs` → split into `Saga.Tests/Features/Operator/{GetSaga,ListSagas,AbortSaga}/EndpointTests.cs`.
- `SagaObservabilityTests.cs` → keep top-level (cross-cutting; reads `SagaTelemetry`).
- `Saga.Tests/Domain/*` (state-machine + reaper + handler unit tests) → kept verbatim, namespace touched only.
- `Saga.Tests/EndToEnd/*` → kept verbatim. Fixture + happy-path / compensation-path coverage unchanged.
- `Saga.Tests/Authentication/*` → kept top-level.

**Add new tests** to pin saga-specific seams:

- `Architecture/LayoutTests.cs` + `Architecture/LayoutAnalyzerTests.cs` — already added Phase 1 with skipped tests; enabled Phase 10. No change here.
- `Saga.Tests/Infrastructure/Data/EntityFramework/EfOrderSagaTransitionRunnerTests.cs` — integration tests covering: given inbound trigger, runner loads state, applies pure transition, persists new snapshot + `SagaTransition` row, enqueues all returned commands into outbox in one transaction; on `Changed: false`, no persistence/outbox writes; on `BeginCompensation`, correct compensation sequence initiated.
- `Saga.Tests/Infrastructure/Data/EntityFramework/EfRefundSagaTransitionRunnerTests.cs` — same shape for refund saga.
- `Saga.Tests/Features/OrderSaga/PaymentRefunded/DualSubscriptionTests.cs` (or co-located in either saga's slice) — integration test asserting both `OrderSaga` and `RefundSaga` PaymentRefunded handlers are invoked when `PaymentRefundedEvent` is published; each no-ops if its saga not found; identical outbox emission as pre-refactor in each scenario (Order-only refund, Refund-only refund, both-sagas-running concurrent test).

Keep `SagaWebApplicationFactory.cs` + `SagaEndToEndWebApplicationFactory.cs` + `appsettings.Tests.json` + `xunit.runner.json` at project root. Delete the emptied `Api/` test folder. Namespace updates only on relocated tests — zero behavior change on the pre-existing ones.

### Acceptance criteria

- [ ] Build green
- [ ] Full `dotnet test saga-microservice/Saga.Tests` green (zero behavior diff on pre-existing tests)
- [ ] `Saga.Tests/Api/` folder deleted
- [ ] `Saga.Tests/Features/` folder count = slice count (19)
- [ ] `EfOrderSagaTransitionRunnerTests.cs` + `EfRefundSagaTransitionRunnerTests.cs` present and green
- [ ] Dual-subscription test present and green
- [ ] `Saga.Tests/Domain/`, `Saga.Tests/EndToEnd/`, `Saga.Tests/Authentication/` untouched
- [ ] Commit: `refactor(saga): Phase 9 reshape Saga.Tests into Features/ + add runner/dual-sub tests`

---

## Phase 10: Enable NetArchTest + LayoutAnalyzer rules

**User stories**: 16, 17, 30.

### What to build

Unskip `LayoutTests.cs` + `LayoutAnalyzerTests.cs`. Fill in NetArchTest rules:

- `Saga.Service.Domain.*` must not depend on `Saga.Service.Infrastructure.*`, `Saga.Service.Features.*`, `Saga.Service.Contracts.*`.
- `Saga.Service.Features.<X>.*` must not depend on `Saga.Service.Features.<Y>.*` for distinct two-level slice paths (e.g. `Saga.Service.Features.OrderSaga.StockReserved` and `Saga.Service.Features.OrderSaga.PaymentAuthorized` are distinct; `Saga.Service.Features.OrderSaga.PaymentRefunded` and `Saga.Service.Features.RefundSaga.PaymentRefunded` are distinct).
- `Saga.Service.Infrastructure.*` may reference only `Domain` + `Contracts` (+ allowed shared-lib namespaces).
- `Saga.Service.Contracts.*` must not reference anything internal beyond `Saga.Service.Contracts.*`.

Promote `Saga.Service.LayoutAnalyzer` diagnostics from hidden to error severity (`.editorconfig` or analyzer manifest). Fill in analyzer banned-namespace / banned-symbol diagnostics mirroring `Payment.Service.LayoutAnalyzer` with `SAGLAY***` IDs. Two-level slice-identity twist: analyzer must treat first two namespace segments after `Saga.Service.Features.` as the slice identity (`OrderSaga.StockReserved` vs `OrderSaga.PaymentAuthorized` distinct; not just `OrderSaga` vs `OrderSaga`).

### Acceptance criteria

- [ ] `dotnet build saga-microservice` green (analyzer doesn't fire on existing code — proves refactor satisfies rules)
- [ ] Full `Saga.Tests` green including all unskipped Architecture tests
- [ ] `LayoutAnalyzerTests.cs` proves each rule fires on synthetic violation input (including two-level slice cross-reference)
- [ ] Commit: `refactor(saga): Phase 10 enforce layout boundaries`

---

## Phase 11: Docs — root `CLAUDE.md` Saga exception paragraph + eighth/final note

**User stories**: 27, 28, 29.

### What to build

Add one paragraph to root `CLAUDE.md` under the existing pilot-exception block (after the Payment paragraph), matching the Order/Product/Basket/Auth/Inventory/Shipping/Payment style:

> **Saga service exception** — eighth and final Clean Architecture + Vertical Slices pilot, same layout as Order/Product/Basket/Inventory/Shipping/Payment: `Features/<Saga>/<Trigger>/`, `Domain/{OrderSaga,RefundSaga,}/`, `Contracts/Integration/InboundEvents/`, `Infrastructure/`. Boundaries enforced by NetArchTest (`Saga.Tests/Architecture/LayoutTests.cs`) and the Roslyn `Saga.Service.LayoutAnalyzer`. Composes ADR [0011](docs/adr/0011-order-cleanarch-vsa-pilot.md) by reference (no new ADR); reuses the [adding-a-new-slice.md](docs/runbooks/adding-a-new-slice.md) runbook unchanged. **Diverges from Order/Product/Basket/Auth/Inventory/Shipping/Payment: two-level `Features/<Saga>/<Trigger>/` namespace nesting (new — prior pilots flat; justified by two saga aggregates coexisting in one service); `ISagaTransitionRunner<TState, TEvent>` Domain abstraction new to saga (encapsulates load → pure transition → persist with `SagaTransition` row → outbox-publish commands in one EF transaction); `OrderSagaReplyProcessor` + `RefundSagaReplyProcessor` fan-out routers deleted (dispatch dissolved into per-slice handlers; shared persistence loop lifted into `EfOrderSagaTransitionRunner` + `EfRefundSagaTransitionRunner`); no `IIntegrationMap<,>` + `DomainEventOutboxInterceptor` seam (saga emits commands directly from state-machine result — no `Translate(...)` smell to dissolve; matches Inventory/Shipping); dual-subscription convention for `PaymentRefundedEvent` (two slices register, each loads its own saga by id, no-ops if not its own — only place in monorepo where one integration event drives two slices that must both act on it); reaper as `Infrastructure/Reaper/` hosted service mirroring Shipping's `Infrastructure/Carriers/CarrierPollingService` (no `Features/<Saga>/TimeoutEscalation/` slice — reaper is internal scheduling, not an inbound trigger); no HTTP write endpoint outside `Features/Operator/AbortSaga/` (saga is event-driven by design); saga commands (`ReserveStockCommand`/`AuthorizePaymentCommand`/etc.) consumed from `ECommerce.Shared.IntegrationEvents.Commands`, not owned in local `Contracts/Integration/`.** Saga is the eighth and final pilot — every service in the monorepo is now on the Clean Architecture + Vertical Slices layout. Follow-up ADR can promote the convention from "per-service pilot exception" to "default service shape".

No new ADR. No runbook changes.

### Acceptance criteria

- [ ] `CLAUDE.md` contains the new paragraph; existing pilot paragraphs unchanged
- [ ] `dotnet format --verify-no-changes` green
- [ ] Markdown links resolve (ADR 0011 + adding-a-new-slice)
- [ ] Commit: `refactor(saga): Phase 11 docs root CLAUDE.md Saga exception`

---

## Verification (end-to-end, after Phase 11)

Run each from a clean `dotnet restore`:

1. **Format + build + test full Saga stack**
   ```bash
   find saga-microservice -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
   cd saga-microservice && dotnet restore && dotnet format --verify-no-changes && dotnet build && dotnet test
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
   Then via Bruno/curl against `http://localhost:8008` and the broker UI:
   - Publish `OrderCreatedEvent` → verify `ReserveStockCommand` enqueued with `CausationId == OrderCreatedEvent.Id`, `SagaId` populated.
   - Publish `StockReservedEvent` reply → verify `AuthorizePaymentCommand` enqueued; saga step `StockReserved → PaymentAuthorizing`.
   - Drive full happy path through `OrderConfirmed`, `StockCommitted`, `ShipmentCreated` → verify saga reaches `Completed` and all 5 commands emitted in order.
   - Drive compensation paths: publish `StockReservationFailedEvent` → saga `Failed`, no compensation commands. Publish `PaymentFailedEvent` → `ReleaseStockCommand` enqueued, saga `Compensating`. Publish `ShipmentFailedEvent` → `RefundPaymentCommand` → `PaymentRefundedEvent` reply → `CancelOrderCommand` → `OrderCancelledEvent` reply → `ReleaseStockCommand` → `StockReleasedEvent` reply → saga `Failed` (compensation complete).
   - Refund saga: publish `RefundRequestedEvent` → `RefundPaymentCommand` enqueued. Publish `PaymentRefundedEvent` → verify **both** `Features/OrderSaga/PaymentRefunded/` and `Features/RefundSaga/PaymentRefunded/` handlers are invoked (logs show two store lookups; only the matching saga progresses).
   - Reaper-driven escalation: stall a saga past `OrderSagaTimeoutOptions` threshold → verify `SagaReaperService` calls `runner.BeginCompensation(...)` and emits the right compensation command.
   - Operator endpoints: `GET /operator/api/sagas` (Bearer + operator policy) → 200 list. `GET /operator/api/sagas/{id}` → 200 detail / 404. `POST /operator/api/sagas/{id}/abort` → 202; verify compensation begins.
   - `GET /internal/outbox/failed` with user token → 403; with service token → 200.
   - `GET /health` → 200.

4. **Boundary regression check**
   Add a deliberate violation locally (e.g. `Domain/OrderSaga/OrderSagaStateMachine.cs` adds `using Saga.Service.Infrastructure.Data.EntityFramework;`); confirm:
   - `dotnet build` fails with `SAGLAY***` analyzer diagnostic
   - `dotnet test Saga.Tests --filter LayoutTests` fails the matching NetArchTest assertion
   Revert. Also try a two-level slice cross-reference (`Features/OrderSaga/StockReserved/Handler.cs` adds `using Saga.Service.Features.OrderSaga.PaymentAuthorized;`); both analyzer + NetArchTest fail. Revert.

5. **Dual-subscription regression check**
   In a stack run, publish a `PaymentRefundedEvent` matching an `OrderSagaState` (compensation path) and confirm only the Order saga progresses; publish one matching a `RefundSagaState` and confirm only the Refund saga progresses. Confirm Saga logs show both handlers invoked in each case (one acts, the other no-ops).

6. **Reply-processor regression check**
   Grep entire `saga-microservice/Saga.Service/` for `OrderSagaReplyProcessor` and `RefundSagaReplyProcessor`. Zero matches expected (both deleted by end of Phases 6b and 7).

7. **DLQ poller still ingests Saga failures**
   In a stack run, induce a poison-message scenario and confirm the API gateway DLQ poller still persists Saga rows from `/internal/outbox/failed`.

8. **Telemetry parity**
   Hit Prometheus `/metrics` endpoint on Saga and confirm `SagaTelemetry` activity-source / meter / counter / histogram names + tags emit identical to pre-refactor.

9. **PR open + bisect spot-check**
   Open single PR `refactor/saga-vsa` → `main`. `git bisect` any 3 random commits in the branch range and confirm each builds + tests green in isolation.

## Phases needing manual `dotnet test saga-microservice/Saga.Tests` before commit

Pre-commit hook only runs Basket tests. Run Saga tests locally before staging on every phase, but pay especially close attention to behavior-touching phases:

- **Phase 3** — `ISagaInstanceStore` split (mechanical surface across reply processors + reaper)
- **Phase 5** — `ISagaTransitionRunner` extraction (**largest behavior-touching phase of the pilot before slicing**; saga reply parity + correlation propagation + `SagaTransition` row shape all hinge on this; reaper rerouting through `BeginCompensation` is a second surface)
- **Phase 6a** — OrderSaga forward-path slices (9-slice extraction; outbox command emission must stay byte-identical including `CausationId`/`SagaId`)
- **Phase 6b** — OrderSaga compensation slices + `OrderSagaReplyProcessor` deletion (compensation sequencing is the most behavior-rich path; verify `OrderSagaCompensationTests` + `OrderSagaEndToEndTests` compensation cases)
- **Phase 7** — RefundSaga slices + `RefundSagaReplyProcessor` deletion; dual-subscription convention activates here (both `OrderSaga.PaymentRefunded` and `RefundSaga.PaymentRefunded` slices live)
- **Phase 8** — Operator `AbortSaga` slice drives compensation through runner (operator-driven path was previously inlined; now flows through new abstraction)
- **Phase 9** — new runner + dual-subscription tests added; convention-pinning tests for the pilot
- **Phase 10** — rule enablement (NetArchTest only fires under `dotnet test`)

If hook fails with `MSB3248`: clean `bin`/`obj` → `dotnet restore --force` → rerun hook (per root `CLAUDE.md` sandbox policy). Do not `--no-verify`, do not defer validation. If still failing, **STOP and hand off to user — do not commit**.
