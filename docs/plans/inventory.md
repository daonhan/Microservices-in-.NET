# Plan: Inventory Microservice

> Source PRD: [PRD-Inventory.md](../prd/PRD-Inventory.md)

## Architectural decisions

Durable decisions that apply across all phases:

- **Service boundary**: new `inventory-microservice/Inventory.Service` + `Inventory.Tests`, structure mirrors `product-microservice`.
- **Ports & infra**: local port `8005`; docker-compose service `inventory` mapping `8005:8080`; K8s `inventoryservice` deployment + `inventory-clusterip-service` + LoadBalancer YAML.
- **Datastore**: dedicated SQL Server database named `Inventory` on the shared `mssql` container; EF Core Code-First with migrations; `RowVersion` optimistic concurrency on `StockLevel`; retries via `EnableRetryOnFailure`.
- **Routes**: all under `/inventory` at Ocelot. Reads allowed to authenticated users; writes (`POST`/`PUT`) require `user_role: Administrator`. Defense-in-depth `[Authorize(Roles = "Administrator")]` on write endpoints.
  - `GET    /inventory/{productId}`
  - `GET    /inventory`
  - `POST   /inventory/{productId}/restock`
  - `POST   /inventory/{productId}/reserve`
  - `PUT    /inventory/{productId}/threshold`
  - `GET    /inventory/{productId}/movements`
  - `POST   /inventory/{productId}/backorder`
  - `GET    /health`
- **Key domain models**:
  - `StockItem` aggregate keyed by `ProductId` — `TotalOnHand`, `TotalReserved`, `LowStockThreshold`, `RowVersion`; `Available = TotalOnHand − TotalReserved`.
  - `Warehouse` — seeded with a single `DEFAULT` row via EF `HasData()`.
  - `StockLevel` — per-warehouse breakdown (`ProductId`, `WarehouseId`, `OnHand`, `Reserved`).
  - `StockReservation` — keyed by `OrderId`, status `Held | Committed | Released`.
  - `StockMovement` — append-only ledger (`Reserve | Commit | Release | Restock | Adjustment`).
  - `BackorderRequest` — waitlist rows, drained on restock.
- **Eventing**: all outbound events published via the shared outbox (`AddOutbox` + `OutboxBackgroundService`). All event handlers are idempotent by `OrderId`.
  - Published: `StockReservedEvent`, `StockReservationFailedEvent`, `StockCommittedEvent`, `StockReleasedEvent`, `StockDepletedEvent`, `LowStockEvent`, `StockAdjustedEvent`.
  - Consumed: `ProductCreatedEvent` (new, from Product), `OrderCreatedEvent` (extended, from Order), `OrderConfirmedEvent` (new, from Order), `OrderCancelledEvent` (new, from Order).
- **Neighbor-service contract changes** (ship in the same PR series as the phase that needs them):
  - Product: publish `ProductCreatedEvent(ProductId, Name, Price)` from create-product endpoint via outbox.
  - Order: extend `OrderCreatedEvent` with `Items[] { ProductId, Quantity }`; add `OrderConfirmedEvent` + `OrderCancelledEvent`; add `OrderStatus` enum (`PendingStock | Confirmed | Cancelled`); subscribe to `StockReservedEvent` and `StockReservationFailedEvent`.
  - Basket: no code changes.
  - `ECommerce.Shared`: no contract changes; events remain in each service's own `IntegrationEvents/`.
- **Cross-cutting wiring**: `AddSqlServerDatastore`, `AddOutbox`, `AddRabbitMqEventPublisher`, `AddRabbitMqSubscriberService`, `AddEventHandler<TEvent, THandler>`, `AddJwtAuthentication`, `AddOpenTelemetryTracing`, `AddOpenTelemetryMetrics`.
- **Testing**: xUnit + NSubstitute; unit tests for domain, `WebApplicationFactory<Program>` integration tests for HTTP+DB, RabbitMQ integration tests using the `Subscribe<TEvent>()` pattern, 1–2 cross-service saga tests spinning up Order + Inventory factories against a shared broker.

---

## Phase 1: Skeleton service & read-only stock query

**User stories**: 6, 14, 19, 20

### What to build

Scaffold the Inventory microservice end-to-end with enough schema to answer a single read. Create the Service + Tests projects mirroring `product-microservice`, define the `Warehouse`, `StockItem`, and `StockLevel` entities with an EF migration, seed the `DEFAULT` warehouse, and expose `GET /inventory/{productId}` returning `{ productId, totalOnHand, totalReserved, available, threshold, perWarehouse[] }` (or `404` when no row exists yet). Wire `Program.cs` with the full shared-library stack (DB, JWT, outbox, RabbitMQ publisher, OpenTelemetry tracing + metrics) even though only the read path is used, plus `/health`. Add the Dockerfile, docker-compose entry on port `8005`, Kubernetes deployment + ClusterIP + LoadBalancer YAMLs, and the Ocelot `/inventory/{everything}` route. Set up `InventoryWebApplicationFactory` and `IntegrationTestBase` mirroring the Product test harness.

### Acceptance criteria

- [ ] `Inventory.Service` and `Inventory.Tests` projects build and are reachable from the solution.
- [ ] EF migration creates `Warehouses`, `StockItems`, `StockLevels` tables with `DEFAULT` warehouse seeded.
- [ ] `GET /inventory/{productId}` returns `200` with the DTO shape when a row exists, `404` when it doesn't, and `401` when unauthenticated at the gateway.
- [ ] Service starts in docker-compose on `8005` and under Kubernetes using the new YAMLs.
- [ ] `/health` returns `200` under both docker-compose and Kubernetes probes.
- [ ] `Inventory.Tests` runs against an isolated `Inventory_Tests` DB via `WebApplicationFactory<Program>` with at least one passing integration test.

---

## Phase 2: Provision stock from Product catalog

**User stories**: 15

### What to build

Close the loop between product creation and inventory provisioning. Add a `ProductCreatedEvent(ProductId, Name, Price)` record to the Product service and publish it via the outbox from the create-product endpoint. On the Inventory side, subscribe via `AddRabbitMqSubscriberService` + `AddEventHandler<ProductCreatedEvent, …>`, and the handler creates a zero-quantity `StockItem` plus a matching `StockLevel` row in the `DEFAULT` warehouse. The handler is idempotent on `ProductId` so replays are no-ops.

### Acceptance criteria

- [ ] Product service publishes `ProductCreatedEvent` on a successful create; event travels through the outbox.
- [ ] Inventory consumes the event and persists a `StockItem` + `StockLevel(DEFAULT, 0, 0)` row.
- [ ] Replaying the same `ProductCreatedEvent` does not create duplicate rows or throw.
- [ ] After event arrival, `GET /inventory/{productId}` returns `200` with zeroed quantities.
- [ ] RabbitMQ integration test covers the end-to-end flow.

---

## Phase 3: Admin restock, movement ledger, and list

**User stories**: 6, 7, 8, 10

### What to build

Give operators the tools to put real stock into the system and audit it. Add the `StockMovement` append-only ledger entity + migration. Implement `POST /inventory/{productId}/restock` (admin-only): it increments the per-warehouse `StockLevel.OnHand`, appends a `Restock` movement, and publishes `StockAdjustedEvent` via the outbox. Add `GET /inventory` (admin-only list, no paging but hook ready) and `GET /inventory/{productId}/movements` (admin-only ledger). Enforce `[Authorize(Roles = "Administrator")]` on writes plus the Ocelot role claim.

### Acceptance criteria

- [ ] `POST /inventory/{productId}/restock` requires admin role; returns `403` for non-admin, `401` unauthenticated.
- [ ] A successful restock increments `OnHand`, appends exactly one `StockMovement` of type `Restock`, and publishes `StockAdjustedEvent`.
- [ ] `GET /inventory` returns all stock items for admins.
- [ ] `GET /inventory/{productId}/movements` returns movements in chronological order.
- [ ] Unit tests cover restock math on the `StockItem` aggregate; integration tests cover all three endpoints (happy + auth error paths); RabbitMQ test asserts `StockAdjustedEvent`.

---

## Phase 4: Low-stock threshold & depletion events

**User stories**: 9, 13

### What to build

Make the system proactively surface stock problems. Add `PUT /inventory/{productId}/threshold` (admin-only) to set `LowStockThreshold` on the aggregate. Whenever `Available` changes (restock today, reservation flows in Phase 5), the domain emits `LowStockEvent(ProductId, WarehouseId, Available, Threshold)` on a downward crossing and `StockDepletedEvent(ProductId, WarehouseId)` when `Available` hits zero. Events flow through the outbox. No event is emitted on repeated reads below the threshold — only on the crossing.

### Acceptance criteria

- [ ] `PUT /inventory/{productId}/threshold` persists the threshold and requires admin role.
- [ ] Crossing the threshold from above publishes exactly one `LowStockEvent`; staying below does not re-publish.
- [ ] Reaching `Available == 0` publishes exactly one `StockDepletedEvent`.
- [ ] Unit tests cover threshold crossing logic, including edge cases (equal to threshold, restock back above).
- [ ] RabbitMQ integration tests verify both events.

---

## Phase 5: Reservation saga — reserve & commit

**User stories**: 2, 3, 12, 16, 18

### What to build

Wire Inventory into the order lifecycle on the happy path. Extend Order's `OrderCreatedEvent` with `Items[] { ProductId, Quantity }` and introduce an `OrderStatus` enum (`PendingStock | Confirmed | Cancelled`) on the Order aggregate; new orders start as `PendingStock`. Inventory consumes `OrderCreatedEvent`, attempts to reserve every item against `StockLevel` rows (picking warehouses by deterministic priority, v1 = `DEFAULT` only), creates `StockReservation(Status=Held)` rows, appends `Reserve` movements, and publishes `StockReservedEvent` via the outbox; all within a transaction against `StockLevel.RowVersion`. Add an admin/test `POST /inventory/{productId}/reserve` endpoint that drives the same path for staging validation.

Order subscribes to `StockReservedEvent`, transitions the order to `Confirmed`, and publishes the new `OrderConfirmedEvent`. Inventory consumes `OrderConfirmedEvent`, transitions matching reservations `Held → Committed`, decrements `OnHand` (or adjusts per chosen bookkeeping) and appends `Commit` movements, then publishes `StockCommittedEvent`. All handlers are idempotent by `OrderId`.

### Acceptance criteria

- [ ] `OrderCreatedEvent` carries product lines; existing Order consumers still work.
- [ ] Order persists `OrderStatus.PendingStock` on creation and transitions to `Confirmed` on `StockReservedEvent`.
- [ ] Inventory handler for `OrderCreatedEvent` creates `Held` reservations, writes `Reserve` movements, and publishes `StockReservedEvent` atomically via outbox.
- [ ] Inventory handler for `OrderConfirmedEvent` transitions `Held → Committed`, writes `Commit` movements, and publishes `StockCommittedEvent`.
- [ ] Replaying `OrderCreatedEvent` or `OrderConfirmedEvent` for the same `OrderId` is a no-op.
- [ ] `POST /inventory/{productId}/reserve` (admin) exercises the same reservation path.
- [ ] Concurrent reservations that race on the same `StockLevel` are resolved via `RowVersion` retry without over-allocating.
- [ ] Unit tests cover reserve success, commit idempotency; integration tests cover `/reserve` endpoint; one saga E2E test spins up Order + Inventory factories on a shared broker and verifies an order transitions `PendingStock → Confirmed` with committed reservations.

---

## Phase 6: Reservation saga — failure & release

**User stories**: 1, 4, 17

### What to build

Close the compensating half of the saga. When Inventory's `OrderCreatedEvent` handler cannot satisfy all lines, it does not persist reservations; instead it publishes `StockReservationFailedEvent(OrderId, FailedItems[] { ProductId, Requested, Available })`. Order subscribes, transitions the order to `OrderStatus.Cancelled`, and publishes `OrderCancelledEvent`. Inventory consumes `OrderCancelledEvent` and releases any `Held` or `Committed` reservations for that `OrderId` back to available (`Released`), writes `Release` movements, and publishes `StockReleasedEvent`. Releases are idempotent by `OrderId` so repeated cancellations and crossed-wire ordering (cancel arrives before confirm is processed) are safe.

### Acceptance criteria

- [ ] Insufficient stock produces `StockReservationFailedEvent` and writes no `StockReservation` rows or movements.
- [ ] Order handler for `StockReservationFailedEvent` transitions the order to `Cancelled` and publishes `OrderCancelledEvent`.
- [ ] Inventory handler for `OrderCancelledEvent` releases `Held` and `Committed` reservations, writes `Release` movements, and publishes `StockReleasedEvent`.
- [ ] Replaying `OrderCancelledEvent` for an already-released `OrderId` is a no-op.
- [ ] Out-of-order delivery (cancel before confirm) does not leave reservations orphaned.
- [ ] Unit tests cover release idempotency; RabbitMQ tests cover `StockReservationFailedEvent` and `StockReleasedEvent`; one saga E2E test places an order that exceeds available stock and asserts the order ends in `Cancelled`.

---

## Phase 7: Backorder waitlist

**User stories**: 5

### What to build

Give customers a way to wait for restock. Add the `BackorderRequest` entity + migration (`CustomerId`, `ProductId`, `Quantity`, `CreatedAt`, `FulfilledAt?`). Expose `POST /inventory/{productId}/backorder` (authenticated, not admin-only) that records a request. Extend the restock path (Phase 3) so that after `OnHand` increases, the handler drains unfulfilled backorders for that product in FIFO order while `Available` covers them, stamping `FulfilledAt`. No notification consumer is wired — the PRD defers that — but the domain publishes enough signal for a future subscriber via existing events.

### Acceptance criteria

- [ ] `POST /inventory/{productId}/backorder` requires an authenticated user and persists a `BackorderRequest`.
- [ ] A restock that raises `Available` beyond a pending backorder's `Quantity` stamps `FulfilledAt` on that request in FIFO order.
- [ ] A restock that covers only some pending requests fulfills them in order and leaves the rest unfulfilled.
- [ ] Unit tests cover the fulfillment ordering and partial-coverage cases; integration test covers the endpoint + restock-drain flow end-to-end.

---

## Phase 8: Observability polish

**User stories**: 21

### What to build

Make Inventory observable end-to-end. Add custom OpenTelemetry metrics via the existing shared metrics extension: a `stock-movements` counter tagged by `movement_type`, a `reservation-latency` histogram measured around the reservation handler, and a `stock-depleted` counter incremented alongside `StockDepletedEvent`. Confirm traces span the HTTP entry through EF Core and RabbitMQ publish, matching the Product service's telemetry view. Add Prometheus scrape verification in a test.

### Acceptance criteria

- [x] `stock-movements` counter increments on every appended `StockMovement`, correctly tagged by type.
- [x] `reservation-latency` histogram records a sample for every `OrderCreatedEvent` handled.
- [x] `stock-depleted` counter increments whenever `StockDepletedEvent` is published.
- [x] Traces for a reservation show the HTTP/event entry, EF Core spans, and RabbitMQ publish linked under a single trace ID.
- [x] A Prometheus-scrape integration test asserts the three custom metrics are exposed with expected label keys.
