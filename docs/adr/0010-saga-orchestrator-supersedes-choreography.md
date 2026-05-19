# ADR-0010 — Saga orchestrator supersedes choreography for Order/Inventory/Payment/Shipping

- **Status**: Accepted
- **Date**: 2026-05-17
- **Supersedes**: [ADR-0008](0008-saga-choreography-no-central-orchestrator.md)
- **Pre-cutover reference**: branch [`saga-choreography`](https://github.com/daonhan/Microservices-in-.NET/tree/saga-choreography) preserves the last choreography-only state of the repo for historical inspection.

## Context

ADR-0008 chose choreography for the Order/Inventory/Payment/Shipping saga. That kept service coupling low, but running the platform exposed operational costs that now outweigh the original simplicity. Incident #113 showed that a stuck order can require querying multiple service databases and correlating traces, outbox rows, and DLQ entries before the failure point is clear. The smoke-test saga hardening work added more checks around the same distributed flow, and the StockItem aggregate work (#55, #115-#118) made Inventory's local state machine more explicit while the cross-service saga remained implicit.

The replacement is specified in [`PRD-Saga-Orchestrator.md`](../prd/PRD-Saga-Orchestrator.md) and planned in [`saga-orchestrator.md`](../plans/saga-orchestrator.md). The repo already has the infrastructure primitives this needs: service-owned SQL databases (ADR-0007), transactional outbox (ADR-0002), RabbitMQ fanout with DLQ replay (ADR-0004), and shared authentication/observability plumbing via `ECommerce.Shared` (ADR-0005).

## Decision

Replace the order saga choreography with a central `saga-microservice` orchestrator. The orchestrator owns saga instance state, drives participant services by sending commands, and listens for the existing reply integration events. Participant services continue to publish their existing events as orchestrator-driven replies; choreography subscribers were removed at cutover.

The rollout was phased behind `Saga:Orchestrator:Enabled`, with allowlist and percentage controls. Each order was assigned to exactly one path when `OrderCreatedEvent` arrived. Cutover to orchestrator-only completed **2026-05-18** (issue #132); the choreography saga-step handlers in Order, Inventory, Payment, and Shipping were removed in the same change. The runbook's cutover criteria — 100% orchestrator traffic with no manual operator intervention attributable to the orchestrator path — were the gating condition. The strangler flag was removed **2026-05-19** (issue #136); the orchestrator now opens a saga for every `OrderCreatedEvent` unconditionally.

## Consequences

- Operators get one durable saga state record and transition log to answer where an order is stuck.
- Retry, timeout, and compensation rules move into one state machine instead of being spread across participant event handlers.
- The saga service becomes coupled to participant command contracts, so command schemas and reply-event causation fields must be versioned deliberately.
- Rollback is no longer a configuration change after cutover; reverting requires restoring the removed choreography handlers and event registrations.
- Follow-up work implements the service skeleton, command contracts, state machine slices, reaper, observability, operator API, DLQ verification, and RefundSaga from the linked plan.
