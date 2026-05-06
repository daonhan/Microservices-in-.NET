# ADR-0004 — RabbitMQ fanout exchange with dead-letter queue and operator API

- **Status**: Accepted
- **Date**: 2026-05-06

## Context

Integration events drive the saga, and several services often need to react to the same event (e.g. `OrderCreated` is interesting to Inventory, and to whatever observability/auditing consumer comes later). Routing-key topology would force publishers to know about every consumer's binding pattern. Separately, message handlers can fail — bad payloads, downstream outages, bugs — and silently dropping or infinitely retrying poison messages is unacceptable. Operators need a way to see what failed and either replay or discard it.

Implemented in [`shared-libs/ECommerce.Shared/Infrastructure/RabbitMq/`](../../shared-libs/ECommerce.Shared/Infrastructure/RabbitMq/) (publish/subscribe) and surfaced through the gateway's operator endpoints. See also the wiki page [`Integration-Events.md`](../wiki/Integration-Events.md).

## Decision

All integration events flow through a single RabbitMQ **fanout exchange** named `ecommerce-exchange`. Each subscriber declares its own queue bound to that exchange, so adding a new consumer never touches publishers. Failed message handling routes to a dead-letter queue per subscriber; failures are persisted as outbox-failure rows and exposed via a gateway-fronted operator API (`/operator/api/failures/...`) supporting view, replay (single + batch), and discard.

## Consequences

- Publishers stay completely unaware of consumers — easy to add new subscribers.
- All consumers receive every event, so handlers must filter on event type. Acceptable given the small event catalogue.
- The DLQ + operator API adds operational surface (endpoints, metrics like `dlq_discards_total`) but turns silent failures into visible, actionable ones.
- Out of scope: topic-based routing, partitioned consumers for ordering guarantees.
