# Product — service notes

Clean Architecture + Vertical Slices: `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/`.

Boundaries enforced by NetArchTest (`Product.Tests/Architecture/LayoutTests.cs`) and the Roslyn `Product.Service.LayoutAnalyzer`.

Composes ADR [0011](../docs/adr/0011-order-cleanarch-vsa-pilot.md) by reference (no new ADR); reuses [adding-a-new-slice.md](../docs/runbooks/adding-a-new-slice.md) runbook unchanged.

## Notable

Same shape as Order — no documented divergences.
