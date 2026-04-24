# PRD: Shipping Service — Post-Confirmation Fulfillment & Tracking

## Problem Statement

Today, once an order is confirmed (stock committed by the Inventory service), nothing physically happens. The order sits in a "Confirmed" state forever — there is no fulfillment workflow, no carrier integration, no tracking number, and no way for a customer to know where their package is. Operations staff have no system-of-record for picking, packing, dispatching, and monitoring shipments, and analytics/notifications have no signal that anything has shipped or been delivered.

## Solution

Introduce a new **Shipping microservice** that takes over the order lifecycle after confirmation. It listens for `OrderConfirmedEvent`, creates a `Shipment` aggregate bound to the originating order and warehouse, drives the shipment through a well-defined state machine (`Pending → Picked → Packed → Shipped → InTransit → Delivered`, plus `Cancelled` / `Failed` / `Returned`), and integrates with pluggable carriers through an `ICarrierGateway` abstraction. Rate shopping picks the cheapest/fastest carrier at dispatch time, labels are generated through the same abstraction, and carrier-driven status updates arrive via both a polling background service and an inbound webhook endpoint. Customers can query shipment status by order id; admins/ops drive transitions and handle exceptions. Each transition emits both a specific milestone integration event and a generic `ShipmentStatusChangedEvent`.

## User Stories

1. As a **customer**, I want a shipment to be automatically created as soon as my order is confirmed, so that I never have to wait for a human to kick off fulfillment.
2. As a **customer**, I want to look up the status and tracking number for my order, so that I know when to expect delivery.
3. As a **customer**, I want the system to remember which order a shipment belongs to, so that one API call with my order id returns all relevant tracking info.
4. As a **warehouse picker (admin)**, I want to mark a shipment as `Picked`, so that the system records that items have been pulled from the shelves.
5. As a **packer (admin)**, I want to mark a shipment as `Packed`, so that subsequent carrier handoff can happen only on packed shipments.
6. As an **ops dispatcher (admin)**, I want the system to rate-shop across all registered carriers when I dispatch a shipment, so that I get the best combination of cost and speed without manual comparison.
7. As an **ops dispatcher (admin)**, I want a shipping label generated at dispatch time, so that I can print and attach it to the package.
8. As an **ops dispatcher (admin)**, I want the carrier and tracking number stored on the shipment at dispatch time, so that tracking becomes authoritative.
9. As a **carrier (external system)**, I want to POST tracking updates to a webhook, so that the shipment reflects real-world movement without manual intervention.
10. As an **ops manager (admin)**, I want the system to poll carriers for status on active in-transit shipments on a schedule, so that we still get updates when a carrier has no webhook support.
11. As an **ops manager (admin)**, I want to manually transition a shipment to `Delivered`, `Failed`, or `Returned` when needed, so that broken carrier feeds don’t block the workflow.
12. As an **ops manager (admin)**, I want to cancel a shipment when the underlying order is cancelled after confirmation, so that fulfillment doesn’t ship something the customer already refunded.
13. As a **downstream service (notifications, analytics)**, I want both granular milestone events (`ShipmentDispatchedEvent`, `ShipmentDeliveredEvent`, …) and a generic `ShipmentStatusChangedEvent`, so that I can subscribe at the granularity that fits my use case.
14. As an **ops manager (admin)**, I want a shipment to know which warehouse it originated from, so that we can report on and troubleshoot fulfillment per facility.
15. As a **customer**, I want to be prevented from seeing someone else’s shipment, so that my data is private.
16. As an **admin**, I want to list all shipments in a given state (e.g. stuck in `Picked` for too long), so that I can find and unblock exceptions.
17. As a **developer**, I want carrier integration isolated behind one interface, so that adding a new carrier doesn't require touching the domain or endpoints.
18. As a **developer**, I want the `Shipment` aggregate to enforce its own state transition rules, so that invalid transitions (e.g. `Pending → Delivered`) can never be persisted, regardless of caller.
19. As an **SRE**, I want Shipping to expose the same liveness/readiness probes, metrics, and traces as every other service, so that it plugs into existing dashboards.
20. As a **developer**, I want outbound integration events published via the existing transactional outbox, so that a crash mid-transition never leaves the system in an inconsistent state.
21. As a **customer-facing frontend**, I want Shipping reachable through the API Gateway at `/shipping/{…}`, so that the client uses the same base URL as all other services.
22. As an **ops dispatcher (admin)**, I want to preview the rate-shopping quotes before committing a dispatch, so that I can override the automatic choice if needed.
23. As an **ops manager (admin)**, I want a failed shipment to capture the failure reason and carrier error, so that retries/claims have the context they need.

## Implementation Decisions

### New service: `shipping-microservice/Shipping.Service`

Follows the canonical template (`ApiModels`, `Endpoints`, `Infrastructure/Data/EntityFramework`, `IntegrationEvents/{Events,EventHandlers}`, `Models`, `Migrations`, `Program.cs`). SQL Server + EF Core, own `Shipping` database, own migrations, same outbox pattern, same `AddRabbitMqEventBus` + `AddRabbitMqSubscriberService` + `AddEventHandler` composition in `Program.cs`, same JWT auth and shared observability/health check extensions.

### Modules

- **`Shipment` aggregate (deep module, pure domain)**: encapsulates the full state machine. Public surface is small (`TryPick`, `TryPack`, `TryDispatch(carrier, tracking, label)`, `TryMarkInTransit(update)`, `TryDeliver`, `TryFail(reason)`, `TryReturn(reason)`, `TryCancel`). Each guard returns a result that reports whether the transition was legal. The aggregate owns its line items, carrier assignment, tracking number, warehouse id, and an append-only status history.
- **`ICarrierGateway` (deep module, one interface)**: the only surface the rest of the service uses to talk to carriers. Methods: `QuoteAsync(ShipmentQuoteRequest)`, `DispatchAsync(ShipmentDispatchRequest) → CarrierDispatchResult (tracking# + label bytes)`, `GetStatusAsync(trackingNumber) → CarrierStatus`. Registered as a keyed/multi-instance DI service; callers enumerate all registered carriers for rate shopping.
- **`RateShoppingService` (thin orchestration)**: takes a shipment + list of `ICarrierGateway`, calls `QuoteAsync` on each, picks the winner by a simple policy (cheapest, tiebreak fastest). Returns the ranked list so ops can preview.
- **`FakeExpressCarrierGateway` + `FakeGroundCarrierGateway`**: two in-process fake implementations so rate-shopping is demonstrable. Both generate deterministic fake labels (PNG bytes) and deterministic tracking numbers and simulate status transitions over time.
- **`CarrierPollingService` (`BackgroundService`)**: on a schedule, finds active shipments whose state is `Shipped` or `InTransit`, calls `ICarrierGateway.GetStatusAsync`, and applies updates via the aggregate. Uses existing `IServiceScopeFactory` pattern from `OutboxBackgroundService`.
- **Webhook endpoint**: `POST /shipping/webhooks/carrier/{carrierKey}` parses a carrier-specific payload (each gateway can supply a parser) and applies the update via the aggregate. Authenticated via a shared secret header, not the Bearer JWT.
- **`OrderConfirmedEventHandler`**: creates a `Shipment` in `Pending`, copies order lines, records `WarehouseId` (see event-schema decision), writes `ShipmentCreatedEvent` + `ShipmentStatusChangedEvent` to the outbox in the same transaction.
- **`OrderCancelledEventHandler`**: transitions any existing shipment for that order to `Cancelled` (if still in a cancellable state) and emits the corresponding events.

### State machine

- **States**: `Pending`, `Picked`, `Packed`, `Shipped`, `InTransit`, `Delivered`, `Cancelled`, `Failed`, `Returned`.
- **Legal transitions**:
  - `Pending → Picked → Packed → Shipped → InTransit → Delivered`
  - `Pending | Picked | Packed → Cancelled` (before handoff)
  - `Shipped | InTransit → Failed | Returned` (after handoff, with reason)
- `Delivered`, `Cancelled`, `Failed`, `Returned` are terminal. Any attempt to transition out of terminal states returns a domain error.
- Status history is an append-only child collection: `(Status, OccurredAt, Source: System|Admin|CarrierPoll|CarrierWebhook, Reason?)`.

### Events

- **Consumes**: `OrderConfirmedEvent`, `OrderCancelledEvent`, `StockCommittedEvent` (to learn `WarehouseId` — see below).
- **Publishes (milestone)**: `ShipmentCreatedEvent`, `ShipmentDispatchedEvent` (includes carrier, tracking number), `ShipmentDeliveredEvent`, `ShipmentCancelledEvent`, `ShipmentFailedEvent`, `ShipmentReturnedEvent`.
- **Publishes (generic)**: `ShipmentStatusChangedEvent` on every transition (including non-milestone ones like `Picked`, `Packed`, `InTransit`). Contains `ShipmentId`, `OrderId`, `FromStatus`, `ToStatus`, `OccurredAt`.
- All outbound events go through the existing transactional outbox in the same DB transaction as the state change.

### Address handling (v1)

The shipping address is **not carried on `OrderConfirmedEvent` today**. v1 accepts the address as input to `POST /shipping/{id}/dispatch`; ops supplies it (and it is stored on the `Shipment` aggregate). Extending `OrderCreatedEvent`/`OrderConfirmedEvent` with a `ShippingAddress` is deferred as a follow-up that will naturally simplify dispatch.

### Warehouse handling

- Introduces a `Warehouse` entity in Shipping (`Id`, `Code`, `Name`, `OriginAddress`). Seeded at migration time for dev.
- `Shipment.WarehouseId` is populated from `StockCommittedEvent` (Inventory already tracks `WarehouseId` per line). If the stock was split across warehouses, v1 creates **one shipment per warehouse** (many shipments per order allowed).

### HTTP API (all behind `/shipping/{…}` via Ocelot)

- `GET /shipping/by-order/{orderId}` — customer & admin. Customer only sees their own orders (claim-based check on `customerId`).
- `GET /shipping/{shipmentId}` — customer & admin, with same ownership check.
- `GET /shipping` — admin only. Filter by status, warehouse, date range.
- `POST /shipping/{id}/pick` — admin.
- `POST /shipping/{id}/pack` — admin.
- `GET  /shipping/{id}/quotes` — admin. Returns ranked rate-shopping result.
- `POST /shipping/{id}/dispatch` — admin. Body: `{ carrierKey, shippingAddress, overrideQuote? }`. Triggers `ICarrierGateway.DispatchAsync`, stores label bytes (or reference), emits `ShipmentDispatchedEvent`.
- `POST /shipping/{id}/deliver` — admin (manual override for broken carrier feeds).
- `POST /shipping/{id}/fail` — admin. Body: `{ reason }`.
- `POST /shipping/{id}/return` — admin. Body: `{ reason }`.
- `POST /shipping/{id}/cancel` — admin; also invoked programmatically from the `OrderCancelledEvent` handler.
- `POST /shipping/webhooks/carrier/{carrierKey}` — unauthenticated-by-JWT but requires a shared-secret header (per-carrier config).
- Health: `/health/live`, `/health/ready` (shared extensions).

### Infra / ops

- New `kubernetes/shipping-microservice.yaml` modelled on `kubernetes/order-microservice.yaml` (deployment, ClusterIP, optional LoadBalancer, probes, env vars for `RabbitMq__HostName`, `ConnectionStrings__Default`, `OpenTelemetry__OtlpExporterEndpoint`).
- New `Shipping` database on shared SQL Server.
- Ocelot route added in `api-gateway/ApiGateway/ocelot.json` pointing `/shipping/{everything}` to `shipping-clusterip-service:8080`, with admin-only routes declaring `"RouteClaimsRequirement": {"user_role": "Administrator"}`.
- `docker-compose.yaml` gets a `shipping-service` section mirroring other services.
- Custom metrics: `shipments_total{status}` counter, `time_to_dispatch_seconds` histogram, `time_to_delivery_seconds` histogram, `rate_shopping_quote_spread` histogram.

## Testing Decisions

**Principle**: tests exercise external behavior of each module — public aggregate API, HTTP endpoints, event bus interactions, carrier gateway contract. Tests should not reach into EF internals, RabbitMQ internals, or private methods of the aggregate.

**Modules under test**:

1. **`Shipment` aggregate — pure unit tests.** Every legal transition succeeds, every illegal transition returns an error, terminal states reject further transitions, status history is appended in order. Prior art: domain guard tests are a new category in this repo — the closest equivalent is endpoint-level validation tests such as `AuthApiEndpointsTests`. Shipment unit tests can live next to it stylistically but without `WebApplicationFactory`.
2. **`OrderConfirmedEventHandler` — integration test.** In-memory or test-container broker; publish an `OrderConfirmedEvent`, assert a `Shipment` row exists in `Pending` and `ShipmentCreatedEvent` + `ShipmentStatusChangedEvent` rows land in the outbox. Prior art: Inventory's `OrderConfirmedEventHandler` and its accompanying tests.
3. **Shipping HTTP endpoints — integration tests** using the `WebApplicationFactory` pattern. Prior art: `AuthApiEndpointsTests` and the Inventory `Api/*Tests.cs` files. Covers success, unauthorized (customer reading another user's shipment), forbidden (customer calling admin endpoint), and illegal transition (e.g. `dispatch` before `pack`).
4. **`ICarrierGateway` contract tests.** A single shared test suite run against both `FakeExpressCarrierGateway` and `FakeGroundCarrierGateway` to enforce consistent `QuoteAsync`/`DispatchAsync`/`GetStatusAsync` behavior. Future real carriers must satisfy the same suite.
5. **`CarrierPollingService` — integration test.** Seed an active shipment, let the service tick, assert the shipment advances state and that the corresponding events appear in the outbox. Use a time abstraction (`TimeProvider`) so tests are not wall-clock-dependent.

## Out of Scope

- Real carrier integrations (UPS, FedEx, DHL, USPS). v1 ships only fake carriers; the abstraction is designed so real adapters are additive.
- Extending `OrderConfirmedEvent` with a shipping address. v1 accepts address at dispatch time; schema change is a follow-up.
- Customer-facing label self-service, return label generation, return pickup scheduling.
- Multi-package shipments (one shipment = one package in v1). Splitting by warehouse is supported, but each warehouse's shipment is a single package.
- International shipping, customs forms, duties, restricted-items checks.
- Pricing passed back to Order/Billing. v1 records the quoted price on the shipment but does not feed it into invoicing.
- End-customer notifications (email/SMS). A separate Notifications service can subscribe to the emitted events.
- Returns/RMA workflow beyond marking a shipment `Returned` with a reason — no return authorization, inspection, or restocking flows.
- Warehouse management (inventory placement, reorder). Warehouses are a reference entity only.
- Rate-shopping policies beyond "cheapest, tiebreak fastest". Weighted policies, customer preferences, SLA rules are future work.

## Further Notes

- `ICarrierGateway` is the architectural keystone. Keep it narrow — if a real-carrier feature (e.g. scheduled pickup) doesn't fit, add a new interface rather than bloating this one.
- The webhook endpoint's shared-secret should be per-carrier and stored in configuration; do not hardcode.
- Outbox + aggregate transition must share the same EF Core transaction, exactly as Inventory's `OrderConfirmedEventHandler` does.
- The `ShipmentStatusChangedEvent` stream is the recommended subscription for generic downstream consumers; milestone events exist for consumers that only care about specific transitions.
- Migrations seed at least two warehouses (e.g. `WH-EAST`, `WH-WEST`) and register both fake carriers so the stack is demoable on first run.
