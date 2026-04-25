# Plan: Payment Microservice — close the checkout loop

> Source PRD: [`docs/prd/PRD-Payment.md`](../prd/PRD-Payment.md) — tracking issue [#11](https://github.com/daonhan/Microservices-in-.NET/issues/11)

## Architectural decisions

Durable decisions that apply across all phases. Locked before any phase begins.

- **Routes** (behind API gateway, prefix `/payment`):
  - `GET /payment/by-order/{orderId}` — customer (ownership) + admin
  - `GET /payment/{paymentId}` — customer (ownership) + admin
  - `POST /payment/{paymentId}/refund` — admin only
  - `POST /payment/{paymentId}/capture` — admin only
  - `GET /health/live`, `GET /health/ready`
- **Schema**: dedicated `Payment` SQL Server database on the shared instance. Single primary aggregate table:
  - `Payment` — `PaymentId (PK)`, `OrderId (UNIQUE)`, `CustomerId`, `Amount`, `Currency`, `Status`, `ProviderReference`, `CreatedAt`, `UpdatedAt`
  - Plus the standard outbox tables from `ECommerce.Shared.Infrastructure.Outbox`.
- **Key models**:
  - `Payment` aggregate. Happy states `Pending → Authorized → Captured → Refunded`; branch `Failed` (from `Pending` only). Transitions exposed as `TryAuthorize`, `TryFail`, `TryCapture`, `TryRefund`; illegal transitions throw.
  - `IPaymentGateway` interface — `AuthorizeAsync`, `CaptureAsync`, `RefundAsync`. Default impl `InMemoryPaymentGateway` (deterministic by amount cents).
- **Auth/authz**: shared `AddJwtAuthentication` extension. Admin endpoints gated by `policy.RequireClaim("user_role", "Administrator")`. Read endpoints enforce ownership against `CustomerId`, mirroring Shipping.
- **Service boundary**: new `payment-microservice/Payment.Service/` mirroring Shipping layout. Consumes RabbitMQ via `ECommerce.Shared`. Owns its DB. Registered in docker-compose, kubernetes, and gateway routes.
- **Event contract**: `ECommerce.Shared` bumps 1.17 → 1.18 once. New shape applied in Phase 2 and propagated to every service in the same slice.
  - `OrderItem.UnitPrice: decimal` (added)
  - `OrderCreatedEvent.Currency: string` (added)
  - `StockReservedEvent.Amount: decimal`, `StockReservedEvent.Currency: string` (added; echoed by Inventory)
  - New events emitted by Payment: `PaymentAuthorizedEvent`, `PaymentFailedEvent`, `PaymentCapturedEvent`, `PaymentRefundedEvent`.
- **Observability**: OpenTelemetry traces to Jaeger, logs to Loki, metrics to Prometheus — all via `ECommerce.Shared.Observability` exactly as Shipping configures them.

---

## Phase 1: Skeleton service boots

**User stories**: 10, 13, 15, 17

### What to build

A new Payment microservice that starts, connects to its own SQL database via EF Core, registers with the API gateway, authenticates via shared JWT, and reports health. No business logic yet — handlers are absent or no-op. Observability (OTEL + Loki + Prometheus) wired from day one. Combined Swagger UI lists Payment endpoints.

### Acceptance criteria

- [x] `payment-microservice/Payment.Service/` and `payment-microservice/Payment.Tests/` exist mirroring the Shipping project layout.
- [ ] `docker compose up --build payment` starts the container, EF migrations create the `Payment` database, container reports healthy.
- [ ] `GET http://localhost:8004/payment/health/ready` returns 200 through the gateway.
- [ ] Combined Swagger at `http://localhost:8004/swagger` enumerates Payment routes.
- [ ] A trace from a health-check call appears in Jaeger; a `payments_total` counter is registered (zero) in Prometheus.
- [x] `kubernetes/payment-microservice.yaml` mirrors the Shipping manifest.

---

## Phase 2: Event contract bump (shared lib 1.18)

**User stories**: 14, 20 (foundation)

### What to build

Extend the integration-event shapes used end-to-end so Payment can later charge the correct amount without an extra round trip. Bump `ECommerce.Shared` to 1.18.0, repackage, and update every consuming service's `csproj` in the same slice. Order populates the new fields from its existing cached product prices; Inventory echoes them onto `StockReservedEvent`. **No behavior change** — this slice exists to land the contract atomically.

### Acceptance criteria

- [ ] `ECommerce.Shared` 1.18.0 published locally; every service `csproj` references it.
- [ ] `OrderItem` carries `UnitPrice`, `OrderCreatedEvent` carries `Currency`, `StockReservedEvent` carries `Amount` and `Currency`.
- [ ] Order's checkout endpoint computes `UnitPrice` per line and `Currency` from cached prices; missing-price path fails with `InvalidOperationException` (consistent with Basket).
- [ ] Inventory's `OrderCreatedEvent` consumer echoes `Amount`/`Currency` onto the `StockReservedEvent` it publishes.
- [ ] All existing tests in Order, Inventory, Basket, and Shipping projects pass unchanged.
- [ ] Manual smoke: place an order, observe `Amount` and `Currency` populated on RabbitMQ payloads (RabbitMQ management UI).

---

## Phase 3: Authorize happy path — saga edge moves to Payment

**User stories**: 1, 9, 11, 12, 20, 23, 25

### What to build

Wire Payment as a saga participant. On `StockReservedEvent`, Payment creates a `Pending` row, calls `InMemoryPaymentGateway.AuthorizeAsync`, transitions to `Authorized`, and emits `PaymentAuthorizedEvent` via the shared transactional outbox. Order's saga edge moves: `OrderConfirmedEvent` is now published in response to `PaymentAuthorizedEvent`, not `StockReservedEvent`. Idempotency enforced by the unique constraint on `OrderId`. The Payment aggregate's full state machine is unit-tested in this phase since it first compiles here.

### Acceptance criteria

- [ ] `Payment` aggregate exists with `TryAuthorize`, `TryFail`, `TryCapture`, `TryRefund`. Aggregate unit tests cover every legal transition (success) and every illegal transition (throws). No DB, no DI in these tests.
- [ ] Payment's `StockReservedEventHandler` creates a row, authorizes via `InMemoryPaymentGateway`, transitions `Pending → Authorized`, emits `PaymentAuthorizedEvent` through outbox in the same transaction.
- [ ] Order's `PaymentAuthorizedEventHandler` publishes `OrderConfirmedEvent`. Order's `StockReservedEventHandler` no longer publishes `OrderConfirmedEvent`.
- [ ] Re-delivering the same `StockReservedEvent` does not create a second `Payment` row (unique-on-`OrderId` is honored idempotently).
- [ ] Integration test `Payment.Tests/IntegrationEvents/CheckoutHappyPathTests.cs` dispatches a `StockReservedEvent` with a `.00` amount and asserts `Authorized` row + `PaymentAuthorizedEvent` on outbox.
- [ ] Order test project gets a new test asserting `PaymentAuthorized → OrderConfirmed`.
- [ ] Manual smoke: a `.00`-total order ends in Order = `Confirmed`, Payment = `Authorized`, Shipping row created.

---

## Phase 4: Failure compensation

**User stories**: 2, 21, 24

### What to build

Wire the decline path through the existing cancellation cascade. On a declining amount, Payment transitions `Pending → Failed` and emits `PaymentFailedEvent`. Order's new `PaymentFailedEventHandler` publishes the existing `OrderCancelledEvent`; Inventory's existing handler already releases stock from that signal. Payment's own `OrderCancelledEventHandler` cleans up any in-flight `Pending`/`Authorized` row for that order (idempotent — no-op if already terminal).

### Acceptance criteria

- [ ] Payment's `StockReservedEventHandler` transitions to `Failed` and emits `PaymentFailedEvent` when the gateway declines.
- [ ] Order's `PaymentFailedEventHandler` publishes `OrderCancelledEvent` (existing event, no contract change).
- [ ] Payment's `OrderCancelledEventHandler` is idempotent: redelivery does not throw, terminal payments are untouched.
- [ ] Integration test `Payment.Tests/IntegrationEvents/PaymentFailureCompensationTests.cs` asserts `Failed` row + `PaymentFailedEvent`.
- [ ] Order test asserts `PaymentFailed → OrderCancelled`.
- [ ] Manual smoke: a `.99`-total order ends in Order = `Cancelled`, Payment = `Failed`, Inventory reserved count back to zero.

---

## Phase 5: Capture on shipment dispatch

**User stories**: 3, 22

### What to build

Capture happens when goods physically leave the warehouse. Payment subscribes to `ShipmentDispatchedEvent` (already published by Shipping — no Shipping change), captures via the gateway, transitions `Authorized → Captured`, and emits `PaymentCapturedEvent`. Idempotent on redelivery.

### Acceptance criteria

- [ ] Payment's `ShipmentDispatchedEventHandler` captures via `IPaymentGateway.CaptureAsync` and transitions `Authorized → Captured`.
- [ ] `PaymentCapturedEvent` emitted via outbox.
- [ ] Redelivery of `ShipmentDispatchedEvent` after the row is already `Captured` is a no-op (no double capture).
- [ ] Integration test extends the happy-path test: dispatch the shipment, assert `Captured` + `PaymentCapturedEvent`.
- [ ] Manual smoke: after Phase 3 happy path, dispatch the shipment via the existing Shipping admin endpoint; Payment row flips `Authorized → Captured`.

---

## Phase 6: Read API + ownership

**User stories**: 4, 5, 6, 19

### What to build

Customer- and admin-facing read endpoints on the Payment service. Customers see only their own payments (ownership check on `CustomerId` from JWT); admins see any. Pattern mirrors `Shipping`'s by-order and by-id endpoints exactly.

### Acceptance criteria

- [ ] `GET /payment/by-order/{orderId}` returns the payment for that order; ownership enforced; admin can read any.
- [ ] `GET /payment/{paymentId}` returns the payment by id; ownership enforced; admin can read any.
- [ ] Cross-customer reads return 404 (not 403), matching Shipping.
- [ ] Endpoint tests in `Payment.Tests/Api/` cover happy reads, ownership rejection, and admin-overrides-all. Prior art: `ShipmentOwnershipTests`.
- [ ] Combined Swagger lists both endpoints with auth annotations.

---

## Phase 7: Admin operations — refund + manual capture

**User stories**: 7, 8, 18, 26

### What to build

Admin-only mutation endpoints. `POST /payment/{id}/refund` accepts an optional `amount` (defaults to full); transitions `Captured → Refunded` (or partial — single transition in v1, partial-refund tracking is Out of Scope per PRD); emits `PaymentRefundedEvent`. `POST /payment/{id}/capture` is the manual override for `Authorized → Captured` (same gateway call as Phase 5, just operator-triggered). Both gated by Administrator role.

### Acceptance criteria

- [ ] `POST /payment/{paymentId}/refund` requires Administrator role; transitions `Captured → Refunded` via `IPaymentGateway.RefundAsync`; emits `PaymentRefundedEvent`.
- [ ] `POST /payment/{paymentId}/capture` requires Administrator role; transitions `Authorized → Captured` via `IPaymentGateway.CaptureAsync`; emits `PaymentCapturedEvent`. Idempotent if already captured.
- [ ] Non-admin caller receives 403; missing JWT receives 401.
- [ ] Endpoint tests cover role enforcement, idempotency, and illegal-state-transition responses.
- [ ] Manual smoke: after a captured payment, admin refund flips status to `Refunded` and `PaymentRefundedEvent` is observable in RabbitMQ.

---

## Verification (post all phases)

- `dotnet build` solution-wide green.
- `dotnet test` all service test projects green, including new Payment + Order tests.
- `docker compose up --build` from repo root: Payment healthy on `:8007/health/ready`.
- End-to-end happy: order with `.00` total → Authorized → shipment dispatched → Captured → admin refund → Refunded.
- End-to-end failure: order with `.99` total → Failed → Order Cancelled → stock released.
- Jaeger trace spans Order → Inventory → Payment → Order → Shipping → Payment → end.
- Prometheus shows `payments_total` incrementing per checkout.
