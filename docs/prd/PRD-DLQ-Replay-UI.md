# PRD: Dead-Letter Queue + Replay UI — Operator Tool for Failed Events

## Problem Statement

Today, when an integration-event handler in any service throws an exception, the
message is silently lost. RabbitMQ consumers in `RabbitMqHostedService` use
`autoAck: true` and have no `x-dead-letter-exchange`, so a failed `Handle()`
call leaves no trace in the broker, no row in any database, and no alert in
observability dashboards. Operators discover failures only when downstream
state diverges (e.g. an order is `Confirmed` but inventory was never reserved,
or a basket is never cleared after `OrderCreatedEvent`).

The same problem exists at the publish edge: the `OutboxBackgroundService`
currently has no concept of a failed publish. If `IEventBus.PublishAsync`
throws, the loop bubbles the exception and the row stays as `Sent = false`
forever, with no error captured and no retry budget.

Operators have no tool to:
- See *which* events failed, in *which* service, and *why*
- Inspect the original payload and stack trace
- Decide whether to replay or discard a failed message
- Replay a message safely without firing duplicate side-effects across every
  subscriber service

## Solution

Introduce a centralised dead-letter capture pipeline and an operator console
hosted by the API Gateway:

1. Every service's queue is declared durable with a dead-letter exchange
   pointing at a single shared dead-letter exchange and queue
   (`ecommerce-dlx` / `ecommerce-dlq`). Consumers switch to manual ack and
   `BasicNack(requeue: false)` after exhausting an in-process retry budget.
2. A new shared infrastructure module (`DeadLetter`) drains `ecommerce-dlq`
   into a SQL store, capturing the payload, routing key, originating queue,
   failure reason, stack trace, and timestamps.
3. The Outbox is extended with `Status`, `Attempts`, `LastError`, and
   `LastAttemptAt` so failed publishes are first-class and visible alongside
   handler-side failures.
4. The API Gateway hosts an Operator UI at `/operator/dlq` (Blazor Server)
   plus an admin API. Operators authenticated with the new `Operator` role
   can list, filter, inspect, replay, or discard failed messages.
5. Replay is **targeted**: a replayed message is re-published directly to its
   original queue (not fanned out through the exchange), so only the service
   that failed re-runs the handler. Idempotency on handlers remains the
   service's responsibility, but cross-service duplicate side-effects are
   structurally avoided.

## User Stories

1. As an on-call operator, I want a single web page that lists every failed
   integration event across all services, so that I do not have to grep logs
   in eight different services.
2. As an operator, I want to filter the failed-message list by service, event
   type, and failure timestamp, so that I can quickly scope an incident.
3. As an operator, I want to see the full JSON payload of a failed event, so
   that I can understand what state the system tried to apply.
4. As an operator, I want to see the exception type, message, and stack trace
   captured at failure time, so that I can decide whether the failure was
   transient or a code bug.
5. As an operator, I want to see how many in-process retries were attempted
   before the message landed in the DLQ, so that I can tell flaky failures
   from hard failures.
6. As an operator, I want to replay a single failed message with one click,
   so that I can recover from a transient outage without writing a script.
7. As an operator, I want a replayed message to be delivered only to the
   service whose handler failed, so that other subscribers do not run their
   side-effects a second time.
8. As an operator, I want to replay a batch of selected messages, so that I
   can recover after a multi-message incident efficiently.
9. As an operator, I want to mark a message as discarded with a short reason,
   so that I have an audit trail of "we deliberately dropped this".
10. As an operator, I want to see the status of each failed message
    (`Pending`, `Replayed`, `Discarded`), so that I know what work is still
    outstanding.
11. As an operator, I want to see failed *outbox* rows in the same UI as
    failed *consumer* messages, so that I have one place to look for any
    event-pipeline failure.
12. As an operator, I want each failed message to carry a correlation id, so
    that I can pivot from the DLQ entry into Jaeger traces and Loki logs.
13. As an operator, I want only users with an `Operator` role to be able to
    open the UI or call the admin API, so that ordinary customers cannot
    trigger replays.
14. As an operator, I want every replay and discard action to be logged with
    my user id and timestamp, so that there is an audit trail.
15. As a service developer, I want consumer handler failures to follow a
    bounded retry-then-DLQ policy by default, so that I do not have to wire
    retry logic into every handler.
16. As a service developer, I want the replay to deserialize and dispatch the
    event through the existing `IEventHandler<T>` pipeline, so that replayed
    messages flow through the same code path as live messages.
17. As an SRE, I want a Prometheus counter for messages dead-lettered per
    service and event type, so that I can alert on DLQ growth.
18. As an SRE, I want a Prometheus counter for replays performed, so that
    abnormal replay activity is visible in Grafana.
19. As a service developer, I want the DLQ store and Outbox to be migrated by
    the same EF Core migration tooling already used per service, so that the
    operational footprint is familiar.
20. As an operator, I want the UI to be reachable through the existing API
    Gateway URL and auth flow, so that there is no separate deploy artifact
    or login.

## Implementation Decisions

### Modules to be built or modified

- **Modify `ECommerce.Shared.Infrastructure.RabbitMq`**
  Switch consumer queues to `durable: true` with arguments
  `x-dead-letter-exchange = ecommerce-dlx` and a routing key that encodes the
  source queue. Replace `autoAck: true` with manual ack: ack on handler
  success, retry up to `RabbitMqOptions.HandlerRetryCount` with exponential
  backoff via Polly, then `BasicNack(requeue: false)` to send the message to
  the DLX. Capture exception details as message headers so the dead-letter
  consumer can persist them. Public surface (`IEventBus`, `IRabbitMqConnection`)
  is unchanged.

- **New `ECommerce.Shared.Infrastructure.DeadLetter`** (deep module)
  - `IDeadLetterStore` with `Capture`, `List(filter)`, `Get(id)`,
    `MarkReplayed`, `MarkDiscarded`.
  - `DeadLetterDbContext` + `DeadLetterMessage` entity:
    `Id`, `EventType`, `RoutingKey`, `OriginalQueue`, `Payload` (json),
    `FailureReason`, `StackTrace`, `Attempts`, `FailedAt`, `Status`
    (`Pending|Replayed|Discarded`), `ReplayedAt`, `ReplayedBy`,
    `DiscardedAt`, `DiscardedBy`, `DiscardReason`, `CorrelationId`.
  - `DeadLetterHostedService` subscribes to the central `ecommerce-dlq` and
    calls `Capture`.
  - `IDeadLetterReplayer.ReplayAsync(id)` re-publishes the stored payload
    directly to `OriginalQueue` via the default exchange (targeted replay,
    no fan-out).
  - Registered through `AddDeadLetter(...)` and `AddDeadLetterCapture(...)`
    extension methods, mirroring the existing `AddOutbox(...)` pattern.

- **Extend `ECommerce.Shared.Infrastructure.Outbox`**
  Add columns `Status` (`Pending|Sent|Failed`), `Attempts`, `LastError`,
  `LastAttemptAt`, `CorrelationId` to `OutboxEvent`. `OutboxBackgroundService`
  catches publish exceptions, increments `Attempts`, persists the error, and
  marks the row `Failed` after exceeding `OutboxOptions.MaxAttempts`. Failed
  outbox rows are surfaced through the same admin API (`GET /outbox/failed`)
  and same UI list, tagged as origin = `Outbox`.

- **Extend `ECommerce.Shared.Infrastructure.EventBus.Event`**
  Add optional `CorrelationId` (nullable `Guid?`) on the base record. Existing
  events default to `null`; new events can flow it through. Remains
  backwards-compatible because the field is optional.

- **Modify `ECommerce.Shared.Authentication`**
  Add a `RequireOperator` policy bound to the `operator` role claim. Auth
  microservice issues this claim for users in the operator group.

- **Modify `api-gateway/ApiGateway`**
  Host the Operator UI and admin API in the gateway process:
  - Admin API (Minimal API endpoints) under `/operator/api`:
    - `GET /operator/api/failures` — paged list, filters: `service`,
      `eventType`, `status`, `from`, `to`, `origin` (`Consumer|Outbox`)
    - `GET /operator/api/failures/{id}`
    - `POST /operator/api/failures/{id}/replay`
    - `POST /operator/api/failures/replay-batch`
    - `POST /operator/api/failures/{id}/discard`
  - Blazor Server UI under `/operator` (list, detail, replay, discard).
  - Both gated by `RequireOperator`.
  - Gateway depends on `ECommerce.Shared.Infrastructure.DeadLetter` and on
    a read view over each service's Outbox tables. Outbox visibility is
    served through a new endpoint on each service (`GET /internal/outbox/failed`,
    locked behind a service-to-service policy) which the gateway aggregates,
    so the gateway does not directly query other services' databases.

### Architectural decisions

- **Single, central dead-letter exchange and queue.** One operator view, one
  consumer to maintain, one storage location. The `OriginalQueue` header is
  used for targeted replay.
- **Targeted replay over fan-out replay.** Replay re-publishes through the
  default exchange to `OriginalQueue` only. Other subscribers do not re-run.
- **Bounded in-process retry, then DLQ.** Retries are short-lived (Polly,
  exponential backoff, configurable count, default 3). Persistent failures
  go to the DLQ rather than spinning forever.
- **Outbox failures and consumer failures are unified at the read model.**
  The UI shows them in a single list with an `Origin` column.
- **API Gateway hosts the UI.** No new deployable. Reuses existing JWT auth,
  observability, and health-check wiring.
- **Per-service DB ownership preserved.** The DLQ store has its own database
  context owned by the gateway; other services' outbox tables are read via
  a service-internal HTTP endpoint, not direct DB access.

### API contracts (admin API)

- All endpoints require the `RequireOperator` policy.
- `GET /operator/api/failures` returns a paged result with the projected
  fields needed by the list view (no full payload or stack trace).
- `GET /operator/api/failures/{id}` returns the full payload, stack trace,
  and history.
- `POST .../replay` returns 202 with the new message id; replay is async on
  the gateway side.
- `POST .../discard` requires a non-empty reason.
- All mutating actions emit a structured Serilog event with the operator's
  subject claim and the failure id.

### Schema changes

- New table `dead_letter_messages` (gateway DB, snake_case as per repo
  convention).
- `outbox_events` (per-service) gains `status`, `attempts`, `last_error`,
  `last_attempt_at`, `correlation_id`.
- New EF Core migration in each service for the outbox extension. New EF
  Core migration in the gateway for the DLQ store.

### Observability

- Prometheus counters: `dlq_messages_total{service,event_type}`,
  `dlq_replays_total{service,event_type,outcome}`,
  `dlq_discards_total{service,event_type}`.
- Tracing: replay re-uses `RabbitMqTelemetry` so the replay span links into
  the original trace via the propagated `CorrelationId`.

## Testing Decisions

A good test in this codebase exercises *external behaviour* — what an
operator, a service, or the broker observes — not internal helper calls. We
use `WebApplicationFactory<Program>` for HTTP entry points and Testcontainers
for real broker/database behaviour where integration matters. Tests are
named `Given_X_When_Y_Then_Z` (CA1707 already suppressed).

### What we will test

1. **RabbitMQ ack and DLQ behaviour (priority, Testcontainers RabbitMQ).**
   Lives next to the existing RabbitMQ integration tests in
   `shared-libs/ECommerce.Shared.Tests` (or service-level equivalent). Cases:
   - Given a handler that succeeds, when a message is delivered, then it is
     ack'd and not requeued.
   - Given a handler that throws once then succeeds, when a message is
     delivered with retry budget = 1, then the second attempt ack's and the
     DLQ stays empty.
   - Given a handler that always throws, when retry budget is exhausted,
     then the message lands on `ecommerce-dlq` with the `OriginalQueue`
     header populated.
   - Given a queue is declared with the new dead-letter args, when the
     consumer is restarted, then the queue topology is compatible (idempotent
     declare).

   Prior art: existing Testcontainers-based broker tests already in use for
   the saga flows (e.g. `Payment.Tests/IntegrationEvents/CheckoutHappyPathTests.cs`,
   `Shipping.Tests/IntegrationEvents/EventStreamConsolidationTests.cs`).

### What we will *not* (initially) test

Per your direction, the following are not in scope for this PRD's test
coverage: Dead-letter store persistence, admin API endpoints, auth policy
enforcement, outbox failure tracking, and the Blazor UI. They will be
covered in follow-up work if needed.

## Out of Scope

- Automatic, scheduled replay of DLQ messages (manual, operator-driven only).
- A poison-message classifier or automatic discard rules.
- Cross-cluster / cross-region DLQ replication.
- A REST API for non-operator users (e.g. tenants self-replaying their own
  failed events).
- Migrating the broker to RabbitMQ Streams or to Azure Service Bus
  dead-letter semantics. The `AzureServiceBus` shared module is unchanged.
- Per-tenant data isolation in the DLQ store. Tenant scoping can be added
  later via an `OriginalQueue` or payload-derived field; not required for
  the v1 operator tool.
- Editing message payloads before replay. Replay is byte-for-byte.
- Replaying outbox-failed rows from the gateway. Outbox rows are surfaced
  read-only in v1; "retry now" for outbox is a follow-up.

## Further Notes

- The `Event` base record gaining `CorrelationId` is intentionally optional
  to avoid a flag-day migration of every event in every service.
- The `OperatorTools` UI is folded into the API Gateway process to keep the
  deploy footprint identical to today. If the gateway later needs to be
  replaced with a managed service (e.g. Azure API Management), the Blazor
  UI and admin API can be lifted into a standalone `OperatorTools.Api`
  project; the DLQ store and shared infrastructure are designed to be
  hostable independently.
- Manual-ack consumers are a behaviour change: services that currently rely
  on best-effort delivery need to be aware that handler exceptions now
  produce DLQ entries. This is the intended behaviour, but should be called
  out in the rollout plan.
- The `Gateway:Provider` switch (`Yarp` vs `Ocelot`) is unaffected: the
  Operator UI and admin API are mapped before the proxy and are not part of
  the route table either provider exposes.
