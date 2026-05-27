# Payment — service notes

Clean Architecture + Vertical Slices is the default service shape ([ADR-0012](../docs/adr/0012-clean-arch-vsa-default-service-shape.md)): `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/`.

Boundaries enforced by NetArchTest (`Payment.Tests/Architecture/LayoutTests.cs`) and the Roslyn `Payment.Service.LayoutAnalyzer`.

Composes ADR [0011](../docs/adr/0011-order-cleanarch-vsa-pilot.md) by reference (original pilot); reuses [adding-a-new-slice.md](../docs/runbooks/adding-a-new-slice.md) runbook unchanged.

Narrow-pins shared-libs per [ADR-0013](../docs/adr/0013-shared-libs-multi-package-split.md) and [shared-libs-versioning.md](../docs/runbooks/shared-libs-versioning.md): `ECommerce.Shared.Platform`, `ECommerce.Shared.EventBus`, `ECommerce.Shared.Messaging`, `ECommerce.Shared.Contracts`, and `ECommerce.Shared.Testing.Qa`.

## Divergences from Shipping/Inventory (re-adopts Order pattern)

- **`IIntegrationMap<,>` + `DomainEventOutboxInterceptor` seam reintroduced** — `PaymentContext.Translate` was a real smell with a real workaround (`AuthorizePaymentCommandHandler` manual `DequeueDomainEvents()`) to dissolve.
- `IPaymentStore` lives in `Domain/Abstractions/` with `EfPaymentStore` in Infrastructure (matches Order/Inventory/Shipping).
- `IPaymentGateway` lifted to `Domain/Abstractions/` with `InMemoryPaymentGateway` impl in `Infrastructure/Gateways/` (mirrors Shipping `ICarrierGateway` shape).
- `PaymentMetrics` moved to `Infrastructure/Observability/` (no peer-layer `Observability/` folder).
- **Multi-producer convention (new to Payment)**: HTTP `CapturePayment` / `RefundPayment` and saga `CapturePaymentCommand` / `RefundPaymentCommand` are distinct slices that share the integration-event mapper through DI. HTTP slice owns the `IIntegrationMap<,>` file; saga slice raises the same domain event and the interceptor resolves the map globally — **not** a slice-to-slice source reference.
- Saga commands (`AuthorizePaymentCommand` / `CapturePaymentCommand` / `VoidPaymentCommand` / `RefundPaymentCommand`) consumed from `ECommerce.Shared.Contracts` — not owned in local `Contracts/Integration/`.
- `OrderCustomer` idempotency record is a Domain type co-located with `Payment` aggregate, written by `Features/OrderCreated/` and read by `Features/AuthorizePaymentCommand/`.
