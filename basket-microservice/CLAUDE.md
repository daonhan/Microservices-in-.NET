# Basket — service notes

Clean Architecture + Vertical Slices: `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/`.

Boundaries enforced by NetArchTest (`Basket.Tests/Architecture/LayoutTests.cs`) and the Roslyn `Basket.Service.LayoutAnalyzer`.

Composes ADR [0011](../docs/adr/0011-order-cleanarch-vsa-pilot.md) by reference (no new ADR); reuses [adding-a-new-slice.md](../docs/runbooks/adding-a-new-slice.md) runbook unchanged.

## Divergences from Order/Product

- **No outbox seam** — Basket emits no integration events.
- **No CQRS-lite read split** — one read, no projection benefit.
