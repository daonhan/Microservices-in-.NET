# Inventory — service notes

Clean Architecture + Vertical Slices: `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/`.

Boundaries enforced by NetArchTest (`Inventory.Tests/Architecture/LayoutTests.cs`) and the Roslyn `Inventory.Service.LayoutAnalyzer`.

Composes ADR [0011](../docs/adr/0011-order-cleanarch-vsa-pilot.md) by reference (no new ADR); reuses [adding-a-new-slice.md](../docs/runbooks/adding-a-new-slice.md) runbook unchanged.

## Divergences from Order

- **No `IIntegrationMap<,>` / outbox interceptor seam** — Inventory constructs integration events inline per slice (no DbContext-level translation switch to extract).
- `IInventoryStore` lives in `Domain/Abstractions/` with `EfInventoryStore` in Infrastructure (matches Order).
- `StockLevelMonitor` returns domain-typed crossings (`LowStockCrossing`, `StockDepletion`); slices map to Contracts events.
- Saga commands (`ReserveStockCommand` / `CommitStockCommand` / `ReleaseStockCommand`) consumed from `ECommerce.Shared.IntegrationEvents.Commands` — not owned in local `Contracts/Integration/`.

## Pinning

Narrow-pins the shared-libs saga participant package set: `ECommerce.Shared.Platform`, `ECommerce.Shared.EventBus`, `ECommerce.Shared.Messaging`, `ECommerce.Shared.Contracts`, and `ECommerce.Shared.Testing.Qa` at the current lockstep version. `ECommerce.Shared.Messaging` owns provider selection and the lazy broker singleton fix; boot test `Inventory.Tests.MessagingProviderBootTests` depends on it.
