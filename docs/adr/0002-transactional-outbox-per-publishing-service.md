# ADR-0002 — Transactional Outbox per publishing service

- **Status**: Accepted
- **Date**: 2026-05-06

## Context

Services in the saga (Order, Inventory, Payment, Shipping) must publish integration events as part of business transactions. Publishing straight to RabbitMQ inside the same business code creates the classic dual-write hazard: the database commit can succeed while the broker publish fails (or vice versa), leaving the saga in an inconsistent state. The platform also needs to demonstrate a production-grade messaging pattern, not a "fire-and-pray" `IEventBus.Publish` call.

Implemented as shared infrastructure in [`shared-libs/ECommerce.Shared/Infrastructure/Outbox/`](../../shared-libs/ECommerce.Shared/Infrastructure/Outbox/) and consumed per service. See also the wiki page [`Integration-Events.md`](../wiki/Integration-Events.md).

## Decision

Every publishing service owns a transactional outbox: integration events are written to an `OutboxContext` table inside the same DbContext transaction as the business state change. A background `OutboxBackgroundService` (also from the shared library) polls unpublished rows and pushes them to RabbitMQ, then marks them sent. Each service runs its own outbox table — there is no shared outbox database.

## Consequences

- Publishers get at-least-once delivery semantics with no dual-write risk; consumers must be idempotent (and are).
- Adds one background service and one extra table per publisher. Migrations are applied automatically in development via `app.ApplyOutboxMigrations()`.
- A small publish latency is introduced because the poller sweeps periodically rather than publishing inline.
- Out of scope: change-data-capture-based outbox (e.g. Debezium) — kept as a future option if poll latency ever becomes an issue.
