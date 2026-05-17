# ADR-0010 — Saga orchestrator supersedes choreography for Order/Inventory/Payment/Shipping

- **Status**: Accepted
- **Date**: 2026-05-17
- **Supersedes**: ADR-0008

## Context

ADR-0008 chose choreography for the Order/Inventory/Payment/Shipping saga. That kept service coupling low, but running the platform exposed operational costs that now outweigh the original simplicity. Incident #113 showed that a stuck order can require querying multiple service databases and correlating traces, outbox rows, and DLQ entries before the failure point is clear. The smoke-test saga hardening work added more checks around the same distributed flow, and the StockItem aggregate work (#55, #115-#118) made Inventory's local state machine more explicit while the cross-service saga remained implicit.

The replacement is specified in [`PRD-Saga-Orchestrator.md`](../prd/PRD-Saga-Orchestrator.md) and planned in [`saga-orchestrator.md`](../plans/saga-orchestrator.md). The repo already has the infrastructure primitives this needs: service-owned SQL databases (ADR-0007), transactional outbox (ADR-0002), RabbitMQ fanout with DLQ replay (ADR-0004), and shared authentication/observability plumbing via `ECommerce.Shared` (ADR-0005).

## Decision

Replace the order saga choreography with a central `saga-microservice` orchestrator. The orchestrator owns saga instance state, drives participant services by sending commands, and listens for the existing reply integration events. Participant services continue to publish their existing events during the strangler period so non-orchestrated orders and existing consumers keep working.

The rollout is phased behind `Saga:Orchestrator:Enabled`, with allowlist and percentage controls. Each order is assigned to exactly one path when `OrderCreatedEvent` arrives: the new orchestrator path or the existing choreography path. Choreography handlers remain in place until the orchestrator handles 100% of new orders for the documented soak window and a later cutover issue removes them.

## Consequences

- Operators get one durable saga state record and transition log to answer where an order is stuck.
- Retry, timeout, and compensation rules move into one state machine instead of being spread across participant event handlers.
- The saga service becomes coupled to participant command contracts, so command schemas and reply-event causation fields must be versioned deliberately.
- Rollback remains a configuration change for new orders while the strangler flag is active; in-flight orders stay on the path selected at saga start.
- Follow-up work implements the service skeleton, command contracts, state machine slices, reaper, observability, operator API, DLQ verification, and RefundSaga from the linked plan.
