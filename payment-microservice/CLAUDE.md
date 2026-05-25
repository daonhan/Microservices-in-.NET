# Payment — service notes

Clean Architecture + Vertical Slices: `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/`.

Boundaries enforced by NetArchTest (`Payment.Tests/Architecture/LayoutTests.cs`) and the Roslyn `Payment.Service.LayoutAnalyzer`.

Composes ADR [0011](../docs/adr/0011-order-cleanarch-vsa-pilot.md) by reference (no new ADR); reuses [adding-a-new-slice.md](../docs/runbooks/adding-a-new-slice.md) runbook unchanged.

## Divergences from Shipping/Inventory (re-adopts Order pattern)

- **`IIntegrationMap<,>` + `DomainEventOutboxInterceptor` seam reintroduced** — `PaymentContext.Translate` was a real smell with a real workaround (`AuthorizePaymentCommandHandler` manual `DequeueDomainEvents()`) to dissolve.
- `IPaymentStore` lives in `Domain/Abstractions/` with `EfPaymentStore` in Infrastructure (matches Order/Inventory/Shipping).
- `IPaymentGateway` lifted to `Domain/Abstractions/` with `InMemoryPaymentGateway` impl in `Infrastructure/Gateways/` (mirrors Shipping `ICarrierGateway` shape).
- `PaymentMetrics` moved to `Infrastructure/Observability/` (no peer-layer `Observability/` folder).
- **Multi-producer convention (new to Payment)**: HTTP `CapturePayment` / `RefundPayment` and saga `CapturePaymentCommand` / `RefundPaymentCommand` are distinct slices that share the integration-event mapper through DI. HTTP slice owns the `IIntegrationMap<,>` file; saga slice raises the same domain event and the interceptor resolves the map globally — **not** a slice-to-slice source reference.
- Saga commands (`AuthorizePaymentCommand` / `CapturePaymentCommand` / `VoidPaymentCommand` / `RefundPaymentCommand`) consumed from `ECommerce.Shared.IntegrationEvents.Commands` — not owned in local `Contracts/Integration/`.
- `OrderCustomer` idempotency record is a Domain type co-located with `Payment` aggregate, written by `Features/OrderCreated/` and read by `Features/AuthorizePaymentCommand/`.
