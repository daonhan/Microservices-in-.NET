# Plan: Shipping Service

> Source PRD: #6 ([docs/prd/PRD-Shipping.md](../prd/PRD-Shipping.md))

## Architectural decisions

Durable decisions that apply across all phases:

- **Routes** (all behind API Gateway at `/shipping/...`):
  - `GET /shipping/by-order/{orderId}` — customer + admin (ownership check)
  - `GET /shipping/{shipmentId}` — customer + admin (ownership check)
  - `GET /shipping` — admin only (filters: status, warehouse, date range)
  - `POST /shipping/{id}/pick` | `/pack` | `/dispatch` | `/deliver` | `/fail` | `/return` | `/cancel` — admin
  - `GET  /shipping/{id}/quotes` — admin (rate-shop preview)
  - `POST /shipping/webhooks/carrier/{carrierKey}` — unauthenticated by JWT; per-carrier shared-secret header
  - `GET /health/live`, `GET /health/ready` — shared extensions
- **Schema** (own `Shipping` database on shared SQL Server):
  - `Shipment` (aggregate root): `Id`, `OrderId`, `CustomerId`, `WarehouseId`, `Status`, `CarrierKey?`, `TrackingNumber?`, `ShippingAddress?`, `LabelRef?`, `QuotedPrice?`, timestamps
  - `ShipmentLine`: `ShipmentId`, `ProductId`, `Quantity`
  - `ShipmentStatusHistory` (append-only): `ShipmentId`, `Status`, `OccurredAt`, `Source (System|Admin|CarrierPoll|CarrierWebhook)`, `Reason?`
  - `Warehouse` (reference): `Id`, `Code`, `Name`, `OriginAddress` — seeded at migration time
  - Shared `Outbox` table (same pattern as Order/Inventory)
- **Key models**: `Shipment` aggregate owns the state machine; `ICarrierGateway` port for all carrier I/O; `RateShoppingService` orchestrates quoting; `CarrierPollingService` is the only background worker for in-transit progression.
- **State machine**: `Pending → Picked → Packed → Shipped → InTransit → Delivered`; `Pending|Picked|Packed → Cancelled`; `Shipped|InTransit → Failed|Returned`. Terminal: `Delivered`, `Cancelled`, `Failed`, `Returned`.
- **Auth**: JWT via shared `AddJwtAuthentication`; customer ownership enforced via `customerId` claim; mutations require `Administrator` role (enforced at Ocelot + in-service). Webhook endpoints use per-carrier shared-secret header from configuration.
- **Messaging**: RabbitMQ fanout `ecommerce-exchange`; routing key = event type name. All outbound events published via transactional outbox in the same EF Core transaction as the state change.
- **Events consumed**: `OrderConfirmedEvent`, `OrderCancelledEvent`, `StockCommittedEvent` (for `WarehouseId`).
- **Events published**: milestone events (`ShipmentCreatedEvent`, `ShipmentDispatchedEvent`, `ShipmentDeliveredEvent`, `ShipmentCancelledEvent`, `ShipmentFailedEvent`, `ShipmentReturnedEvent`) **plus** generic `ShipmentStatusChangedEvent` on every transition.
- **Observability**: shared `AddPlatformObservability` (OTLP traces/metrics/logs) + `AddPlatformHealthChecks`.
- **Infra**: new `kubernetes/shipping-microservice.yaml`; `docker-compose.yaml` entry; Ocelot route in `api-gateway/ApiGateway/ocelot.json`.

---

## Phase 1: Skeleton service — shipment created on order confirmation

**User stories**: 1, 3, 14, 19, 20, 21

### What to build

Stand up a new `shipping-microservice/Shipping.Service` following the canonical template with its own `Shipping` database, EF Core, migrations, transactional outbox, RabbitMQ subscriber, JWT auth, shared observability, and health checks. Register an `OrderConfirmedEventHandler` that writes a `Pending` `Shipment` with order lines and a `WarehouseId` (derived from `StockCommittedEvent`; split-across-warehouses → one shipment per warehouse) and emits `ShipmentCreatedEvent` + `ShipmentStatusChangedEvent` via outbox. Expose `GET /shipping/by-order/{orderId}` (admin only for now) returning the shipment. Add the Ocelot route, k8s manifest, and docker-compose entry so the service is routable end-to-end.

### Acceptance criteria

- [ ] New `shipping-microservice/Shipping.Service` project builds and runs; solution includes `Shipping.Tests`.
- [ ] Publishing an `OrderConfirmedEvent` (with `StockCommittedEvent` context) results in one `Shipment` per warehouse in `Pending` state with the correct `OrderId`, `CustomerId`, lines, `WarehouseId`.
- [ ] Outbox contains `ShipmentCreatedEvent` and `ShipmentStatusChangedEvent` rows in the same transaction as the `Shipment` insert.
- [ ] `GET /shipping/by-order/{orderId}` (admin token) returns the created shipments; unauthenticated returns 401.
- [ ] `/health/live` and `/health/ready` respond correctly; readiness fails when SQL or RabbitMQ is down.
- [ ] Ocelot routes `/shipping/{everything}` to the service; k8s manifest and docker-compose entry present and boot locally.
- [ ] `Warehouse` table seeded with at least two entries (e.g. `WH-EAST`, `WH-WEST`) via migration.

---

## Phase 2: `Shipment` aggregate + customer read with ownership

**User stories**: 2, 3, 15, 18

### What to build

Introduce the `Shipment` aggregate that owns the full state machine (`TryPick`, `TryPack`, `TryDispatch`, `TryMarkInTransit`, `TryDeliver`, `TryFail`, `TryReturn`, `TryCancel`), append-only status history, and terminal-state guards. Replace Phase 1's ad-hoc field writes with aggregate calls. Allow customers to call `GET /shipping/by-order/{orderId}` and `GET /shipping/{shipmentId}` with ownership enforced via the `customerId` claim; admins see everything.

### Acceptance criteria

- [ ] `Shipment` aggregate exists with a small public surface; every transition method returns a result indicating legality.
- [ ] Pure unit tests cover every legal transition, every illegal transition, and terminal-state rejection; status history is appended with correct `Source`.
- [ ] `GET /shipping/by-order/{orderId}` and `GET /shipping/{id}` return 200 for the owning customer, 403 for a different customer, and 200 for admins.
- [ ] Phase 1 handler now uses the aggregate; no direct field mutation remains.
- [ ] All previous Phase 1 acceptance criteria still pass.

---

## Phase 3: Manual fulfillment — pick, pack, admin listing, order-cancelled handler

**User stories**: 4, 5, 11 (cancel portion), 12, 16

### What to build

Add admin endpoints `POST /shipping/{id}/pick`, `POST /shipping/{id}/pack`, and `POST /shipping/{id}/cancel`. Register an `OrderCancelledEventHandler` that cancels any existing shipment for the order when the current state allows it, emitting `ShipmentCancelledEvent` + `ShipmentStatusChangedEvent`. Add `GET /shipping` (admin only) with filters for `status`, `warehouseId`, and a date range.

### Acceptance criteria

- [ ] Pick/pack/cancel endpoints require admin role; non-admin callers receive 403.
- [ ] Pick/pack transitions are persisted and produce `ShipmentStatusChangedEvent` in the outbox; illegal transitions return 409 with a domain-error payload.
- [ ] `OrderCancelledEvent` published externally results in matching shipments being cancelled (when in a cancellable state); already-terminal shipments are left untouched (observable via history/logs).
- [ ] `GET /shipping` supports filters and paginates; returns only shipments matching the filter.
- [ ] Integration tests use the `WebApplicationFactory` pattern for each new endpoint (success, forbidden, illegal transition).

---

## Phase 4: `ICarrierGateway` + fake carriers + rate shopping + dispatch

**User stories**: 6, 7, 8, 17, 22

### What to build

Introduce `ICarrierGateway` with `QuoteAsync`, `DispatchAsync`, and `GetStatusAsync`. Implement `FakeExpressCarrierGateway` and `FakeGroundCarrierGateway` with deterministic tracking numbers, deterministic PNG label bytes, and simulated status transitions. Add `RateShoppingService` that quotes all registered carriers and ranks by cheapest then fastest. Expose `GET /shipping/{id}/quotes` (ranked preview) and `POST /shipping/{id}/dispatch` (body: `carrierKey`, `shippingAddress`, optional `overrideQuote`). Dispatch stores the carrier, tracking number, label reference, and address on the `Shipment`, transitions it to `Shipped`, and emits `ShipmentDispatchedEvent` + `ShipmentStatusChangedEvent`.

### Acceptance criteria

- [ ] Two fake carriers are registered at startup; their quotes differ enough to make rate shopping observable.
- [ ] `GET /shipping/{id}/quotes` returns the ranked list of quotes from all registered carriers.
- [ ] `POST /shipping/{id}/dispatch` succeeds only from `Packed`; stores carrier/tracking/label/address; emits dispatched + status-changed events via outbox.
- [ ] Dispatch without prior `Packed` state returns 409.
- [ ] `ICarrierGateway` contract test suite runs against both fake carriers and passes.
- [ ] Admin-only enforcement confirmed for both endpoints.

---

## Phase 5: In-transit tracking — polling + webhook

**User stories**: 9, 10

### What to build

Add `CarrierPollingService` (`BackgroundService`) that periodically scans active shipments in `Shipped` or `InTransit`, calls `ICarrierGateway.GetStatusAsync`, and advances the aggregate accordingly. Use `TimeProvider` so tests are deterministic. Add `POST /shipping/webhooks/carrier/{carrierKey}` that validates a per-carrier shared-secret header, parses the payload via a gateway-supplied parser, and applies the update through the aggregate. Both paths tag status history with the correct `Source` and publish `ShipmentStatusChangedEvent` plus any applicable milestone event.

### Acceptance criteria

- [ ] Polling service ticks on schedule; advances shipments based on fake-carrier simulated progression; emits appropriate events.
- [ ] Integration test uses `TimeProvider` to drive the polling service without wall-clock dependency.
- [ ] Webhook endpoint returns 401 with missing/invalid shared secret; 200 with valid secret and payload.
- [ ] Webhook updates appear in status history with `Source=CarrierWebhook`; poll updates with `Source=CarrierPoll`.
- [ ] Contradicting webhook updates on terminal shipments are rejected gracefully (no crash, no state regression).

---

## Phase 6: Terminal outcomes + custom metrics

**User stories**: 11, 23

### What to build

Add admin endpoints `POST /shipping/{id}/deliver`, `POST /shipping/{id}/fail` (requires `reason`), and `POST /shipping/{id}/return` (requires `reason`) that transition the aggregate, capture the reason in status history, and emit the corresponding milestone event plus `ShipmentStatusChangedEvent`. Wire custom metrics into shared observability: `shipments_total{status}` counter, `time_to_dispatch_seconds` histogram, `time_to_delivery_seconds` histogram, `rate_shopping_quote_spread` histogram.

### Acceptance criteria

- [ ] All three endpoints require admin role and valid source state; illegal transitions return 409.
- [ ] `fail` and `return` persist `Reason` on status history and on the emitted event payload.
- [ ] All four metrics are emitted and visible in the OTLP export; histograms are populated with realistic values from integration tests.
- [ ] Happy-path end-to-end test (order confirmed → picked → packed → dispatched → in-transit → delivered) passes with all events and metrics observed.

---

## Phase 7: Event-stream consolidation audit

**User stories**: 13

### What to build

Audit all aggregate transition call sites and ensure the canonical pair — milestone event (where applicable) + `ShipmentStatusChangedEvent` — is published consistently. Add an integration test that asserts the full expected event stream for a happy-path shipment and for a cancelled-after-confirmed shipment.

### Acceptance criteria

- [ ] Every transition in the codebase publishes `ShipmentStatusChangedEvent`; milestone transitions additionally publish their specific event.
- [ ] Integration test asserts the exact ordered sequence of published events for: (a) happy path through `Delivered`, (b) cancellation after `OrderCancelledEvent`.
- [ ] No transition path is missing from the audit (checked via code review + test coverage of each transition method).
