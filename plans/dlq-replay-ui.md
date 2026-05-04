# Plan: Dead-Letter Queue + Replay UI

> Source PRD: [docs/prd/PRD-DLQ-Replay-UI.md](../docs/prd/PRD-DLQ-Replay-UI.md) — GitHub issue [#36](https://github.com/daonhan/Microservices-in-.NET/issues/36)

## Architectural decisions

Durable decisions that apply across all phases:

- **Broker topology**: single shared dead-letter exchange `ecommerce-dlx` and queue `ecommerce-dlq`. Every consumer queue is declared `durable: true` with arguments `x-dead-letter-exchange = ecommerce-dlx` and a routing key encoding the source queue name. The existing fanout exchange `ecommerce-exchange` is unchanged for live publishing.
- **Ack model**: consumers switch from `autoAck: true` to manual ack. Handler success → `BasicAck`. Handler exception → bounded Polly retry (default 3, exponential backoff) → `BasicNack(requeue: false)`.
- **Replay semantics**: targeted. A replayed message is published through the **default exchange** with the routing key set to `OriginalQueue`, so only the originally-failing service's queue receives it. The fanout exchange is not used for replay.
- **Routes (API Gateway)**:
  - `GET    /operator/api/failures` — paged list with filters `service`, `eventType`, `status`, `from`, `to`, `origin` (`Consumer|Outbox`).
  - `GET    /operator/api/failures/{id}` — full detail (payload + stack trace + history).
  - `POST   /operator/api/failures/{id}/replay`
  - `POST   /operator/api/failures/replay-batch`
  - `POST   /operator/api/failures/{id}/discard` — body requires non-empty reason.
  - `GET    /operator` (and subroutes) — Blazor Server UI.
  - All gated by an authorization policy `RequireOperator` bound to the `operator` role claim.
  - Per-service: `GET /internal/outbox/failed`, locked behind a service-to-service auth policy; the gateway aggregates these to surface outbox failures.
- **Schema**:
  - Gateway DB, new table `dead_letter_messages` with columns: `id`, `event_type`, `routing_key`, `original_queue`, `payload` (json), `failure_reason`, `stack_trace`, `attempts`, `failed_at`, `status` (`Pending|Replayed|Discarded`), `replayed_at`, `replayed_by`, `discarded_at`, `discarded_by`, `discard_reason`, `correlation_id`.
  - Per-service `outbox_events` gains `status` (`Pending|Sent|Failed`), `attempts`, `last_error`, `last_attempt_at`, `correlation_id`.
- **Key models**:
  - `DeadLetterMessage` (gateway-owned).
  - `OutboxEvent` (extended in-place, owned per-service).
  - `Event` base record gains optional `CorrelationId` (`Guid?`), backwards-compatible.
- **Auth**: existing JWT (RS256/JWKS) is reused. New role claim `operator` issued by auth-microservice. New shared policy `RequireOperator`.
- **Migrations**: EF Core, per-service `IDesignTimeDbContextFactory` pattern preserved. Gateway gets its own `DeadLetterDbContext` and design-time factory.
- **Observability**: Prometheus counters `dlq_messages_total{service,event_type}`, `dlq_replays_total{service,event_type,outcome}`, `dlq_discards_total{service,event_type}`. OTEL spans for capture and replay reuse `RabbitMqTelemetry`.
- **Hosting**: Operator UI and admin API live in the existing `api-gateway/ApiGateway` process. Mapped before the YARP/Ocelot proxy so the `Gateway:Provider` switch is irrelevant to these routes.

---

## Phase 1: Tracer — capture one consumer failure end-to-end

**User stories**: 1, 4, 5, 12, 13, 15, 17, 19

### What to build

A single thin path through the entire stack: a failing handler in **one** service (basket, smallest blast radius) is retried, dead-lettered to `ecommerce-dlq`, persisted in the gateway's `dead_letter_messages` table, and visible via `GET /operator/api/failures` behind `RequireOperator`. No UI, no replay, no outbox changes yet — but every layer (broker topology, manual ack, retry, DLX consumer, EF Core store, gateway API, auth policy) has its first user.

### Acceptance criteria

- [ ] `ecommerce-dlx` and `ecommerce-dlq` are declared at startup; basket's queue declares the dead-letter args.
- [ ] Basket consumer uses manual ack and the bounded Polly retry policy.
- [ ] `RequireOperator` policy and `operator` role claim issuance exist in shared auth and auth-microservice.
- [ ] Gateway hosts the new `DeadLetterDbContext` with an applied EF migration creating `dead_letter_messages`.
- [ ] Gateway's `DeadLetterHostedService` consumes `ecommerce-dlq` and inserts a row per failure with payload, routing key, original queue, failure reason, stack trace, attempts, and failed-at.
- [ ] `GET /operator/api/failures` returns the paged list and rejects unauthenticated/non-operator callers with 401/403.
- [ ] `dlq_messages_total{service,event_type}` increments on capture.
- [ ] Demo: force a basket handler exception → row appears in API response within one polling cycle.

---

## Phase 2: Replay one message

**User stories**: 6, 7, 14, 16, 18

### What to build

Add the replay path on top of phase 1: `POST /operator/api/failures/{id}/replay` re-publishes the stored payload via the default exchange to the message's `OriginalQueue`. The originating service's existing `IEventHandler<T>` pipeline runs the message; no other service sees it. The DLQ row transitions `Pending → Replayed`, captures `replayed_at` / `replayed_by`, emits a structured Serilog audit event, and increments `dlq_replays_total`. Still single-service (basket) and still no UI.

### Acceptance criteria

- [ ] Endpoint returns 202 with the new message id; replay is async server-side.
- [ ] Replayed message arrives only on `OriginalQueue`; no other subscriber receives it.
- [ ] Replay span is linked to the original capture trace via `CorrelationId` propagation.
- [ ] Status, `replayed_at`, and `replayed_by` (sub claim) are persisted.
- [ ] Audit log line written via Serilog with operator subject and failure id.
- [ ] `dlq_replays_total{service,event_type,outcome}` increments with `outcome` = `success` or `failure`.
- [ ] Demo: replay a captured basket failure with a now-fixed handler and observe a single successful re-execution.

---

## Phase 3: Roll out manual-ack + DLQ to all services, with broker-behaviour tests

**User stories**: 1 (broadened), 15

### What to build

Extend the manual-ack + DLX wiring from basket to order, inventory, payment, shipping, product, and auth. Add the priority test suite from the PRD: Testcontainers RabbitMQ integration tests that exercise ack/retry/DLQ behaviour at the shared infrastructure layer. No UI, no schema changes; this phase is about coverage and confidence.

### Acceptance criteria

- [ ] All seven service consumers use manual ack, the retry policy, and the DLX-bound queue.
- [ ] Every existing service test suite still passes against the new ack model.
- [ ] Testcontainers test: handler succeeds → message ack'd, DLQ empty.
- [ ] Testcontainers test: handler throws once then succeeds with retry budget = 1 → ack'd on second attempt, DLQ empty.
- [ ] Testcontainers test: handler always throws → message lands on `ecommerce-dlq` with the `OriginalQueue` header populated.
- [ ] Testcontainers test: queue topology is idempotent across consumer restarts.
- [ ] Failures from each service produce `dlq_messages_total` rows with the correct `service` label.

---

## Phase 4: Operator UI in the API Gateway

**User stories**: 1, 2, 3, 8, 9, 10, 13, 20

### What to build

Blazor Server UI mounted at `/operator` inside the existing API Gateway process. The UI consumes the admin API and provides: a list view with the documented filters, a detail view showing payload and stack trace, single replay, batch replay (multi-select), and discard with mandatory reason. Discard is added to the admin API in this phase: `POST /operator/api/failures/{id}/discard`. Status transitions to `Discarded` with `discarded_by` / `discarded_at` / `discard_reason` and increments `dlq_discards_total`. UI is gated by the same `RequireOperator` policy.

### Acceptance criteria

- [ ] `/operator` redirects unauthenticated users to login and rejects non-operator users.
- [ ] List view filters by service, event type, status, date range, and origin.
- [ ] Detail view shows full payload (formatted JSON) and stack trace.
- [ ] Single replay button calls the existing replay endpoint and reflects the new status without a full reload.
- [ ] Batch replay accepts a selection and is fronted by `POST /operator/api/failures/replay-batch`.
- [ ] Discard requires a non-empty reason at both the API and UI layers.
- [ ] Discard audit log written; `dlq_discards_total` increments.
- [ ] UI is reachable through the existing gateway URL with no separate deploy artifact and works under both `Gateway:Provider=Yarp` and `Gateway:Provider=Ocelot`.

---

## Phase 5: Outbox failure tracking + unified failure view

**User stories**: 11, 19

### What to build

Make outbox publish failures first-class and surface them in the same operator UI. Extend the shared Outbox: `OutboxBackgroundService` catches `PublishAsync` exceptions, increments `attempts`, records `last_error` and `last_attempt_at`, and marks the row `Failed` after `OutboxOptions.MaxAttempts`. Each service exposes `GET /internal/outbox/failed` behind a service-to-service auth policy. Gateway aggregates these and exposes them through the existing failures API with `origin = Outbox`. The UI gains an `Origin` column and an `Outbox` filter. Outbox rows remain read-only in the UI (per Out of Scope).

### Acceptance criteria

- [ ] EF migration in every service adds `status`, `attempts`, `last_error`, `last_attempt_at`, `correlation_id` to `outbox_events`.
- [ ] Outbox publish failures are caught, persisted, and stop being retried after `MaxAttempts`.
- [ ] `/internal/outbox/failed` returns failed rows for that service and rejects calls without the service-to-service policy.
- [ ] `GET /operator/api/failures?origin=Outbox` returns aggregated outbox failures across all services.
- [ ] UI shows a unified list with an `Origin` column and a working `Origin` filter.
- [ ] Outbox rows have no replay/discard buttons in the UI for v1.

---

## Phase 6: CorrelationId on `Event` base + observability polish

**User stories**: 12, 17, 18

### What to build

Add the optional `CorrelationId` (`Guid?`) to the `Event` base record. Propagate it through publish (`RabbitMqEventBus`), consume (`RabbitMqHostedService`), capture (`DeadLetterHostedService`), and replay (`IDeadLetterReplayer`). Backfill it onto OTEL activity tags so DLQ entries link to Jaeger and Loki. Finalize the three Prometheus counters with consistent label sets. No UI changes beyond surfacing the trace link on the detail page.

### Acceptance criteria

- [ ] `Event.CorrelationId` exists, defaults to `null`, and does not break any existing event consumers.
- [ ] When present, `CorrelationId` round-trips publish → handler → capture → replay.
- [ ] DLQ detail view shows the correlation id and a link out to the trace UI when configured.
- [ ] `dlq_messages_total`, `dlq_replays_total`, `dlq_discards_total` exist with the documented label sets and are visible on the existing Prometheus scrape.
- [ ] Replay span is a child of (or linked to) the original capture span via the same `CorrelationId`.
