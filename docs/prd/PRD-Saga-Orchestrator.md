# PRD: Replace order saga choreography with a central orchestrator service

> Supersedes ADR-0008. Companion ADR to be filed alongside the first implementation slice.

## Context

The order saga across Order → Inventory → Payment → Shipping currently uses choreography: each service reacts to integration events from peers, and the end-to-end flow is reconstructable only from wiki diagrams and OpenTelemetry traces. ADR-0008 (2026-05-06) accepted this trade. Operating the saga since then has surfaced concrete friction: incidents (#113 order-not-cancelled), repeat hardening PRDs (Smoke-Test-Saga-Hardening), and the StockItem aggregate work (#55, #115-#118) all touched saga-adjacent code and exposed how hard it is to know *where* a stuck order is, *why* it stopped, and *what step to retry*. The cost of choreography is now larger than the cost of an orchestrator, in particular because the repo already has the infra primitives needed (Outbox, IEventBus, DLQ replay) and because new sagas (refund, return) are coming. The intended outcome: one place that knows the entire saga, makes it debuggable, makes timeouts and compensation explicit, and makes new sagas additive instead of distributed.

## Problem Statement

As a developer or operator of this system, I cannot answer simple questions about a saga in flight without inspecting multiple services. When an order is stuck, the current state lives implicitly across `OrderStatus`, `ReservationStatus`, `PaymentStatus`, and `ShipmentStatus` enums, each owned by a different service and reachable only by querying that service's database. To understand *why* it is stuck, I have to correlate OpenTelemetry spans, the outbox table in each service, and the DLQ in the gateway. There is no overdue-step detection: a stuck saga stays stuck until someone notices. There is no way to retry a single step — the only operator action is a DLQ replay, which assumes the message itself failed (rather than, for example, a downstream service silently never publishing the next event). Compensation is distributed: each service owns its own compensating action, so changes to compensation semantics require touching every service. New saga participants subscribe to existing events and emit new ones, which works for additive cases but does not give the team a single contract surface to evolve. The current shape is documented in wiki diagrams that drift; the truth lives spread across `IntegrationEvents/EventHandlers/` directories in four services.

The asymmetry with how the rest of the codebase thinks about state machines makes this worse. The Order service has an explicit `Order` aggregate with guarded `TryConfirm`/`TryCancel` transitions; the StockItem aggregate now owns its lifecycle (PR #118). The cross-service saga, which is itself a state machine, has no such home — it is the only state machine in the repo whose transitions are not in any one place I can read or test.

## Solution

Introduce a new `saga-microservice` (port 8008) that owns saga *instance* state and explicitly drives the order flow by sending commands to Order, Inventory, Payment, and Shipping, and listening for reply events. The orchestrator is the only thing that knows the full order saga shape. Each participant service exposes its existing operations as command handlers (`ReserveStockCommand`, `AuthorizePaymentCommand`, `CommitStockCommand`, `CreateShipmentCommand`, plus the reverse `ReleaseStockCommand`, `VoidPaymentCommand`, `CancelShipmentCommand`) but **keeps publishing the existing integration events** so that the wider system (observability sinks, future consumers, the strangler fallback path) is unaffected.

The saga starts naturally from `OrderCreatedEvent` (which Order continues to publish exactly as today) — the orchestrator subscribes to it as the "start saga" trigger. Saga state lives in two tables in the saga service's DB: a generic `SagaInstance` header (id, type, current step, status, correlation id, created/updated, version) and a typed `OrderSagaState` payload (order id, reservation id, payment id, shipment id, last step result). A `PeriodicTimer`-driven `SagaReaperService` (mirroring `OutboxBackgroundService`) scans for overdue steps and either retries or drives compensation. Compensation is explicit: on any failure, the orchestrator computes the reverse-step sequence based on the last completed step and dispatches the appropriate `Release*`/`Void*`/`Cancel*` commands.

Migration is a strangler: a feature flag (`Saga:Orchestrator:Enabled`, with per-order-id allowlist or percentage rollout) routes new orders through the orchestrator while in-flight orders finish via the existing choreography. The choreography handlers stay in place during the transition; once the orchestrator path is stable, the choreography handlers are deprecated and removed in a follow-up PRD. A second `RefundSaga` is included in this PRD's scope because the refund flow ties payment refund + shipment return cancellation together and is the immediate driver after Order saga ships.

The win is one readable home for the order saga state machine, explicit compensation, operator-visible saga instances, overdue detection, and a uniform template for the next saga.

## User Stories

1. As an operator triaging a stuck order, I want to query `GET /sagas/{id}` and see the current step, last transition timestamp, and last error, so that I can answer "where is this order?" in one HTTP call instead of querying four services.
2. As an operator, I want an HTML page at `/operator/sagas` that lists in-flight sagas, their current step, and the time since their last transition, so that I can spot stuck sagas without writing SQL.
3. As an operator, I want to retry a single saga step (e.g. re-send `AuthorizePaymentCommand`) from the operator UI, so that I can recover from a transient downstream failure without touching the DLQ.
4. As an operator, I want to abort a saga (forcing compensation) from the operator UI, so that I can resolve a hung instance instead of waiting for an overdue reaper.
5. As a developer reading the order saga, I want the entire happy-path and every failure branch to be visible in one state-machine file in the saga service, so that I can understand the flow without grepping four services.
6. As a developer changing a compensation rule (e.g. "if shipment fails after payment capture, refund instead of void"), I want to change one method in the orchestrator, so that I do not have to coordinate changes across Payment + Shipping handlers.
7. As a developer adding a new saga (e.g. refund), I want a documented template (saga state class + state machine + reply handlers + reaper config) so that I can add it without reinventing the pattern.
8. As a developer of the Order service, I want my service to continue publishing `OrderCreatedEvent` as the saga start trigger, so that I do not need to know the saga exists.
9. As a developer of the Inventory service, I want to receive a `ReserveStockCommand` and reply with the existing `StockReservedEvent` or `StockReservationFailedEvent`, so that the work my service does is unchanged.
10. As a developer of the Payment service, I want command handlers for `AuthorizePaymentCommand`, `CapturePaymentCommand`, `VoidPaymentCommand`, `RefundPaymentCommand`, so that the orchestrator can drive payment lifecycle explicitly.
11. As a developer of the Shipping service, I want command handlers for `CreateShipmentCommand` and `CancelShipmentCommand`, so that the orchestrator can sequence shipment creation after stock commit deterministically.
12. As an SRE, I want `saga_started_total`, `saga_completed_total`, `saga_failed_total`, `saga_step_duration_seconds` (per-step histogram), and `saga_overdue_total` Prometheus counters, so that I can build dashboards and alerts on saga health.
13. As an SRE, I want every saga transition to emit a span linked to the saga's correlation id, so that I can trace a saga end-to-end in Jaeger/Tempo.
14. As an SRE, I want overdue saga instances to emit a structured log and increment `saga_overdue_total`, so that I can alert on hung workflows.
15. As an SRE rolling out the orchestrator, I want a feature flag `Saga:Orchestrator:Enabled` with per-order-id allowlist and percentage rollout, so that I can canary the new path safely.
16. As a developer rolling back, I want toggling the flag off to route subsequent orders through the existing choreography unchanged, so that rollback is one config change with no migration.
17. As a developer of the Order service, I want my existing choreography event handlers (`PaymentAuthorizedEventHandler`, `PaymentFailedEventHandler`, `StockReservationFailedEventHandler`) to remain functional during strangler, so that in-flight orders finish on the path they started on.
18. As a developer testing the saga state machine, I want pure-unit tests (no infra) for every transition, so that I can iterate on rules in milliseconds.
19. As a developer testing command handlers, I want per-service integration tests using `WebApplicationFactory<Program>` for each new command endpoint, so that the contract is verified at the service boundary.
20. As a developer testing end-to-end, I want a Docker Compose-driven happy-path test plus one test per failure branch (`StockReservationFailed`, `PaymentFailed`, shipment failure, overdue reaper, abort), so that the whole flow is exercised before merge.
21. As a developer testing the reaper, I want unit tests with an injected `IClock`, so that I can assert overdue detection deterministically.
22. As an operator handling refunds, I want a `RefundSaga` started by `RefundRequestedEvent` (published by Order on customer-initiated refund), which drives `RefundPaymentCommand` then optional `CancelShipmentCommand`/`ReturnShipmentCommand`, so that refund coordination has the same shape as the order saga.
23. As a developer of the saga service, I want saga state writes wrapped in `IOutboxUnitOfWork.ExecuteAsync` so that the outgoing command publish + saga state update are atomic, matching how Order/Inventory/Payment already use the outbox.
24. As a developer of any participant service, I want commands and reply events to carry `CorrelationId` and `CausationId` (the message id of the command they reply to), so that the orchestrator can match replies to in-flight saga steps without ambiguity.
25. As a developer adding idempotency, I want the orchestrator to deduplicate replies by `CausationId` against the saga's current step, so that retried replies do not double-advance the state machine.
26. As a developer of the Inventory service, I want my command handlers to use the same `IInventoryStore` aggregate methods that the existing event handlers use, so that there is no duplicate business logic across the choreography and orchestrator paths.
27. As a developer reading ADR-0008, I want a new ADR-00xx that supersedes it and links to this PRD, so that the architectural history is preserved and the current decision is unambiguous.
28. As a developer auditing a saga, I want a `SagaTransition` table that logs every state change with timestamp, from-step, to-step, command/event message id, and error (if any), so that I can reconstruct a saga's history forensically.
29. As an SRE replaying from DLQ, I want orchestrator-bound commands and replies to flow through the existing DLQ + replay pipeline, so that the operator workflow for failed messages is unchanged.
30. As a developer of the saga service, I want my service to compile under `net10.0` with `TreatWarningsAsErrors`, follow the per-service layout (`Endpoints/`, `ApiModels/`, `Models/`, `Infrastructure/Data/`, `IntegrationEvents/`, `Migrations/`), and use `ECommerce.Shared` extensions for outbox/event bus/observability/health/auth, so that the new service is indistinguishable in shape from existing services.
31. As an operator, I want `/internal/sagas/*` operator endpoints gated by the existing `RequireService` policy (Bearer + `scope=service`), so that the operator surface uses the same auth model as the gateway operator endpoints.
32. As a developer of the strangler period, I want a clear runbook for "what to do if the flag is half-on and an order is in choreography but the orchestrator started a saga for it", so that the dual-path failure mode has a documented resolution.

## Implementation Decisions

### Service shape

- **New service**: `saga-microservice` at port `8008`. Own `.slnx`, own `Saga.Service` project, own `Saga.Tests` test project. Composition root uses `AddSqlServerDatastore`, `AddOutbox` + `ApplyOutboxMigrations` (Dev), `AddPlatformEventBus`, `AddPlatformEventPublisher`, `AddPlatformSubscriberService`, `AddEventHandler<TEvent,THandler>`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddPlatformOpenApi`, `AddJwtAuthentication`. Per-service layout matches existing services.
- **Update**: `docker-compose.yml` adds saga service + SQL database. `azure-pipelines.yml` cloned from another service. Bicep updated for AKS namespace + Azure SQL DB. CLAUDE.md service table updated.
- **ADR**: new `docs/adr/00XX-saga-orchestrator-supersedes-choreography.md` with status `Accepted` and `Supersedes: ADR-0008`. ADR-0008 status updated to `Superseded by ADR-00XX`.

### Saga state

- **Hybrid storage**. Generic `SagaInstance(SagaId PK, SagaType, CurrentStep, Status, CorrelationId, Version (row version), CreatedAt, UpdatedAt, NextTimeoutAt nullable)` plus per-saga-type typed payload tables (`OrderSagaState`, `RefundSagaState`) FK'd to `SagaInstance` on `SagaId`. Generic header enables a single reaper, a single operator listing, and one set of metrics; typed payload keeps domain fields strongly typed.
- **Status enum**: `Running`, `Completed`, `Failed`, `Compensating`, `Compensated`, `Aborted`.
- **Step enum per saga type**: e.g. `OrderSagaStep { Started, StockReserving, PaymentAuthorizing, OrderConfirming, StockCommitting, ShipmentCreating, Completed }`. Compensation steps mirrored with a `Compensating` prefix.
- **Concurrency**: optimistic, via the `Version` column on `SagaInstance`. Reply handlers load the row, advance state, save with version check.

### State machine

- One class per saga type: `OrderSagaStateMachine`, `RefundSagaStateMachine`. Pure functions over `(currentState, event) -> (newState, commandsToDispatch[])`. No DB access, no clock dependency, no event bus dependency. Returned commands are dispatched by the calling reply handler inside an `IOutboxUnitOfWork.ExecuteAsync` envelope so that state advance + command publish are atomic.
- The state machine is the single source of truth for the saga shape. Adding/removing steps means editing this class plus its tests, nothing else.

### Commands and reply events

- **New command messages** in `ECommerce.Shared.IntegrationEvents.Commands` (or per-service `IntegrationEvents/Commands/` — chosen during implementation). Each command carries `MessageId`, `CorrelationId`, `CausationId`, `SagaId`, plus its typed payload.
- **Commands per service**:
  - Inventory: `ReserveStockCommand`, `CommitStockCommand`, `ReleaseStockCommand`.
  - Payment: `AuthorizePaymentCommand`, `CapturePaymentCommand`, `VoidPaymentCommand`, `RefundPaymentCommand`.
  - Shipping: `CreateShipmentCommand`, `CancelShipmentCommand`.
  - Order: `ConfirmOrderCommand`, `CancelOrderCommand` (replacing the orchestrator's reliance on the Order service producing `OrderConfirmedEvent` from internal logic).
- **Reply events**: existing integration events (`StockReservedEvent`, `StockReservationFailedEvent`, `PaymentAuthorizedEvent`, `PaymentFailedEvent`, `StockCommittedEvent`, `ShipmentCreatedEvent`, `ShipmentFailedEvent`, `OrderConfirmedEvent`, `OrderCancelledEvent`, etc.) continue to flow exactly as today, with the addition of `CausationId = command.MessageId` when a command triggered them. The orchestrator subscribes to these. The choreography handlers in peer services continue to subscribe to them too during the strangler.
- **Idempotency**: orchestrator deduplicates replies by `(SagaId, CausationId)` against the saga's current step. If a reply arrives for a step the saga already advanced past, it is logged and dropped.

### Reaper

- `SagaReaperService` is a `BackgroundService` with `PeriodicTimer` (configurable interval, default 30s, mirroring `OutboxBackgroundService`). Query: `SagaInstance` rows with `Status=Running` and `NextTimeoutAt <= UtcNow`. Each saga type defines its per-step timeout in config (`Saga:OrderSaga:StockReservingTimeout = 00:01:00`, etc.). Default action on timeout: re-dispatch the in-flight command (idempotency on the service side guarantees safety); after N retries (default 3), transition the saga to `Compensating`.
- `NextTimeoutAt` is set by the state machine when advancing into a step that has a timeout configured.

### Compensation

- Orchestrator-driven, explicit. On any reply event indicating failure (or on reaper escalation), the state machine transitions to `Compensating` and returns the reverse-step command sequence based on the last successful step. Each reverse-step command is dispatched, awaited, and the saga moves to `Compensated` (or `Failed` if compensation itself fails — in which case the saga is parked in `Failed` and visible to the operator UI for manual intervention).
- Reverse-step matrix for Order saga:
  - After `StockReserved`: `ReleaseStockCommand`.
  - After `PaymentAuthorized`: `VoidPaymentCommand`, then `ReleaseStockCommand`.
  - After `OrderConfirmed`: `VoidPaymentCommand`, then `ReleaseStockCommand`, then `CancelOrderCommand`.
  - After `StockCommitted`: `RefundPaymentCommand`, then `CancelOrderCommand`. (Stock that has been committed cannot be released; refund is the correct compensation.)
  - After `ShipmentCreated`: `CancelShipmentCommand`, then `RefundPaymentCommand`, then `CancelOrderCommand`.

### Strangler rollout

- Config flag `Saga:Orchestrator:Enabled` (default `false`). When `false`, the orchestrator subscribes to no events and acts as a no-op (the existing choreography handles everything).
- Per-order-id allowlist + percentage rollout: `Saga:Orchestrator:AllowList = [guid, guid]`, `Saga:Orchestrator:Percentage = 10`. Inclusion is decided at saga start: orchestrator's `OrderCreatedEvent` handler decides whether to open a saga or no-op. If it no-ops, choreography handlers proceed exactly as today.
- A given order is fully owned by exactly one path. There is no mid-flight handoff. The strangler runbook (story 32) covers the dual-path failure mode.
- Cutover criteria (target for the follow-up PRD removing choreography handlers): orchestrator handles 100% of new orders for two weeks with no manual operator intervention attributable to the orchestrator path.

### Audit log

- `SagaTransition(Id, SagaId FK, FromStep, ToStep, Timestamp, TriggerMessageId, TriggerKind (Command|Event|Timeout|OperatorAction), Error nullable)`. Written inside the same `IOutboxUnitOfWork.ExecuteAsync` envelope as the saga advance, so audit + state + outbox publish are atomic.

### Operator surface

- `GET /operator/api/sagas` — list in-flight sagas with filters (saga type, status, overdue).
- `GET /operator/api/sagas/{id}` — saga detail with full `SagaTransition` history.
- `POST /operator/api/sagas/{id}/retry` — re-dispatch the in-flight command for the current step.
- `POST /operator/api/sagas/{id}/abort` — force compensation.
- `GET /operator/sagas` — HTML page that calls the above APIs. Same style as the gateway DLQ operator page.
- All gated by Bearer + `RequireService` policy (already in `ECommerce.Shared`).

### Observability

- Metrics: `saga_started_total{type}`, `saga_completed_total{type}`, `saga_failed_total{type, reason}`, `saga_step_duration_seconds{type, step}` (histogram), `saga_overdue_total{type, step}`, `saga_compensation_total{type}`. Registered through `AddPlatformObservability`.
- Tracing: every state machine transition opens an `Activity` with `saga.id`, `saga.type`, `saga.from_step`, `saga.to_step`, parented to the incoming event's trace context. `CorrelationId` flows through commands and replies.
- Logging: structured logs at every transition with `SagaId`, `SagaType`, `Step`, `MessageId`, `CausationId`.

### Authentication

- Saga service uses `AddJwtAuthentication` (validates user JWTs via Auth's JWKS) for user-facing operator endpoints, plus `RequireService` policy on `/internal/*` and `/operator/api/*`.
- Saga service uses a `client_credentials` service token (issued by Auth via `POST /token`) when calling `/internal/*` on peer services if any synchronous calls become necessary. Commands themselves flow through the event bus and do not need a JWT.

### DLQ + replay

- Commands and reply events use the existing `ecommerce-exchange` → `ecommerce-dlq` pipeline (RabbitMQ) and native Azure Service Bus DLQ. The gateway's DLQ poller picks them up unchanged. Replay re-publishes to the original queue, which works without orchestrator-side changes because commands are idempotent at the consumer.

### Cross-service impact

- Order: adds `RefundRequestedEvent` publication on customer-initiated refund. Adds `ConfirmOrderCommand`/`CancelOrderCommand` handlers (during strangler, both the command handler and the existing event handlers route into the same `Order.TryConfirm`/`TryCancel` aggregate methods; no behavior change).
- Inventory: adds `ReserveStockCommand`/`CommitStockCommand`/`ReleaseStockCommand` handlers that call the existing `IInventoryStore` methods used by the event handlers. No new business logic. Same `IOutboxUnitOfWork.ExecuteAsync` envelope. Same reply event shapes.
- Payment: adds `AuthorizePaymentCommand`/`CapturePaymentCommand`/`VoidPaymentCommand`/`RefundPaymentCommand` handlers calling existing payment aggregate methods. Reply events unchanged.
- Shipping: adds `CreateShipmentCommand`/`CancelShipmentCommand` handlers calling existing shipment creation/cancellation logic. Reply events unchanged.

## Testing Decisions

A good test here tests *what the saga does in response to events*, not *how the orchestrator stores state*. For the state machine, the external surface is "given a current state and an incoming event, what is the new state and what commands are emitted". For command handlers in participant services, the external surface is "given an incoming command, what side effects in the DB and what reply event". For end-to-end, the external surface is "given a `POST /orders`, eventually the saga reaches `Completed` and the order is confirmed, stock committed, payment captured, shipment created".

Modules to test:

1. **`OrderSagaStateMachine` and `RefundSagaStateMachine`** — pure unit tests in `Saga.Tests/Domain/`. Cover every transition: happy path (one test per step advance), every failure branch (StockReservationFailed at each possible state, PaymentFailed, ShipmentFailed), every compensation matrix entry, idempotent replies (CausationId already seen), and unknown-event-in-step (no-op + log). No DB, no event bus, no clock — `IClock` injected.
2. **`SagaReaperService`** — unit tests in `Saga.Tests/Domain/` with an injected `IClock` and an in-memory `ISagaStore`. Cover: overdue detection picks up only `Running` sagas past `NextTimeoutAt`; retry within N attempts re-dispatches the current command; exceeding N attempts transitions to `Compensating`.
3. **Command handlers in each participant service** — integration tests using `WebApplicationFactory<Program>` per service:
   - `Inventory.Tests/Api/ReserveStockCommandHandlerTests.cs` (and the same for `Commit`/`Release`). Use the existing test infra (real EF context, in-process bus).
   - Same shape for Payment, Shipping, Order.
   - These tests should mirror the existing `Inventory.Tests/Api/ReleaseReservationsTests.cs` style and the existing `Inventory.Tests/Domain/StockItemTests.cs` style — explicit `Given_When_Then` names, real DB via the test factory pattern, no mocks of EF.
4. **Saga orchestrator integration** — `Saga.Tests/Api/` integration tests using `WebApplicationFactory<Program>` with an in-process event bus. Cover: orchestrator opens a saga on `OrderCreatedEvent` when flag is on; ignores it when flag is off; dispatches `ReserveStockCommand`; on `StockReservedEvent` advances to `PaymentAuthorizing`; etc. One test per major transition.
5. **End-to-end** — `Saga.Tests/EndToEnd/` Docker-Compose-driven (or a new test fixture that spins up all four services with Testcontainers for SQL and RabbitMQ). One happy-path test, one test per failure branch (StockReservationFailed at start, PaymentFailed mid-flow, ShipmentFailed late, overdue-reaper-compensates, operator-abort-compensates).
6. **Operator endpoint tests** — `Saga.Tests/Api/OperatorEndpointTests.cs`. Cover: list returns running sagas; detail returns transitions; retry re-dispatches command; abort transitions to `Compensating`; all gated by `RequireService` (unauthenticated returns 401).

Prior art to follow:

- `inventory-microservice/Inventory.Tests/Domain/StockItemTests.cs` — pure aggregate tests, `Given_When_Then` style, no infra.
- `inventory-microservice/Inventory.Tests/Api/ReleaseReservationsTests.cs` — endpoint integration via `WebApplicationFactory<Program>`.
- Existing saga smoke tests referenced in `PRD-Smoke-Test-Saga-Hardening.md` and `docs/wiki/QA-Scenarios/01-happy-path.md`, `03-payment-decline.md` — end-to-end shape.

## Out of Scope

- Removing the choreography event handlers. This PRD adds the orchestrator path and runs both in parallel via the strangler flag. A follow-up PRD removes the choreography handlers once orchestrator soak is complete.
- Migrating away from custom `IEventBus`/`IOutboxUnitOfWork` to MassTransit or NServiceBus. The orchestrator is built on existing primitives.
- BPMN-style visual modelling tools, no-code saga designers, or pluggable workflow engines (Temporal, Cadence, Azure Durable Functions). The state machine is hand-written C# in this repo.
- Sagas other than Order and Refund (e.g. dispute, partial-fulfillment, subscription renewal). Each new saga is its own PRD that follows the template established here.
- Delayed-message infrastructure (RabbitMQ delayed exchange plugin, Azure Service Bus scheduled enqueue). The reaper covers timeouts via polling; if reaper latency becomes a problem the delayed-message path can be added in a future PRD without changing the state machine contract.
- Cross-saga coordination (e.g. an Order saga that spawns a child Refund saga atomically). The Refund saga is a peer that starts from its own trigger event.
- Operator UI built on a frontend framework. The `/operator/sagas` page is a server-rendered HTML view matching the existing gateway operator page style.
- Per-tenant saga isolation, saga-level rate limiting, or saga prioritization queues. Not required at current scale.
- Migrating in-flight orders mid-saga from choreography to orchestrator. Each order is owned by exactly one path for its lifetime.

## Further Notes

- **Supersedes ADR-0008.** A new ADR (numbered at implementation time) records the orchestrator decision and links to this PRD. The new ADR's "Context" cites concrete operational pain (e.g. #113 order-not-cancelled, repeated saga-hardening PRDs) as the basis for revisiting the choreography choice.
- **Module depth check.** The deep, isolation-testable module is `OrderSagaStateMachine` / `RefundSagaStateMachine`: a small interface (`(state, event) → (newState, commands)`), a large amount of behavior, no infra dependencies, contract rarely changes once the saga shape is settled. Aligns with the codebase's emerging "deep aggregate" pattern (Order, StockItem).
- **Symmetry with existing state-machine work.** The orchestrator's state machines are intentionally the same shape as the StockItem aggregate (PR #115-#118): guarded transitions, returned side effects (commands here, movements there), pure-function core. New developers can map one onto the other.
- **Future PRDs unlocked by this work.** (a) Removing choreography handlers, (b) RefundSaga details if they grow beyond what this PRD covers, (c) Subscription/recurring-payment saga, (d) Delayed-message infra if reaper polling latency becomes a bottleneck.
