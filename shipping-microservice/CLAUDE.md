# Shipping — service notes

Clean Architecture + Vertical Slices: `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/`.

Boundaries enforced by NetArchTest (`Shipping.Tests/Architecture/LayoutTests.cs`) and the Roslyn `Shipping.Service.LayoutAnalyzer`.

Composes ADR [0011](../docs/adr/0011-order-cleanarch-vsa-pilot.md) by reference (no new ADR); reuses [adding-a-new-slice.md](../docs/runbooks/adding-a-new-slice.md) runbook unchanged.

## Divergences from Order

- **No `IIntegrationMap<,>` / outbox interceptor seam** — Shipping constructs integration events inline per slice (matches Inventory).
- `IShipmentStore` lives in `Domain/Abstractions/` with `EfShipmentStore` in Infrastructure (matches Order/Inventory).
- Carrier adapters consolidated under `Infrastructure/Carriers/` with `ICarrierGateway` abstraction in `Domain/Abstractions/`:
  - `FakeExpressCarrierGateway`, `FakeGroundCarrierGateway`, `FakeCarrierDispatchRegistry`, `FakeCarrierWebhookParser`, `CarrierStatusApplier`, `CarrierPollingService`, `RateShoppingService`, `CarrierWebhookOptions`.
- `ShippingMetrics` moved to `Infrastructure/Observability/` (no peer-layer `Observability/` folder).
- HTTP write endpoints split per state transition: `PickShipment`, `PackShipment`, `DispatchShipment`, `DeliverShipment`, `FailShipment`, `ReturnShipment`, `CancelShipment`, `ProcessCarrierWebhook`.
- HTTP `CancelShipment` and saga `CancelShipmentCommand` are two distinct slices that each construct `ShipmentCancelledEvent` independently.
- `CarrierPollingService` (hosted) stays in `Infrastructure/Carriers/` — not a `Features/` slice.
- Saga commands (`CreateShipmentCommand` / `CancelShipmentCommand`) consumed from `ECommerce.Shared.IntegrationEvents.Commands` — not owned in local `Contracts/Integration/`.
