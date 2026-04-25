# PRD — Payment Microservice

> Tracking issue: https://github.com/daonhan/Microservices-in-.NET/issues/11

## Context

The Nhamnhi e-commerce repo currently runs an event-choreographed saga across Order, Inventory, Basket, Shipping, Product, and Auth services. The checkout loop today is **incomplete**: an order is created, stock is reserved/committed, and a shipment is produced — but **money never moves**. There is no payment authorization, no capture, no failure compensation tied to charging. This PRD specifies a Payment microservice that joins the existing choreography as a sibling participant (modeled on Shipping), closes the checkout loop, and lets failed payments compensate the saga via the existing `OrderCancelledEvent` path. Outcome: a complete create-order → reserve-stock → authorize-payment → confirm-order → ship → capture → deliver flow with deterministic, testable failure handling and no real PSP dependency.

## Problem Statement

As an operator of this microservices system, I cannot accept money for the orders my customers place. Stock is reserved and shipments are produced for orders that have not been paid for. There is no way to fail an order because of a declined card, no way to capture funds when goods leave the warehouse, and no way to refund a returned order. The saga is missing its money leg, so the system cannot run a real checkout.

As a customer, I expect that placing an order means my card is charged when the order is confirmed and the funds are actually taken when my goods ship. If my card is declined, I expect the order to be cancelled cleanly and my reserved stock to be released for someone else.

## Solution

Add a Payment microservice as a saga participant alongside Shipping. Payment subscribes to `StockReservedEvent`, authorizes the order's amount against a pluggable provider (a deterministic in-memory fake by default), and publishes `PaymentAuthorizedEvent` on success or `PaymentFailedEvent` on failure. Order is updated to confirm the order only after `PaymentAuthorizedEvent` (not after `StockReservedEvent` directly, as it does today). On `PaymentFailedEvent`, Order publishes `OrderCancelledEvent`, which the existing Inventory handler already consumes to release stock.

Capture is two-step: Payment subscribes to `ShipmentDispatchedEvent` and captures previously authorized funds when goods leave the warehouse. Refunds are admin-triggered via a REST endpoint on Payment.

The `OrderCreatedEvent` integration event is extended to carry `UnitPrice` per item and a `Currency` field; Order computes the total at checkout from cached product prices. This amount flows through `StockReservedEvent` so Payment can charge without an extra round-trip. Existing consumers (Inventory, Basket) ignore the new fields.

From the customer's perspective, nothing in the API changes today — the flow becomes correct rather than gaining new surface area. Admins gain a small Payment admin API for inspection, refund, and manual capture.

## User Stories

1. As a customer, I want my card authorized as soon as my order's stock is reserved, so that I know quickly whether my purchase will go through.
2. As a customer, I want my order to be cancelled and my reserved stock released if my card is declined, so that I am not left with a dead pending order.
3. As a customer, I want my card to be charged only when my goods physically ship, so that I am not paying for items still sitting in a warehouse.
4. As a customer, I want to retrieve the payment record for my order via the API, so that I can confirm what was charged.
5. As an administrator, I want to look up payments by order ID, so that I can support customer queries.
6. As an administrator, I want to look up payments by payment ID, so that I can investigate a specific transaction surfaced in logs or metrics.
7. As an administrator, I want to refund a captured payment, so that I can resolve disputes and process returns.
8. As an administrator, I want to manually capture an authorized payment, so that I can override the dispatch-driven capture in exceptional cases.
9. As a developer, I want a deterministic in-memory payment provider in non-production environments, so that I can run the saga end-to-end in CI without external secrets or network flakiness.
10. As a developer, I want the Payment service to be a sibling of Shipping in repo structure, build, and deployment, so that I can extend and operate it using the same patterns I already know.
11. As a developer, I want the Payment service to use the existing transactional outbox library, so that payment events are not lost and saga semantics remain at-least-once.
12. As a developer, I want Payment to deduplicate by `OrderId`, so that a redelivered `StockReservedEvent` does not double-charge the customer.
13. As a developer, I want Payment to be discoverable via the API gateway's combined Swagger UI, so that I can explore its endpoints alongside the other services.
14. As a developer, I want Payment events to follow the existing `Event` base record and naming convention, so that they integrate with the shared event bus without special-casing.
15. As a developer, I want a Payment health check endpoint, so that orchestration platforms can detect a sick instance.
16. As an operator, I want metrics on payment volume, success rate, and latency, so that I can monitor checkout health.
17. As an operator, I want OpenTelemetry traces from Payment into the existing Jaeger setup, so that I can trace a checkout from order creation through capture.
18. As a security reviewer, I want admin-only endpoints (refund, manual capture) to require the Administrator role, so that customers cannot trigger them.
19. As a security reviewer, I want customers to see only their own payment records, so that ownership is enforced consistently with the Shipping service pattern.
20. As a saga designer, I want Order to confirm the order only after payment is authorized, so that no unpaid order proceeds to shipment.
21. As a saga designer, I want stock release on payment failure to reuse the existing `OrderCancelledEvent` cascade, so that no parallel compensation channel is introduced.
22. As a saga designer, I want capture triggered by `ShipmentDispatchedEvent`, so that money moves at the same business moment that goods do.
23. As a tester, I want an integration test that drives the happy path from `OrderCreatedEvent` through `PaymentAuthorizedEvent` to `OrderConfirmedEvent`, so that the saga's money leg is regression-protected.
24. As a tester, I want an integration test for the payment-failure compensation path, so that declined-card cleanup is regression-protected.
25. As a tester, I want unit tests on the `Payment` aggregate's state machine, so that illegal state transitions (e.g., capturing a failed payment) are guaranteed to throw.
26. As a tester, I want endpoint tests for refund and capture covering authentication and ownership, so that authorization is regression-protected.

## Implementation Decisions

### New service: Payment

- New microservice folder `payment-microservice/Payment.Service/` mirroring the Shipping layout (`Endpoints/`, `ApiModels/`, `Models/`, `Infrastructure/Data/EntityFramework/`, `IntegrationEvents/Events/`, `IntegrationEvents/EventHandlers/`, `Migrations/`).
- Test project `payment-microservice/Payment.Tests/` with `IntegrationTestBase`, `PaymentWebApplicationFactory`, and `TestAuthHandler`, mirroring `Shipping.Tests`.
- Owns its own SQL Server database `Payment` on the shared instance.
- Registered in `docker-compose.yaml` with port `8007:8080`, RabbitMQ host, SQL connection string, OTEL endpoints, and `Authentication__AuthMicroserviceBaseAddress` matching the Shipping entry.
- Registered in `kubernetes/payment-microservice.yaml` (mirror of `kubernetes/shipping-microservice.yaml`).
- Registered in API gateway routes (`/payment` → `http://payment:8080`) via the YARP route config used by the other services. Combined Swagger picks it up automatically through the gateway's OpenAPI discovery.

### Payment aggregate

- Entity: `Payment { PaymentId, OrderId (unique), CustomerId, Amount, Currency, Status, ProviderReference, CreatedAt, UpdatedAt }`.
- States: `Pending → Authorized → Captured → Refunded` (happy path). Terminal/branch states: `Failed` (from `Pending` only), `Refunded` (from `Captured` only).
- State machine exposed via methods on the aggregate (`TryAuthorize`, `TryFail`, `TryCapture`, `TryRefund`), each enforcing legal source states and throwing on illegal transitions. This is a deep module: the rest of the service speaks only to these four methods plus a constructor.
- Unique constraint on `OrderId` enforces idempotency: a re-delivered `StockReservedEvent` for the same `OrderId` is a no-op.

### Payment provider abstraction

- Interface `IPaymentGateway` with `AuthorizeAsync(amount, currency, reference)`, `CaptureAsync(reference)`, `RefundAsync(reference, amount)`.
- Default implementation `InMemoryPaymentGateway`, deterministic by amount: amounts ending in specific cents map to success/decline/timeout outcomes (exact rules documented in code XML doc comments).
- Registered via DI; future Stripe implementation can be slotted behind config without changing consumers. Interface stays small and stable — a deep module.

### Integration event contract changes

- Extend `OrderItem` (in `ECommerce.Shared.Infrastructure.EventBus` or the Order events folder, wherever it currently lives) with `decimal UnitPrice`.
- Extend `OrderCreatedEvent` with `string Currency`.
- Extend `StockReservedEvent` with `decimal Amount` and `string Currency`, echoed by Inventory from the `OrderCreatedEvent` it consumed.
- Existing consumers (Basket, Inventory non-pricing logic) ignore the new fields. Inventory only echoes them through.
- Order computes per-item `UnitPrice` at checkout time using the same product-price cache strategy Basket uses. If a price is not cached, checkout fails synchronously with `InvalidOperationException` (consistent with Basket's current behavior on missing prices).

### New integration events (published by Payment)

- `PaymentAuthorizedEvent { PaymentId, OrderId, CustomerId, Amount, Currency }`.
- `PaymentFailedEvent { PaymentId, OrderId, CustomerId, Reason }`.
- `PaymentCapturedEvent { PaymentId, OrderId, Amount }`.
- `PaymentRefundedEvent { PaymentId, OrderId, Amount }`.
- All inherit `ECommerce.Shared.Infrastructure.EventBus.Event`.

### New consumers (in Payment)

- `StockReservedEventHandler` — creates `Payment` row in `Pending`, calls `IPaymentGateway.AuthorizeAsync`, transitions to `Authorized` or `Failed`, emits `PaymentAuthorizedEvent` or `PaymentFailedEvent` via outbox in the same transaction.
- `ShipmentDispatchedEventHandler` — captures the authorized payment, transitions to `Captured`, emits `PaymentCapturedEvent`.
- `OrderCancelledEventHandler` — if a `Pending` or `Authorized` payment exists for the cancelled order, voids/refunds and transitions to a terminal state. Idempotent.

### Order service changes

- Replace the current `StockReservedEventHandler`-driven `OrderConfirmedEvent` publication with a `PaymentAuthorizedEventHandler`-driven one. `StockReservedEvent` no longer triggers confirmation directly.
- New `PaymentFailedEventHandler` publishes `OrderCancelledEvent` (reusing the existing event), which Inventory's existing handler consumes to release stock.
- Order computes `UnitPrice` and `Currency` at order creation and includes them in `OrderCreatedEvent`.

### Inventory service changes

- Echo `Amount` and `Currency` from `OrderCreatedEvent` into the `StockReservedEvent` it publishes. No other behavior change.

### Payment HTTP API (behind gateway, prefix `/payment`)

- `GET /payment/by-order/{orderId}` — customer (ownership-checked) + admin.
- `GET /payment/{paymentId}` — customer (ownership-checked) + admin.
- `POST /payment/{paymentId}/refund` — admin only. Body: `{ amount?: decimal }` (defaults to full amount).
- `POST /payment/{paymentId}/capture` — admin only. Manual capture override.
- `GET /health/live`, `GET /health/ready` — standard.

### Auth, observability, build

- Reuses the shared `AddJwtAuthentication` extension; admin endpoints gated by `policy.RequireClaim("user_role", "Administrator")`; ownership check on read endpoints mirrors Shipping.
- Reuses the shared `MetricFactory` for `payments_total` (counter), `payment_authorize_latency_ms` (histogram), `payment_failure_rate` (derived).
- OpenTelemetry traces and Loki logs configured exactly as Shipping is configured.
- Bumps `ECommerce.Shared` to 1.18.0 because `OrderItem` and `OrderCreatedEvent` shapes change. All services pick up the bump in their `csproj` and republish their docker images.

## Testing Decisions

A good test in this codebase asserts external, observable behavior: events emitted, database rows persisted, HTTP responses returned, and authorization decisions enforced. Tests must not assert on private fields, internal method ordering, or DI registrations. Integration tests dispatch events through the same keyed-DI handler resolution path that production uses (see `ShipmentCreationFlowTests.DispatchAsync` for prior art) so event handlers are exercised with real EF Core, real outbox, and a real (in-test) RabbitMQ stub.

Modules under test:

- **`Payment` aggregate (unit, in `Payment.Tests/Models/PaymentStateMachineTests.cs`):** every legal transition succeeds, every illegal transition throws. No DB, no DI. Prior art: state-machine tests inferred from `Shipment` aggregate methods (`TryPick`, `TryPack`, `TryDispatch`).
- **Saga happy path (integration, in `Payment.Tests/IntegrationEvents/CheckoutHappyPathTests.cs`):** dispatch `StockReservedEvent` for an order with a successful amount; assert `Payment` row in `Authorized` and `PaymentAuthorizedEvent` emitted on outbox. Then dispatch `ShipmentDispatchedEvent`; assert `Captured` and `PaymentCapturedEvent`. Prior art: `Shipping.Tests/IntegrationEvents/ShipmentCreationFlowTests.cs`.
- **Compensation path (integration, in `Payment.Tests/IntegrationEvents/PaymentFailureCompensationTests.cs`):** dispatch `StockReservedEvent` for an order with a declining amount; assert `Failed` and `PaymentFailedEvent`. The Order service's reaction (publishing `OrderCancelledEvent`) is tested in Order's test project, not Payment's, to keep service test boundaries clean. Prior art: `Shipping.Tests/IntegrationEvents/OrderCancelledFlowTests.cs`.
- **Admin endpoints (integration, in `Payment.Tests/Api/PaymentEndpointsTests.cs` and `PaymentOwnershipTests.cs`):** refund and capture require Administrator role; customer can read only their own payment; cross-customer reads return 404. Prior art: `Shipping.Tests/Api/ShipmentOwnershipTests.cs` and `ShipmentDispatchEndpointsTests.cs`.
- **Order service** gets new tests for the `PaymentAuthorized → OrderConfirmed` and `PaymentFailed → OrderCancelled` handlers in `Order.Tests/IntegrationEvents/`. These mirror the existing `StockReservedEventHandler` tests and replace them where the saga edge moved.

## Out of Scope

- Real PSP integration (Stripe, Adyen, Braintree). The `IPaymentGateway` interface is built so a real implementation can be added later; no real provider ships with this PRD.
- 3-D Secure, SCA, off-session re-authentication, or any tokenization/vaulting of card data. The fake provider takes an opaque amount and returns success/decline.
- Multi-currency conversion. `Currency` is recorded but not converted; one currency per order.
- Partial captures (capturing less than the authorized amount).
- Split tenders (paying with multiple methods on one order).
- Saved payment methods, customer-stored cards, or wallets.
- Subscription / recurring billing.
- Reconciliation / settlement files.
- Automatic dunning or retry of declined payments.
- Migrating the existing seeded order/customer data to include amounts; only orders created after this change carry pricing in events.
- A separate front-end checkout UI; the existing API surface is the contract.
- Removing the orchestration-style `StockReservedEvent → OrderConfirmedEvent` path on a feature flag. The change is direct; older deployments must roll forward.

## Further Notes

- The shared library version bump (1.17 → 1.18) is the riskiest piece. Every service `csproj` referencing `ECommerce.Shared` must be bumped together to avoid runtime mismatch on event shapes. The bump should land in the same PR as the Payment service.
- Choreography is preserved deliberately. There is no MassTransit/NServiceBus saga state machine in the repo today, and this PRD does not introduce one. The Payment-driven `OrderConfirmed` decision keeps all coordination in event handlers, consistent with the rest of the codebase.

---

## Verification

End-to-end verification once implemented:

1. **Build and unit tests.** `dotnet build` at solution root. `dotnet test payment-microservice/Payment.Tests` — all green, including new aggregate and handler tests. `dotnet test order-microservice/Order.Tests` — all green, including new payment-driven handler tests.
2. **Spin up locally.** `docker compose up --build` from repo root. Confirm `payment` container reports healthy on `http://localhost:8007/health/ready`. Confirm gateway combined Swagger at `http://localhost:8004/swagger` lists Payment endpoints.
3. **Happy path manual smoke.**
   1. Authenticate via Auth and obtain a JWT.
   2. POST a basket, then `POST /order/{customerId}` with a small product list whose total ends in `.00` (success in the fake provider).
   3. `GET /payment/by-order/{orderId}` — expect status `Authorized`.
   4. `POST /shipping/{id}/dispatch` (admin token) — triggers `ShipmentDispatchedEvent`.
   5. `GET /payment/by-order/{orderId}` — expect status `Captured`.
4. **Failure path manual smoke.** Place an order whose total ends in `.99` (decline in the fake provider). `GET /payment/by-order/{orderId}` — `Failed`. `GET /order/{customerId}/{orderId}` — `Cancelled`. `GET /inventory` for a product in the order — its reserved count is back to zero.
5. **Refund manual smoke.** After a captured order, `POST /payment/{paymentId}/refund` with admin token. Expect 200 and status `Refunded`. `PaymentRefundedEvent` visible in RabbitMQ management UI / OTEL traces.
6. **Observability.** Open Jaeger and follow a trace from `Order.CreateOrder` through Inventory, Payment authorize, Order confirm, Shipping create, Shipping dispatch, Payment capture. Open Prometheus and confirm `payments_total` increments per checkout.

## Critical files

To be modified:

- `shared-libs/ECommerce.Shared/Infrastructure/EventBus/` — `OrderItem`, `OrderCreatedEvent`, `StockReservedEvent` extensions; package version bump.
- `order-microservice/Order.Service/Endpoints/OrderApiEndpoint.cs` — populate `UnitPrice` and `Currency` in `OrderCreatedEvent`.
- `order-microservice/Order.Service/IntegrationEvents/EventHandlers/StockReservedEventHandler.cs` — remove `OrderConfirmedEvent` publication (moves to `PaymentAuthorizedEventHandler`).
- `order-microservice/Order.Service/IntegrationEvents/EventHandlers/PaymentAuthorizedEventHandler.cs` — **new**.
- `order-microservice/Order.Service/IntegrationEvents/EventHandlers/PaymentFailedEventHandler.cs` — **new**.
- `inventory-microservice/Inventory.Service/IntegrationEvents/EventHandlers/OrderCreatedEventHandler.cs` — echo `Amount`/`Currency` into `StockReservedEvent`.
- `docker-compose.yaml` — add `payment` service.
- `api-gateway/ApiGateway/` — add `/payment` route.
- `kubernetes/payment-microservice.yaml` — **new**.

To be created:

- `payment-microservice/Payment.Service/` — full service mirroring `shipping-microservice/Shipping.Service/`.
- `payment-microservice/Payment.Tests/` — full test project mirroring `shipping-microservice/Shipping.Tests/`.

## Reusable existing utilities

- `ECommerce.Shared.Infrastructure.EventBus.Event` (base record), `IEventHandler<T>`, `AddRabbitMqEventBus`, `AddEventHandler<,>` — reuse verbatim.
- `ECommerce.Shared.Infrastructure.Outbox.*` — reuse for transactional event publishing.
- `ECommerce.Shared.Authentication.AddJwtAuthentication`, `UseJwtAuthentication` — reuse for auth wiring.
- `ECommerce.Shared.Observability.*` (OTEL, Prometheus, Loki extensions, `MetricFactory`) — reuse.
- Shipping aggregate transition pattern (`TryDispatch`, etc.) — copy the *shape* into `Payment` aggregate, not the code.
- `Shipping.Tests/IntegrationTestBase`, `TestAuthHandler`, `WebApplicationFactory<Program>` setup — copy and rename for `Payment.Tests`.
