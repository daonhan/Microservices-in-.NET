# Saga — service notes

Clean Architecture + Vertical Slices: `Features/<Saga>/<Trigger>/`, `Domain/{OrderSaga,RefundSaga,}/`, `Contracts/Integration/InboundEvents/`, `Infrastructure/`.

Boundaries enforced by NetArchTest (`Saga.Tests/Architecture/LayoutTests.cs`) and the Roslyn `Saga.Service.LayoutAnalyzer`.

Composes ADR [0011](../docs/adr/0011-order-cleanarch-vsa-pilot.md) by reference (no new ADR); reuses [adding-a-new-slice.md](../docs/runbooks/adding-a-new-slice.md) runbook unchanged.

## Role

Orchestrator-only. Saga owns the order saga end-to-end:

- Starts from `OrderCreatedEvent`, persists saga state, drives participants exclusively with commands.
- Issues: `ReserveStockCommand`/`CommitStockCommand`/`ReleaseStockCommand` (Inventory), `AuthorizePaymentCommand`/`CapturePaymentCommand`/`VoidPaymentCommand`/`RefundPaymentCommand` (Payment), `ConfirmOrderCommand`/`CancelOrderCommand` (Order), `CreateShipmentCommand`/`CancelShipmentCommand` (Shipping).
- Participants reply with integration events (`StockReserved|StockReservationFailed|StockCommitted|StockReleased|PaymentAuthorized|Captured|Failed|Voided|Refunded|OrderConfirmed|OrderCancelled|ShipmentCreated|Dispatched|Delivered|Cancelled|Returned|Failed`) carrying `CausationId`/`SagaId`.
- Cutover completed 2026-05-18 (issue #132); legacy event-driven saga handlers removed.

Runbook: [saga-orchestrator-strangler.md](../docs/runbooks/saga-orchestrator-strangler.md). ADR: [0010](../docs/adr/0010-saga-orchestrator-supersedes-choreography.md).

## Divergences from other services

- **Two-level `Features/<Saga>/<Trigger>/` namespace nesting** (new — prior pilots flat; justified by two saga aggregates coexisting in one service).
- **`ISagaTransitionRunner<TState, TEvent>` Domain abstraction** (new to saga) — encapsulates load → pure transition → persist with `SagaTransition` row → outbox-publish commands in one EF transaction.
- `OrderSagaReplyProcessor` + `RefundSagaReplyProcessor` fan-out routers **deleted** — dispatch dissolved into per-slice handlers; shared persistence loop lifted into `EfOrderSagaTransitionRunner` + `EfRefundSagaTransitionRunner`.
- **No `IIntegrationMap<,>` + `DomainEventOutboxInterceptor` seam** — saga emits commands directly from state-machine result (no `Translate(...)` smell to dissolve; matches Inventory/Shipping).
- **Dual-subscription convention for `PaymentRefundedEvent`** — two slices register, each loads its own saga by id, no-ops if not its own. **Only place in the monorepo where one integration event drives two slices that must both act on it.**
- Reaper as `Infrastructure/Reaper/` hosted service mirroring Shipping's `Infrastructure/Carriers/CarrierPollingService` — **no `Features/<Saga>/TimeoutEscalation/` slice** (reaper is internal scheduling, not an inbound trigger).
- **No HTTP write endpoint outside `Features/Operator/{AbortSaga,RetrySaga}/`** — saga is event-driven by design. `AbortSaga` cancels an in-flight saga; `RetrySaga` requeues the in-flight command.
- Saga commands (`ReserveStockCommand`/`AuthorizePaymentCommand`/etc.) consumed from `ECommerce.Shared.IntegrationEvents.Commands` — not owned in local `Contracts/Integration/`.
