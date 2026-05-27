# Order — service notes

Clean Architecture + Vertical Slices is the default service shape ([ADR-0012](../docs/adr/0012-clean-arch-vsa-default-service-shape.md)): `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/`.

Boundaries enforced by NetArchTest (`Order.Tests/Architecture/LayoutTests.cs`) and the Roslyn `LayoutAnalyzer`.

ADRs: [0011](../docs/adr/0011-order-cleanarch-vsa-pilot.md) (original pilot), [0012](../docs/adr/0012-clean-arch-vsa-default-service-shape.md) (promoted to default). Runbook for new slices: [adding-a-new-slice.md](../docs/runbooks/adding-a-new-slice.md).

Narrow-pins shared-libs per [ADR-0013](../docs/adr/0013-shared-libs-multi-package-split.md) and [shared-libs-versioning.md](../docs/runbooks/shared-libs-versioning.md): `ECommerce.Shared.Platform`, `ECommerce.Shared.EventBus`, `ECommerce.Shared.Messaging`, `ECommerce.Shared.Contracts`, and `ECommerce.Shared.Testing.Qa`.

## Notable

- `IIntegrationMap<,>` + `DomainEventOutboxInterceptor` seam: DbContext-level translation switch turns domain events into outbox rows.
- Saga participant: consumes `ConfirmOrderCommand` / `CancelOrderCommand` from `ECommerce.Shared.Contracts`.
