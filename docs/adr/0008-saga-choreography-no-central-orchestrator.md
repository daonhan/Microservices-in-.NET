# ADR-0008 — Saga choreography (no central orchestrator) for Order/Inventory/Payment/Shipping

- **Status**: Superseded by ADR-0010
- **Date**: 2026-05-06

## Context

Placing an order touches four services: Order owns the order lifecycle, Inventory reserves and commits stock, Payment authorises and captures funds, Shipping arranges fulfillment. There are two canonical ways to coordinate this: a central orchestrator (a state machine that explicitly sends commands to each service) or choreography (each service reacts to integration events from the others). An orchestrator is easier to visualise but creates a single point that has to know every service's contract, which becomes the kind of coupling microservices are meant to avoid.

Implemented across [`order-microservice/`](../../order-microservice/), [`inventory-microservice/`](../../inventory-microservice/), [`payment-microservice/`](../../payment-microservice/), and [`shipping-microservice/`](../../shipping-microservice/), with event types in each service's `IntegrationEvents/Events/` and handlers in `IntegrationEvents/EventHandlers/`. See also the wiki page [`Integration-Events.md`](../wiki/Integration-Events.md).

## Decision

The order saga is **choreographed**: there is no central orchestrator. `OrderCreatedEvent` flows out of Order; Inventory reserves stock and emits `StockReserved` or `StockReservationFailed`; Order then emits `OrderConfirmed` (which Payment and Shipping pick up) or `OrderCancelled`; Inventory commits or releases on the final event. Each service is responsible for its own compensating actions on failure.

## Consequences

- No service knows the entire saga; coupling is reduced to event contracts.
- The end-to-end flow is harder to read in any one place, so it's documented in the wiki and traced via OpenTelemetry (ADR-0009) rather than living in one orchestration class.
- Adding a new participant means subscribing to existing events and emitting new ones — no central state machine to edit.
- Out of scope: long-running saga timeouts implemented as orchestrated state, BPMN-style visual modelling.
