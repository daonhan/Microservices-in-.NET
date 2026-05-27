# Product — service notes

Clean Architecture + Vertical Slices is the default service shape ([ADR-0012](../docs/adr/0012-clean-arch-vsa-default-service-shape.md)): `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/`.

Boundaries enforced by NetArchTest (`Product.Tests/Architecture/LayoutTests.cs`) and the Roslyn `Product.Service.LayoutAnalyzer`.

Composes ADR [0011](../docs/adr/0011-order-cleanarch-vsa-pilot.md) by reference (original pilot); reuses [adding-a-new-slice.md](../docs/runbooks/adding-a-new-slice.md) runbook unchanged.

Narrow-pins shared-libs per [ADR-0013](../docs/adr/0013-shared-libs-multi-package-split.md) and [shared-libs-versioning.md](../docs/runbooks/shared-libs-versioning.md): `ECommerce.Shared.Platform`, `ECommerce.Shared.EventBus`, `ECommerce.Shared.Messaging`, and `ECommerce.Shared.Testing.Qa`.

## Notable

Same shape as Order — no documented divergences.
