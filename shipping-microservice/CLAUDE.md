# Shipping — service notes

Clean Architecture + Vertical Slices is the default service shape ([ADR-0012](../docs/adr/0012-clean-arch-vsa-default-service-shape.md)): `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/`.

Boundaries enforced by NetArchTest (`Shipping.Tests/Architecture/LayoutTests.cs`) and the Roslyn `Shipping.Service.LayoutAnalyzer`.

Composes ADR [0011](../docs/adr/0011-order-cleanarch-vsa-pilot.md) by reference (original pilot); reuses [adding-a-new-slice.md](../docs/runbooks/adding-a-new-slice.md) runbook unchanged.

Narrow-pins shared-libs per [ADR-0013](../docs/adr/0013-shared-libs-multi-package-split.md) and [shared-libs-versioning.md](../docs/runbooks/shared-libs-versioning.md): `ECommerce.Shared.Platform`, `ECommerce.Shared.EventBus`, `ECommerce.Shared.Messaging`, `ECommerce.Shared.Contracts`, and `ECommerce.Shared.Testing.Qa`.

## Divergences from Order

- **No `IIntegrationMap<,>` / outbox interceptor seam** — Shipping constructs integration events inline per slice (matches Inventory).
- `IShipmentStore` lives in `Domain/Abstractions/` with `EfShipmentStore` in Infrastructure (matches Order/Inventory).
- Carrier adapters consolidated under `Infrastructure/Carriers/` with `ICarrierGateway` abstraction in `Domain/Abstractions/`:
  - `FakeExpressCarrierGateway`, `FakeGroundCarrierGateway`, `FakeCarrierDispatchRegistry`, `FakeCarrierWebhookParser`, `CarrierStatusApplier`, `CarrierPollingService`, `RateShoppingService`, `CarrierWebhookOptions`.
- `ShippingMetrics` moved to `Infrastructure/Observability/` (no peer-layer `Observability/` folder).
- HTTP write endpoints split per state transition: `PickShipment`, `PackShipment`, `DispatchShipment`, `DeliverShipment`, `FailShipment`, `ReturnShipment`, `CancelShipment`, `ProcessCarrierWebhook`.
- HTTP `CancelShipment` and saga `CancelShipmentCommand` are two distinct slices that each construct `ShipmentCancelledEvent` independently.
- `CarrierPollingService` (hosted) stays in `Infrastructure/Carriers/` — not a `Features/` slice.
- Saga commands (`CreateShipmentCommand` / `CancelShipmentCommand`) consumed from `ECommerce.Shared.Contracts` — not owned in local `Contracts/Integration/`.
