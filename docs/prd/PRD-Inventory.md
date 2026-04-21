# PRD: Inventory Microservice (.NET 8)

## Problem Statement

The current e-commerce platform lets customers add any product to a basket and place orders with no awareness of physical stock. Orders can be accepted for items that don't exist in the warehouse, there is no visibility into on-hand quantities, no audit trail of stock movements, and no way for operators to restock or flag low inventory. As the platform grows toward real fulfillment, the team needs a dedicated bounded context that owns stock levels, reserves units during checkout, commits them on successful order, releases them on cancellation, and broadcasts stock-state changes to the rest of the system.

## Solution

Introduce a new **Inventory microservice** that is the single source of truth for product stock across one or more warehouses. It exposes an HTTP API for admins to query and adjust stock, maintains an immutable movement ledger for auditing, and participates in the existing event-driven saga: it consumes `ProductCreatedEvent` to provision stock rows, consumes `OrderCreatedEvent` to reserve units (hard-committing on success, publishing `StockReservationFailedEvent` to cancel the order otherwise), consumes `OrderCancelledEvent` to release held units, and publishes `StockReservedEvent`, `StockDepletedEvent`, and `LowStockEvent` so other services and operators can react.

The service follows the existing course conventions: ASP.NET Core Minimal APIs, SQL Server + EF Core, the shared `ECommerce.Shared` library for RabbitMQ / outbox / OpenTelemetry / JWT, a dedicated solution folder, Dockerfile, Docker Compose entry, Kubernetes manifests, and a companion `Inventory.Tests` project.

## User Stories

### Customer Perspective

1. As a customer, I want my order to be rejected (and my basket preserved) when items are out of stock, so that I'm not charged for something that can't be fulfilled.
2. As a customer, I want the product I ordered to be decremented from available stock only when my order is confirmed, so that someone else isn't blocked from browsing.
3. As a customer, I want stock reserved for me while my order is in flight, so that a concurrent shopper doesn't take the last unit from under me.
4. As a customer, I want my reserved stock released if my order is cancelled, so that inventory becomes available to others.
5. As a customer, I want to join a waitlist for out-of-stock items (backorder), so that I can be notified when they return.

### Operator / Admin Perspective

6. As an operator, I want to view current stock for any product, so that I can answer customer questions.
7. As an operator, I want to list all inventory with paging/filtering hooks, so that I can audit the warehouse.
8. As an operator, I want to submit a restock adjustment for a product, so that I can record inbound shipments.
9. As an operator, I want to set a low-stock threshold per product, so that I'm alerted before we run out.
10. As an operator, I want a chronological ledger of every stock movement (reserve, commit, release, restock, adjustment), so that I can audit discrepancies.
11. As an operator, I want stock split across multiple warehouses (locations), so that fulfillment can pick from the nearest one.
12. As an operator, I want to trigger a manual reservation for testing/staging, so that I can validate the reservation pipeline without placing a real order.
13. As an operator, I want low-stock and stock-depleted events published on the bus, so that downstream systems (email, dashboards) can subscribe without coupling.

### Developer Perspective

14. As a developer, I want Inventory to be its own microservice with its own SQL Server database, so that the bounded context is isolated.
15. As a developer, I want Inventory to initialize a stock row automatically when `ProductCreatedEvent` arrives, so that catalog and inventory stay in lockstep without manual provisioning.
16. As a developer, I want `OrderCreatedEvent` extended with product IDs and quantities, so that Inventory can perform reservation without calling back to Order.
17. As a developer, I want Inventory to publish `StockReservationFailedEvent` when an order cannot be satisfied, so that Order can compensate (cancel) via saga.
18. As a developer, I want reservation/commit/release to use the existing transactional outbox, so that DB writes and outgoing events are atomic.
19. As a developer, I want Inventory routed through Ocelot under `/inventory/*` with JWT + admin role on write endpoints, so that security matches the rest of the platform.
20. As a developer, I want the Inventory service wired into Docker Compose and Kubernetes using the existing templates, so that local and cluster deploys work identically to other services.
21. As a developer, I want OpenTelemetry tracing and Prometheus metrics wired in (stock-movements counter, reservation-latency histogram), so that Inventory is observable end-to-end.
22. As a developer, I want unit tests for the domain (reserve / commit / release / restock / low-stock), integration tests for the HTTP API, and RabbitMQ integration tests for event pub/sub, so that regressions are caught automatically.

## Implementation Decisions

### Service Boundary

- **New standalone microservice:** `inventory-microservice/Inventory.Service` + `Inventory.Tests`, structure mirrors `product-microservice`.
- **Local HTTP port:** `8005` (next free in the 8000-series).
- **Docker Compose service:** `inventory` mapping `8005:8080`.
- **Database:** dedicated SQL Server database named `Inventory` on the shared `mssql` container.
- **Kubernetes:** `inventoryservice` deployment + `inventory-clusterip-service` + LoadBalancer YAML, added to `kubernetes/`.
- **Gateway route:** `/inventory/{everything}` added to `ocelot.json`; reads allowed to authenticated users, writes (`POST`/`PUT`) require `user_role: Administrator`.

### Domain Model

- **`StockItem` aggregate** keyed by `ProductId` (string/int — align with Product service).
  - Fields: `ProductId`, `TotalOnHand`, `TotalReserved`, `LowStockThreshold`, `RowVersion` (optimistic concurrency).
  - Computed: `Available = TotalOnHand − TotalReserved`.
  - Domain methods: `Reserve(quantity, orderId, warehouseId)`, `Commit(orderId)`, `Release(orderId)`, `Restock(quantity, reason)`, `SetThreshold(value)`.
- **`Warehouse`** entity: `Id`, `Code`, `Name`. Seeded with a single `DEFAULT` warehouse via EF `HasData()` so v1 behaves like single-location without schema churn.
- **`StockLevel`** entity: `ProductId`, `WarehouseId`, `OnHand`, `Reserved` — the per-warehouse breakdown; `StockItem` aggregates across warehouses.
- **`StockReservation`** entity: `OrderId`, `ProductId`, `WarehouseId`, `Quantity`, `Status` (`Held`, `Committed`, `Released`), `CreatedAt`, `UpdatedAt`. Used to reconcile compensation and make release idempotent by `OrderId`.
- **`StockMovement`** append-only ledger: `Id`, `ProductId`, `WarehouseId`, `Quantity` (signed), `MovementType` (`Reserve`, `Commit`, `Release`, `Restock`, `Adjustment`), `OrderId?`, `Reason?`, `OccurredAt`. Never updated or deleted.
- **`BackorderRequest`** entity: `CustomerId`, `ProductId`, `Quantity`, `CreatedAt`, `FulfilledAt?`. Populated when a reservation fails and the customer opts in; drained when restock raises availability.

### Reservation & Saga Flow

- **Model:** reserve on order placement → commit on order confirmation → release on cancellation. No basket-level reservation (explicit decision; keeps Basket service unchanged).
- **Order creation is optimistic:** Order persists the order and publishes `OrderCreatedEvent` (now carrying product lines). Inventory consumes it, attempts reservation, and either publishes `StockReservedEvent` or `StockReservationFailedEvent`. Order subscribes to `StockReservationFailedEvent` and marks the order `Cancelled` (compensating action).
- **Commit trigger:** a new `OrderConfirmedEvent` (published by Order when payment/confirmation succeeds — placeholder until payment exists; for now Order publishes it immediately after successful reservation acknowledgement) causes Inventory to transition `Held → Committed`.
- **Release trigger:** `OrderCancelledEvent` causes Inventory to transition any `Held` or `Committed` reservations for that `OrderId` back to available (`Released`).
- **Idempotency:** all event handlers key on `OrderId`; replays of the same event are no-ops.
- **Concurrency:** EF Core optimistic concurrency (`RowVersion`) on `StockLevel` rows; retries handled by the existing `EnableRetryOnFailure` policy.

### Events

Published by Inventory (all via outbox):

- `StockReservedEvent` — `OrderId`, `Items[]` (`ProductId`, `Quantity`, `WarehouseId`).
- `StockReservationFailedEvent` — `OrderId`, `FailedItems[]` (`ProductId`, `Requested`, `Available`).
- `StockCommittedEvent` — `OrderId`.
- `StockReleasedEvent` — `OrderId`, `Reason`.
- `StockDepletedEvent` — `ProductId`, `WarehouseId`.
- `LowStockEvent` — `ProductId`, `WarehouseId`, `Available`, `Threshold`.
- `StockAdjustedEvent` — `ProductId`, `WarehouseId`, `Delta`, `Reason`.

Consumed by Inventory:

- `ProductCreatedEvent` (**new — requires Product service change**) — initializes a `StockItem` with zero quantities.
- `OrderCreatedEvent` (**extended — requires Order service change**) — triggers reservation.
- `OrderConfirmedEvent` (**new — requires Order service change**) — triggers commit.
- `OrderCancelledEvent` (**new — requires Order service change**) — triggers release.

### Required Changes to Neighboring Services

- **Product service:** publish `ProductCreatedEvent` (`ProductId`, `Name`, `Price`) from the create-product endpoint via outbox; add event record to `IntegrationEvents/`.
- **Order service:** extend `OrderCreatedEvent` with a list of `{ ProductId, Quantity }`; add `OrderConfirmedEvent` and `OrderCancelledEvent`; subscribe to `StockReservedEvent` (→ emit `OrderConfirmedEvent`) and `StockReservationFailedEvent` (→ mark order `Cancelled`, emit `OrderCancelledEvent`). Order gains an `OrderStatus` enum (`PendingStock`, `Confirmed`, `Cancelled`).
- **Basket service:** no code changes required; customers see the failure via order status rather than at basket time.
- **Shared library:** event records and handler wiring remain in each service's own `IntegrationEvents/`; no change to `ECommerce.Shared` contracts is required unless the version is bumped to distribute shared event types (recommended: keep events per-service to match existing pattern).

### HTTP API

All routes under `/inventory` at the gateway.

- `GET /inventory/{productId}` — returns `{ productId, totalOnHand, totalReserved, available, threshold, perWarehouse[] }`. Authenticated.
- `GET /inventory` — admin-only, returns a list (no paging in v1; hook ready).
- `POST /inventory/{productId}/restock` — admin-only. Body: `{ warehouseId, quantity, reason }`. Writes a `Restock` movement.
- `POST /inventory/{productId}/reserve` — admin/test-only. Body: `{ orderId, warehouseId, quantity }`.
- `PUT /inventory/{productId}/threshold` — admin-only. Body: `{ threshold }`.
- `GET /inventory/{productId}/movements` — admin-only ledger view.
- `POST /inventory/{productId}/backorder` — authenticated. Body: `{ customerId, quantity }`.
- Standard `/health` probe for K8s.

### Datastore & Infrastructure

- SQL Server + EF Core Code-First with migrations; DbContext registered via `AddSqlServerDatastore()` with the existing retry policy.
- Outbox wired via `AddOutbox()`; background publisher reuses `OutboxBackgroundService`.
- RabbitMQ publisher via `AddRabbitMqEventPublisher()`; subscriber via `AddRabbitMqSubscriberService()` + `AddEventHandler<TEvent, THandler>()` for each consumed event.
- JWT via `AddJwtAuthentication()`; admin role enforced at the gateway, defense-in-depth via `[Authorize(Roles = "Administrator")]` on write endpoints.
- OpenTelemetry tracing + metrics via existing shared extensions. Custom metrics: `stock-movements` counter tagged by `movement_type`, `reservation-latency` histogram, `stock-depleted` counter.

## Testing Decisions

- **What makes a good test:** verify external behavior — HTTP response + DB state + events published — not internal method calls. Avoid asserting on private fields or on the exact SQL emitted.
- **Unit tests (`Inventory.Tests`, xUnit + NSubstitute):** domain logic on the `StockItem` aggregate and reservation service — reserve success/failure, commit idempotency, release idempotency, low-stock threshold crossing, restock math, backorder creation on failure. Naming: `Given_When_Then`. Mock `IStockStore`, `IEventBus`, `IOutboxStore`.
- **Integration tests (HTTP + DB):** `WebApplicationFactory<Program>` pattern from `ProductWebApplicationFactory`, `appsettings.Tests.json`, `IAsyncLifetime` to provision/drop the test DB. Cover each endpoint's happy + error paths and verify DB state via `InventoryContext`.
- **RabbitMQ integration tests:** reuse the `EventingBasicConsumer` pattern from `IntegrationTestBase.Subscribe<TEvent>()`. Scenarios: `OrderCreatedEvent` with sufficient stock produces `StockReservedEvent`; insufficient stock produces `StockReservationFailedEvent`; `OrderCancelledEvent` releases held units; `ProductCreatedEvent` seeds a stock row.
- **Saga / cross-service tests:** one end-to-end test spinning up Order + Inventory via their respective `WebApplicationFactory` instances sharing the RabbitMQ test broker — placing an order whose stock is unavailable results in order status `Cancelled`. Kept minimal (1–2 scenarios) due to wiring cost.
- **Prior art:** `Product.Tests/IntegrationTestBase.cs`, `ProductWebApplicationFactory.cs`, `Order.Tests/` for API+event coverage patterns.

## Out of Scope

- Payment processing and the `OrderConfirmedEvent` trigger wired to real payment (v1 emits it synthetically after successful reservation).
- Customer-facing notifications for backorder fulfillment (event is published; no email/SMS consumer).
- Inventory forecasting, reorder-point automation, supplier integration.
- Per-warehouse routing logic ("nearest warehouse"); v1 picks warehouses by a deterministic priority order.
- Multi-currency, price validation, tax.
- Performance/load testing and horizontal replica tuning.
- UI / admin dashboard.
- Migration of existing product catalog into seeded stock rows (v1 relies on `ProductCreatedEvent` going forward; a one-time backfill script is left to operators).
- Basket-level soft reservations (explicitly rejected in favor of order-time reservation).

## Further Notes

- Backorders and multi-warehouse are included per user request but carry the most schema risk; both are encapsulated behind the `StockLevel` / `BackorderRequest` entities so they can be disabled by seeding only the `DEFAULT` warehouse and ignoring backorder endpoints without touching the domain core.
- The cross-service contract changes (`OrderCreatedEvent` extension, new `ProductCreatedEvent`, new `OrderConfirmedEvent` / `OrderCancelledEvent`) are the highest-risk items and should land in the same PR series as the Inventory service itself to avoid partial deployments.
- Security key management, NuGet feed, and CI/CD remain as-is per the platform PRD — no new secrets or pipelines introduced beyond what Inventory needs (connection string, JWT issuer, RabbitMQ host).
- Port 8005, database name `Inventory`, deployment name `inventoryservice`, service name `inventory-clusterip-service`.
