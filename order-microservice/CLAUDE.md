# Order — service notes

Clean Architecture + Vertical Slices: `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/`.

Boundaries enforced by NetArchTest (`Order.Tests/Architecture/LayoutTests.cs`) and the Roslyn `LayoutAnalyzer`.

ADR: [0011](../docs/adr/0011-order-cleanarch-vsa-pilot.md) (original pilot). Runbook for new slices: [adding-a-new-slice.md](../docs/runbooks/adding-a-new-slice.md).

## Notable

- `IIntegrationMap<,>` + `DomainEventOutboxInterceptor` seam: DbContext-level translation switch turns domain events into outbox rows.
- Saga participant: consumes `ConfirmOrderCommand` / `CancelOrderCommand` from `ECommerce.Shared.IntegrationEvents.Commands`.
