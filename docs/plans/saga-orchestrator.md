# Plan: Saga Orchestrator (replaces event-driven order saga coordination)

> Source PRD: `docs/prd/PRD-Saga-Orchestrator.md`
> Companion ADR (to be filed in Phase 1): `docs/adr/0010-saga-orchestrator-supersedes-choreography.md` (supersedes ADR-0008).
> Tracking issue: #119.

## Context

The current order saga (Order → Inventory → Payment → Shipping) is event-coordinated: each service reacts to integration events from its peers. End-to-end state lives implicitly across four services' DBs (`OrderStatus`, `ReservationStatus`, `PaymentStatus`, `ShipmentStatus`), and reconstructing a stuck saga requires querying each service plus correlating OpenTelemetry spans and DLQ rows. Operating this since ADR-0008 (2026-05-06) surfaced concrete pain: incident #113 (order-not-cancelled), repeat hardening PRDs (`PRD-Smoke-Test-Saga-Hardening`), and StockItem aggregate work (#55, #115–#118) all touched saga-adjacent code. There is no single home for the saga state machine, no overdue detection, no per-step retry, and compensation is distributed across services.

This plan introduces `saga-microservice` at port 8008 as an orchestrator that owns saga *instance* state and drives Order → Inventory → Payment → Shipping by sending commands and listening for reply events. Each participant service keeps publishing its existing integration events, so the wider system is unaffected and a feature flag can route only canary orders through the orchestrator while in-flight orders finish via the existing event-driven path. The RefundSaga is included in this wave per PRD scope. The intended outcome: one readable home for saga state, explicit compensation, operator-visible saga instances, overdue detection, and a uniform template for the next saga.

## Architectural decisions

Durable across all phases:

- **New service**: `saga-microservice/` at port `8008`, project `Saga.Service`, test project `Saga.Tests`, own `.slnx`. Per-service layout matches existing services (`Endpoints/`, `ApiModels/`, `Models/`, `Infrastructure/Data/`, `IntegrationEvents/`, `Migrations/`). `Program.cs` ends with `public partial class Program { }`.
- **Composition root**: `AddSqlServerDatastore`, `AddOutbox` + `ApplyOutboxMigrations` (Dev), `AddPlatformEventBus`, `AddPlatformEventPublisher`, `AddPlatformSubscriberService`, `AddEventHandler<TEvent,THandler>`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddPlatformOpenApi`, `AddJwtAuthentication`. `TimeProvider.System` registered as singleton (repo's existing clock pattern — see `shipping-microservice/Shipping.Service/Program.cs:46`).
- **Database**: SQL Server, database name `Saga`. Tables:
  - `SagaInstance(SagaId PK, SagaType, CurrentStep, Status, CorrelationId, Version (rowversion), CreatedAt, UpdatedAt, NextTimeoutAt nullable, RetryCount)`.
  - `OrderSagaState(SagaId PK FK, OrderId, ReservationId nullable, PaymentId nullable, ShipmentId nullable, LastStepResult nullable)`.
  - `RefundSagaState(SagaId PK FK, OrderId, PaymentId, ShipmentId nullable, RefundAmount, Currency)`.
  - `SagaTransition(Id PK, SagaId FK, FromStep, ToStep, Timestamp, TriggerMessageId, TriggerKind (Command|Event|Timeout|OperatorAction), Error nullable)`.
- **Status enum**: `Running | Completed | Failed | Compensating | Compensated | Aborted`.
- **OrderSagaStep**: `Started, StockReserving, PaymentAuthorizing, OrderConfirming, StockCommitting, ShipmentCreating, Completed` plus mirrored `Compensating*` variants for the compensation path.
- **RefundSagaStep**: `Started, PaymentRefunding, ShipmentCancellingOrReturning, Completed` plus mirrored compensation variants.
- **Concurrency**: optimistic via `SagaInstance.Version`. Reply handlers load row, advance state, save with version check.
- **State machine**: one pure class per saga type (`OrderSagaStateMachine`, `RefundSagaStateMachine`). Function shape `(state, event) → (newState, commands[])`. No DB, no clock, no event bus. Returned commands dispatched by the caller inside `IOutboxUnitOfWork.ExecuteAsync` so saga state + audit + outbox publish are atomic.
- **Command base class**: new `Command : Event` in `ECommerce.Shared.Infrastructure.EventBus` adds `Guid CausationId`, `Guid SagaId` to the existing `Id` (MessageId) + `CorrelationId?` on `Event`. Reply events get `CausationId` populated by the participant handler from the triggering command's `MessageId`. Add `CausationId?` and `SagaId?` to `Event` base (nullable, so the legacy event-driven path is unaffected) rather than forking. Bump `ECommerce.Shared` version after change and republish to `local-nuget-packages/`.
- **Idempotency**: orchestrator dedupes replies by `(SagaId, CausationId)` against `SagaInstance.CurrentStep`. Out-of-step replies logged and dropped.
- **Commands per service**:
  - Inventory: `ReserveStockCommand`, `CommitStockCommand`, `ReleaseStockCommand`.
  - Payment: `AuthorizePaymentCommand`, `CapturePaymentCommand`, `VoidPaymentCommand`, `RefundPaymentCommand`.
  - Shipping: `CreateShipmentCommand`, `CancelShipmentCommand`, `ReturnShipmentCommand`.
  - Order: `ConfirmOrderCommand`, `CancelOrderCommand`.
- **Reply events**: existing integration events (`StockReservedEvent`, `StockReservationFailedEvent`, `PaymentAuthorizedEvent`, `PaymentCapturedEvent`, `PaymentFailedEvent`, `PaymentRefundedEvent`, `PaymentVoidedDomainEvent`, `StockCommittedEvent`, `ShipmentCreatedEvent`, `ShipmentCancelledEvent`, `OrderConfirmedEvent`, `OrderCancelledEvent`). Participants set `CausationId = command.MessageId` and `SagaId = command.SagaId` when a command triggered the work. Legacy event-driven saga handlers on peer services continue to subscribe during strangler.
- **Outbox unit of work**: every saga state advance wrapped in `IOutboxUnitOfWork.ExecuteAsync` (Payment's PR #114 pattern). State + `SagaTransition` row + outgoing command publish atomic.
- **Reaper**: `SagaReaperService : BackgroundService` mirrors `OutboxBackgroundService` shape — `PeriodicTimer` with default interval `30s` (`Saga:Reaper:IntervalInSeconds`). Picks `Status=Running AND NextTimeoutAt <= UtcNow`, re-dispatches in-flight command (idempotent on consumer); after `Saga:Reaper:MaxRetries` (default 3) transitions to `Compensating`. Per-step timeouts in config: `Saga:OrderSaga:StockReservingTimeout=00:01:00`, etc. Uses `TimeProvider.GetUtcNow()` for determinism in tests.
- **Feature flag** (full shape):
  - `Saga:Orchestrator:Enabled` — bool, default `false`.
  - `Saga:Orchestrator:AllowList` — Guid[] of order ids that always orchestrate.
  - `Saga:Orchestrator:Percentage` — int 0–100, deterministic bucket from `OrderId` GUID hash.
  - Inclusion decided once at `OrderCreatedEvent` arrival in saga service. If excluded, orchestrator no-ops and the event-driven saga path proceeds unchanged. Each order owned by exactly one path for its lifetime.
- **Compensation matrix** (Order saga, last-completed-step → reverse-step sequence):
  - After `StockReserved`: `ReleaseStockCommand`.
  - After `PaymentAuthorized`: `VoidPaymentCommand` → `ReleaseStockCommand`.
  - After `OrderConfirmed`: `VoidPaymentCommand` → `ReleaseStockCommand` → `CancelOrderCommand`.
  - After `StockCommitted`: `RefundPaymentCommand` → `CancelOrderCommand`. (Committed stock cannot be released; refund is the correct compensation.)
  - After `ShipmentCreated`: `CancelShipmentCommand` → `RefundPaymentCommand` → `CancelOrderCommand`.
- **Authentication**: saga service uses `AddJwtAuthentication` + `RequireService` policy on `/internal/*` and `/operator/api/*`. User-facing operator endpoints accept user JWT.
- **Routes**:
  - User-bound: none (saga is internal — triggered by events).
  - Internal: `GET /internal/sagas/failed` (mirror outbox pattern, picked up by gateway DLQ poller if needed).
  - Operator API: `GET /operator/api/sagas`, `GET /operator/api/sagas/{id}`, `POST /operator/api/sagas/{id}/retry`, `POST /operator/api/sagas/{id}/abort`.
  - Operator HTML: `GET /operator/sagas` (server-rendered, gateway-style).
- **DLQ + replay**: commands and reply events flow through existing `ecommerce-exchange` → `ecommerce-dlq` pipeline unchanged. Gateway DLQ poller picks them up without changes because all new event types inherit `Event`. Replay re-publishes to original queue; idempotency on consumer side keeps it safe.
- **ADR**: file [`docs/adr/0010-saga-orchestrator-supersedes-choreography.md`](../adr/0010-saga-orchestrator-supersedes-choreography.md) (status `Accepted`, `Supersedes: ADR-0008`) in Phase 1. Update ADR-0008 status header to `Superseded by ADR-0010`.
- **CLAUDE.md**: update service table to add `saga 8008 SQL`.
- **docker-compose.yml + bicep**: add saga service block (port 8008, db `Saga`, RabbitMQ env, Outbox env), new SQL DB resource in bicep.
- **azure-pipelines.yml**: cloned from `order-microservice/azure-pipelines.yml`, swap `serviceName: sagaservice`, `solutionPath: saga-microservice/Saga.Service.slnx`, secrets `db-saga-secret`.

## Test strategy (durable)

- **Pure state-machine tests** in `Saga.Tests/Domain/` mirror `inventory-microservice/Inventory.Tests/Domain/StockItemTests.cs` style: bare `[Fact]`s, no DI, no DB, `Given_When_Then` naming preserved (`CA1707` suppressed at repo level). One test per transition (happy + every failure branch + every compensation matrix entry + idempotent replies + unknown-event-in-step no-op).
- **Command handler integration tests** per participant service mirror `inventory-microservice/Inventory.Tests/Api/ReleaseReservationsTests.cs` style: `WebApplicationFactory<Program>`, real EF, in-process bus, `TestAuthHandler`, assert DB state + emitted reply event.
- **Saga orchestrator integration tests** in `Saga.Tests/Api/`: `WebApplicationFactory<Program>` with in-process bus. One test per major transition trigger (flag-on opens saga, flag-off no-ops, allowlist routes, percentage bucket determinism).
- **Reaper unit tests** with injected `TimeProvider` (use `FakeTimeProvider` test double; advance time, assert overdue picks up only `Running` rows past `NextTimeoutAt`, retry within N re-dispatches command, exceeding N transitions to `Compensating`).
- **Operator endpoint tests** in `Saga.Tests/Api/OperatorEndpointTests.cs`: list/detail/retry/abort, `RequireService` returns 401 unauthenticated.
- **End-to-end (both harnesses)**:
  - **Extend existing smoke**: add scenarios to `scripts/local-smoke-test.ps1` + Bruno collection at `qa/bruno/` and runbooks in `docs/qa/scenarios/`. Cover happy path + each failure branch + overdue reaper + operator abort.
  - **New Testcontainers fixture** in `Saga.Tests/EndToEnd/`: spins SQL + RabbitMQ + relevant services. Runs in CI per service pipeline; one happy-path + one failure-branch test minimum, expanded over time.

## Critical files / utilities to reuse

- Outbox UoW: `shared-libs/ECommerce.Shared/Infrastructure/Outbox/IOutboxUnitOfWork.cs`, `OutboxStartupExtensions.cs`, `OutboxUnitOfWork.cs`. Example consumer: `payment-microservice/Payment.Service/Infrastructure/Data/EntityFramework/PaymentContext.cs:60-80` (PR #114).
- Outbox loop: `shared-libs/ECommerce.Shared/Infrastructure/Outbox/OutboxBackgroundService.cs:11-84` — copy `PeriodicTimer` pattern for `SagaReaperService`.
- Event base: `shared-libs/ECommerce.Shared/Infrastructure/EventBus/Event.cs` — extend with nullable `CausationId`, `SagaId`.
- Event bus: `IEventBus`, `IEventHandler<TEvent>`, `AddEventHandler<TEvent,THandler>` (`EventBusHandlerExtensions.cs:8`).
- Auth policy: `RequireService` at `AuthenticationPolicies.cs:19-22`.
- DLQ pipeline: `api-gateway/ApiGateway/Operator/OperatorModule.cs` — saga operator endpoints mirror shape.
- Order aggregate: `order-microservice/Order.Service/Models/Order.cs:48-68` (`TryConfirm`, `TryCancel`).
- Inventory store: `inventory-microservice/Inventory.Service/Infrastructure/Data/IInventoryStore.cs:21-25` (`Reserve`, `CommitReservations`, `ReleaseReservations`).
- Payment aggregate: `payment-microservice/Payment.Service/Models/Payment.cs:38-132` (`Authorize`, `Capture`, `Void`, `Refund`).
- Shipping aggregate: `shipping-microservice/Shipping.Service/Models/Shipment.cs:48-156` (`Create`, `TryCancel`).
- Test factory pattern: `inventory-microservice/Inventory.Tests/InventoryWebApplicationFactory.cs:15-88` (remove `RabbitMqHostedService`, swap auth, real EF) — clone shape for `SagaWebApplicationFactory`.
- Aggregate test style: `inventory-microservice/Inventory.Tests/Domain/StockItemTests.cs`.
- Test partial Program: confirm every existing service has `public partial class Program { }` at end of `Program.cs` (e.g. `inventory-microservice/Inventory.Service/Program.cs:72`).
- Clock: `TimeProvider.System` registered in services (e.g. `shipping-microservice/Shipping.Service/Program.cs:46`).
- ADR template: existing `docs/adr/0008-*.md` (status header format).

---

## Phase 1: Service skeleton + ADR-0010

**User stories**: 27, 30.

### What to build

Bootstrap `saga-microservice/` at port 8008 with full composition root (no saga behavior yet). Service starts, runs SQL migrations, exposes `/health/ready` + `/health/live`, swagger at `/swagger`, Prometheus at `/metrics`. Add to `docker-compose.yml`, clone `azure-pipelines.yml`, update bicep for new AKS deployment + Azure SQL DB. Append `saga 8008 SQL` to the service table in `CLAUDE.md`. File `docs/adr/0010-saga-orchestrator-supersedes-choreography.md` (status `Accepted`, supersedes ADR-0008) and update ADR-0008 status header.

### Acceptance criteria

- [ ] `cd saga-microservice && dotnet build` succeeds with `TreatWarningsAsErrors`.
- [ ] `cd saga-microservice && dotnet test` runs (zero tests fine).
- [ ] `docker compose up saga` starts the service and `curl localhost:8008/health/ready` returns 200.
- [ ] `Saga` DB created with `__EFMigrationsHistory` only (no domain tables yet — added Phase 2).
- [ ] `saga 8008 SQL` line present in `CLAUDE.md` service table.
- [ ] `docs/adr/0010-*.md` filed; ADR-0008 status updated.
- [ ] Bicep + `azure-pipelines.yml` present, validated by repo's existing pipeline conventions.

---

## Phase 2: First transition behind flag (StockReserving)

**User stories**: 8, 9, 15, 16, 23, 24, 26.

### What to build

End-to-end vertical slice for one step. Add `CausationId?` + `SagaId?` to `Event` base in `ECommerce.Shared`; bump version, repack to `local-nuget-packages/`. Migrate participant services to consume new shared lib version. Create `SagaInstance` + `OrderSagaState` + `SagaTransition` tables in `Saga` DB. Implement `OrderSagaStateMachine` with two transitions only: `Started → StockReserving`, and on `StockReservedEvent` reply: `StockReserving → StockReserved`. Saga service subscribes to `OrderCreatedEvent` and decides inclusion via `Saga:Orchestrator:Enabled` + `AllowList` + `Percentage`; on inclusion opens saga inside `IOutboxUnitOfWork.ExecuteAsync` (saga row + transition row + outgoing `ReserveStockCommand` published atomically). Inventory adds `ReserveStockCommand` handler that calls existing `IInventoryStore.Reserve` (same path as `OrderCreatedEventHandler`) and publishes `StockReservedEvent` / `StockReservationFailedEvent` with `CausationId = command.MessageId` and `SagaId = command.SagaId`. Saga handles the reply, advances state. No compensation yet — failure parks saga in `Failed` with operator-visible reason.

### Acceptance criteria

- [ ] `ECommerce.Shared` republished with new `Event` fields; consumers compile.
- [ ] Pure unit tests: `Started → StockReserving` on `OrderCreatedEvent`, `StockReserving → StockReserved` on `StockReservedEvent`, idempotent replay drops duplicate (`CausationId` already advanced past).
- [ ] Integration test (`Saga.Tests/Api/`): flag off → orchestrator no-ops; flag on + order in allowlist → saga opens; flag on + order in percentage bucket → saga opens; flag on + order excluded → no-op.
- [ ] Integration test (`Inventory.Tests/Api/`): `ReserveStockCommand` handler reserves stock and emits `StockReservedEvent` with `CausationId` set.
- [ ] Smoke run via `local-smoke-test.ps1`: `POST /orders` for an allowlisted customer; saga row reaches `StockReserved`; existing event-driven saga flow unchanged for non-allowlisted order.

---

## Phase 3: Full happy path forward

**User stories**: 5, 10, 11, 25.

### What to build

Extend `OrderSagaStateMachine` with transitions: `StockReserved → PaymentAuthorizing → PaymentAuthorized → OrderConfirming → OrderConfirmed → StockCommitting → StockCommitted → ShipmentCreating → ShipmentCreated → Completed`. Add command handlers on participant services calling existing aggregate methods (no new business logic): Payment handles `AuthorizePaymentCommand` and `CapturePaymentCommand`; Order handles `ConfirmOrderCommand`; Inventory handles `CommitStockCommand`; Shipping handles `CreateShipmentCommand`. Each handler emits its existing reply event with `CausationId` + `SagaId` propagated. Saga dedupes replies by `(SagaId, CausationId)` vs `CurrentStep`. Still no compensation — happy path only.

### Acceptance criteria

- [ ] Pure unit tests: one per forward transition; idempotent reply (same `CausationId`) is no-op.
- [ ] Integration test per new command handler in each participant service.
- [ ] Saga integration test: full happy path drives `Started → Completed`.
- [ ] Smoke: allowlisted order reaches `OrderStatus=Confirmed`, stock committed, payment captured, shipment created, saga `Status=Completed`.

---

## Phase 4: Compensation matrix

**User stories**: 6.

### What to build

Add reverse-direction command handlers: Inventory `ReleaseStockCommand`, Payment `VoidPaymentCommand` + `RefundPaymentCommand`, Order `CancelOrderCommand`, Shipping `CancelShipmentCommand`. Extend state machine: any failure reply transitions saga to `Compensating` and returns the reverse-step command sequence per the matrix above (`Architectural decisions`). Each reverse step dispatched, awaited, audited; final state is `Compensated`, or `Failed` if a reverse step itself fails (parked for operator action).

### Acceptance criteria

- [ ] Pure unit tests: one per matrix entry. Includes "compensation of compensation fails → `Failed`".
- [ ] Integration test per new reverse command handler.
- [ ] Saga integration test: inject `PaymentFailedEvent` mid-flow → saga compensates → stock released, order cancelled.
- [ ] Smoke: payment-decline scenario (`docs/qa/scenarios/03-payment-decline.md`) routed through orchestrator finishes with reservation released and order cancelled.

---

## Phase 5: Reaper + per-step timeouts

**User stories**: 14, 21.

### What to build

`SagaReaperService : BackgroundService` mirroring `OutboxBackgroundService` shape (`PeriodicTimer`, `Saga:Reaper:IntervalInSeconds` default 30). State machine sets `NextTimeoutAt = TimeProvider.GetUtcNow() + Saga:OrderSaga:<step>Timeout` when advancing into a step that has a configured timeout. Reaper picks `Running` rows past `NextTimeoutAt`, re-dispatches the in-flight command (consumer idempotency keeps it safe), increments `RetryCount`; after `Saga:Reaper:MaxRetries` (default 3) transitions saga to `Compensating`. Structured log + `saga_overdue_total` counter on each overdue pickup.

### Acceptance criteria

- [ ] Reaper unit test with `FakeTimeProvider`: advances time past `NextTimeoutAt`, asserts retry; after N retries asserts `Compensating`.
- [ ] Reaper picks only `Status=Running` rows (not `Completed`/`Failed`/`Compensated`).
- [ ] Smoke: kill Inventory after saga dispatches `ReserveStockCommand`; reaper retries; after N attempts saga moves to `Compensated`.

---

## Phase 6: Observability

**User stories**: 12, 13, 14.

### What to build

Register saga `Meter` ("saga-orchestrator") and instruments: `saga_started_total{type}`, `saga_completed_total{type}`, `saga_failed_total{type,reason}`, `saga_step_duration_seconds{type,step}` (histogram), `saga_overdue_total{type,step}`, `saga_compensation_total{type}`. Wire into `AddPlatformObservability` so Prometheus scraping picks them up. Every state-machine transition opens an `Activity` ("saga.transition") parented to incoming event's trace context, tagged with `saga.id`, `saga.type`, `saga.from_step`, `saga.to_step`. `CorrelationId` flows through commands and replies. Structured logs at every transition with `SagaId`, `SagaType`, `Step`, `MessageId`, `CausationId`.

### Acceptance criteria

- [ ] `curl localhost:8008/metrics` lists all six counters/histograms.
- [ ] Jaeger / OTLP trace shows saga transitions parented to triggering event; correlation id consistent end-to-end.
- [ ] Smoke: happy-path saga increments `saga_started_total` once, `saga_completed_total` once, populates histogram per step.

---

## Phase 7: Operator API + HTML

**User stories**: 1, 2, 3, 4, 28, 31.

### What to build

Endpoints under `/operator/api/sagas`, all gated by `RequireService`:

- `GET /operator/api/sagas` — list with filters (`type`, `status`, `overdue=true`).
- `GET /operator/api/sagas/{id}` — detail incl. full `SagaTransition` history.
- `POST /operator/api/sagas/{id}/retry` — re-dispatches in-flight command for current step.
- `POST /operator/api/sagas/{id}/abort` — forces `Compensating` and runs reverse-step sequence.

HTML page `GET /operator/sagas` — server-rendered, matches gateway DLQ operator page style.

### Acceptance criteria

- [ ] Endpoint tests (`Saga.Tests/Api/OperatorEndpointTests.cs`): list returns running sagas; detail returns transitions; retry re-dispatches; abort enters compensation; unauthenticated returns 401.
- [ ] Manual: operator HTML page renders with at least one in-flight + one completed saga, "Retry"/"Abort" buttons fire endpoints.
- [ ] Operator-abort scenario added to smoke runbook.

---

## Phase 8: DLQ verification + strangler runbook

**User stories**: 17, 29, 32.

### What to build

Verify (no code changes expected) that orchestrator commands and reply events flow through `ecommerce-exchange` → `ecommerce-dlq` unchanged and the gateway DLQ poller persists them with correct `OriginalQueue`/`Service`/`Origin` tagging. Add a regression test that injects a malformed `ReserveStockCommand`, asserts DLQ row appears in `dead_letter_messages`, replays it, and saga resumes. Write strangler runbook `docs/runbooks/saga-orchestrator-strangler.md`: half-on-flag failure mode, "what to do if an order is in the event-driven path but the orchestrator opened a saga for it", cutover criteria (orchestrator handles 100% of new orders for two weeks with zero manual operator intervention attributable to orchestrator path). Add explicit assertion in saga's `OrderCreatedEvent` handler that an order id is never orchestrated twice (idempotency on saga start).

### Acceptance criteria

- [ ] DLQ regression test passes (gateway poller picks up bad saga command, replay restores flow).
- [ ] Runbook filed; CLAUDE.md `Cross-service architecture` section links to it.
- [ ] Legacy event-driven saga handlers in Order (`PaymentAuthorizedEventHandler`, `PaymentFailedEventHandler`, `StockReservationFailedEventHandler`) confirmed unchanged and exercised in smoke for non-allowlisted orders.

---

## Phase 9: RefundSaga

**User stories**: 22.

### What to build

Order publishes `RefundRequestedEvent` when a customer initiates a refund (new endpoint or existing refund flow — chosen during implementation). `RefundSagaStateMachine`: `Started → PaymentRefunding → PaymentRefunded → ShipmentCancellingOrReturning → Completed`, plus compensation if refund succeeds but shipment action fails. Reuses `RefundPaymentCommand` (added Phase 4) and `CancelShipmentCommand` / new `ReturnShipmentCommand` on Shipping. Same flag (`Saga:Orchestrator:Enabled`) controls inclusion; refund follows the same allowlist/percentage scheme as Order saga. `RefundSagaState` table populated.

### Acceptance criteria

- [ ] Pure unit tests cover happy path + every failure/compensation branch.
- [ ] Integration test: `RefundRequestedEvent` opens saga → payment refunded → shipment cancelled/returned → `Completed`.
- [ ] Smoke: customer-initiated refund scenario added to `docs/qa/scenarios/`.

---

## Phase 10: End-to-end smoke + Testcontainers

**User stories**: 18, 19, 20.

### What to build

**Extend existing smoke** (`scripts/local-smoke-test.ps1`, Bruno collection `qa/bruno/`, runbooks `docs/qa/scenarios/`):

- Happy-path orchestrated order (allowlisted).
- `StockReservationFailed` mid-flow.
- `PaymentFailed` mid-flow.
- `ShipmentFailed` after stock commit.
- Reaper overdue → compensation.
- Operator-abort → compensation.
- Refund saga happy path.

**New Testcontainers fixture** at `saga-microservice/Saga.Tests/EndToEnd/`: spins SQL + RabbitMQ (and the participant services as in-process `WebApplicationFactory<Program>` if practical, else as containers via existing Dockerfiles). At least one happy-path + one failure-branch test. Runs in `saga-microservice` Azure Pipelines stage.

### Acceptance criteria

- [ ] `pwsh scripts/local-smoke-test.ps1` passes all new scenarios against `docker compose up --build`.
- [ ] Testcontainers happy-path + payment-decline test pass locally via `dotnet test --filter Category=EndToEnd`.
- [ ] CI pipeline for saga-microservice runs both unit + EndToEnd categories on every push.
- [ ] Cutover criteria from Phase 8 runbook can be evaluated against the smoke output.

---

## Verification (end-to-end)

After all phases:

1. `docker compose up --build` boots all eight services incl. `saga` on 8008.
2. `pwsh scripts/local-smoke-test.ps1` exercises every scenario above and exits 0.
3. `dotnet test` per service passes; `Saga.Tests` covers domain (pure), API (`WebApplicationFactory<Program>`), Reaper (`FakeTimeProvider`), Operator endpoints, EndToEnd (Testcontainers).
4. `curl localhost:8008/metrics` lists all saga counters/histograms.
5. With `Saga:Orchestrator:Enabled=false` (default), every smoke scenario from `docs/qa/scenarios/` still passes — event-driven saga unchanged.
6. With `Saga:Orchestrator:Enabled=true` + `Percentage=100`, same smoke scenarios pass via orchestrator path.
7. Operator UI at `/operator/sagas` lists in-flight + completed sagas, retry and abort buttons function.
8. ADR-0010 filed, ADR-0008 marked `Superseded by ADR-0010`, CLAUDE.md service table updated, strangler runbook linked.
