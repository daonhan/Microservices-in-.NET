# Plan: QA Dump Dataset for Manual End-to-End Verification

> Source PRD: [docs/prd/PRD-Qa-Dump-Dataset.md](../prd/PRD-Qa-Dump-Dataset.md) · Tracking issue: https://github.com/daonhan/Microservices-in-.NET/issues/72

## Architectural decisions

Durable choices that apply across every phase. Reference these from each slice rather than re-deciding.

- **Format**: reference data via EF Core `HasData` (per service `*ContextSeed`/`IEntityTypeConfiguration<T>`). Basket — no SQL store — uses a Redis hosted seeder. No standalone CLI tool, no SQL dump files, no compose init scripts.
- **Lifecycle**: auto-seed when `app.Environment.IsDevelopment()`. In all other environments seed only when `Qa:Seed=true` (env: `Qa__Seed=true`). Default off. Reset = `docker compose down -v && docker compose up --build`.
- **Identity**: stable, hard-coded `Guid` constants in a single shared `QaPersonas` static class (in `ECommerce.Shared`). Every service imports IDs from here — no duplicated literals.
- **Personas**: existing admin (`microservices@daonhan.com`, `Administrator`) plus three customers — `customer-happy@qa.test`, `customer-decline@qa.test`, `customer-cancel@qa.test`. Plaintext passwords appear only in `docs/qa/README.md`; `HasData` stores PBKDF2 hashes (matches existing admin row).
- **Pricing convention**: `*.00` cents for any case that must succeed; `*.99` cents for the payment-decline case (existing `InMemoryPaymentGateway` rule, no gateway code change).
- **Verification depth in runbook**: every step shows four checks — HTTP status/body, SQL `SELECT` snippet, RabbitMQ/log assertion, optional Jaeger trace.
- **Layout**:
  - Shared: `shared-libs/ECommerce.Shared/Qa/` — `QaPersonas`, `QaSeedingExtensions`.
  - Per service: extend existing `Infrastructure/Data/EntityFramework/*ContextSeed.cs` (Order has none yet — add mirroring Payment/Shipping pattern).
  - Basket: new `basket-microservice/Basket.Service/Infrastructure/Seeding/RedisQaSeederHostedService.cs`; new `Basket.Tests/` project (currently missing).
  - QA artefacts: `qa/bruno/` (collection root), `docs/qa/README.md`, `docs/qa/scenarios/01-happy-path.md` … `04-admin-ops.md`.
- **Migration naming**: `YYYYMMDD_SeedQaData_<service>` per service. `HasData` re-application is idempotent.
- **Wiring point**: each service `Program.cs` calls `app.SeedQaData()` after `ApplyMigrationsAsync()` — gated inside the extension so callers never branch on environment themselves.
- **Test layers**: shared lib unit test for the gating contract; per-service integration test using `WebApplicationFactory<Program>` that hits a representative endpoint and asserts the seeded entity is reachable end-to-end (not "the seeder called SaveChanges").

---

## Phase 1: Tracer bullet — happy-path skeleton

**User stories**: 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 20, 22, 23, 25.

### What to build

The thinnest end-to-end vertical slice that lets QA log in as `customer-happy@qa.test`, find a pre-seeded basket containing `product-happy`, place an order, and watch the saga complete through to a delivered shipment — all driven by Bruno requests and a runbook page, with the stack started via `docker compose up --build`.

This phase introduces the shared `QaPersonas` constants and the `Qa:Seed` gating, then walks them through every layer (SQL HasData seeds, Redis hosted seeder, Bruno collection, Markdown runbook). Failure paths and admin ops are out of scope — they ride on this skeleton in later phases.

Concretely the slice covers:

- `ECommerce.Shared/Qa/QaPersonas` (Guids + plaintext-password constants for the runbook only) and `QaSeedingExtensions` (`AddQaSeeding` / `SeedQaData` honouring Dev + `Qa:Seed`).
- Auth seed: 3 customer rows added alongside the existing admin via `UserConfiguration.HasData`.
- Product seed: one entry — `product-happy` at `10.00`.
- Inventory seed: a default warehouse row plus a stock row for `product-happy` with sufficient quantity.
- Basket seed: `RedisQaSeederHostedService` writes one basket key for `customer-happy` with one line item referencing `product-happy`. Idempotent — `EXISTS` then `SET` so QA mutations survive restarts.
- Each service `Program.cs` calls `app.SeedQaData()` after migrations; gating lives inside the extension.
- Bruno: `qa/bruno/qa-local.bru` env (gateway base URL, per-service direct URLs, persona credentials, seeded IDs) plus `qa/bruno/01-happy-path/` collection — login, get basket, place order, poll order until confirmed, dispatch shipment, mark delivered.
- Runbook: `docs/qa/README.md` (persona table, reset instructions, password storage warning, link to `InMemoryPaymentGateway` cents rule) and `docs/qa/scenarios/01-happy-path.md` covering steps 1–N with the four-layer verification per step.
- Tests: gating unit test in `ECommerce.Shared.Tests`; auth integration test asserts 4 users exist with documented IDs/roles after migrations apply; new `Basket.Tests/` project with a Testcontainers-Redis test asserting the seeded basket key resolves through `GET /basket/{customerHappy}`.

### Acceptance criteria

- [ ] Fresh `docker compose down -v && docker compose up --build` from a clean checkout boots all 8 services healthy with no manual seed step.
- [ ] `POST /login` (port 8003) with each persona's documented credentials returns 200 + JWT.
- [ ] `GET /basket/{customerHappy}` returns the seeded basket; `GET /product/{productHappy}` returns the seeded product; `GET /inventory/{productHappy}` returns sufficient stock.
- [ ] Walking `docs/qa/scenarios/01-happy-path.md` end-to-end via the Bruno collection completes with each step's HTTP, SQL, event, and Jaeger checks passing.
- [ ] With `ASPNETCORE_ENVIRONMENT=Production` and no `Qa:Seed=true`, no QA rows are written (verified by gating unit test and a manual check).
- [ ] `Basket.Tests` project exists and runs green; new auth/basket/shared tests included in `dotnet test` for their service.
- [ ] `docs/qa/README.md` documents personas, passwords, reset command, and the cents convention; opens in under five minutes of reading.
- [ ] `dotnet format --verify-no-changes` clean; pre-commit Husky hook (basket tests) green.

---

## Phase 2: Failure paths — stock-shortage and payment-decline

**User stories**: 13, 14.

### What to build

Layer two failure scenarios on top of the Phase 1 skeleton. No new infrastructure — only additional seeded products plus two more Bruno collections and runbook pages. Both scenarios are symmetric thin additions and ship together.

- Add `product-decline` priced `9.99` (triggers `InMemoryPaymentGateway` decline) and `product-zero-stock` (price `10.00`).
- Inventory: seed a stock row for `product-zero-stock` with `quantity = 0`; seed sufficient stock for `product-decline`.
- Pre-seed baskets for `customer-decline` (containing `product-decline`) and `customer-cancel` (containing `product-zero-stock`) via the existing Redis hosted seeder.
- Bruno: `02-stock-shortage/` (login as cancel, place order, observe `StockReservationFailedEvent`, see order Cancelled) and `03-payment-decline/` (login as decline, place order, observe `StockReservedEvent` then `PaymentFailedEvent`, see order Cancelled and stock released).
- Runbook: `docs/qa/scenarios/02-stock-shortage.md` and `03-payment-decline.md` with full four-layer verification per step. Each runbook references the cents rule note already in `README.md`.

### Acceptance criteria

- [ ] `POST /order/{customerCancel}` against the seeded `product-zero-stock` order ends with order status `Cancelled`, no stock reservation row written, and `StockReservationFailedEvent` observed in RabbitMQ.
- [ ] `POST /order/{customerDecline}` against the seeded `product-decline` order ends with order status `Cancelled`, payment row in `Failed`, inventory row's reserved quantity returned to zero, and `PaymentFailedEvent` + `OrderCancelledEvent` observed.
- [ ] Both scenario runbook pages walk end-to-end with all four-layer checks passing.
- [ ] Re-running each scenario after `docker compose down -v && up --build` repeats deterministically (no leaked state from Phase 1).
- [ ] Per-service `dotnet test` green; new seed presence assertions cover the two new products and the zero-stock row.

---

## Phase 3a: Payment admin ops + cancel-post-confirm cascade

**User stories**: 16, 18, 21 (admin-vs-customer call-outs in runbook).

### What to build

Seed pre-built order/payment fixtures so QA can exercise capture, refund, and cancel-after-confirm without first walking the entire saga.

- Add a confirmed order owned by `customer-happy` with an authorised payment row (status `Authorized`) referencing a fresh `INMEM-…` provider reference. Used for capture testing.
- Add a second confirmed order with a captured payment row (status `Captured`). Used for refund and post-confirm cancel testing.
- Bruno additions in `04-admin-ops/payment/`: capture authorised payment → expect `PaymentCapturedEvent`; refund captured payment → expect `PaymentRefundedEvent`; cancel-post-confirm flow (POST cancel on the captured-payment order, observe `OrderCancelledEvent` cascade → `PaymentRefundedEvent` + inventory release + shipment cancel if any).
- Runbook contribution: opening section of `04-admin-ops.md` with an "Admin vs Customer" header and the payment-ops walk-through, including SQL on `Payments`/`Orders` tables and event expectations.

### Acceptance criteria

- [ ] After seeding, `GET /payment/by-order/{authorizedOrderId}` returns a payment in `Authorized`; `POST /payment/{id}/capture` flips it to `Captured` and emits `PaymentCapturedEvent`.
- [ ] `POST /payment/{capturedPaymentId}/refund` flips the payment to `Refunded` and emits `PaymentRefundedEvent`.
- [ ] Cancelling a confirmed order via the order endpoint cascades: order `Cancelled`, payment refunded, inventory released, shipment (if present) cancelled. All four events observable in RabbitMQ.
- [ ] Runbook documents which steps require the admin JWT vs the customer JWT and shows a 403 expectation for misuse.
- [ ] New seed-presence integration tests in `Payment.Tests` and `Order.Tests` cover the two pre-built fixtures.

---

## Phase 3b: Shipping admin ops + carrier webhook

**User stories**: 17, 19, 21.

### What to build

Seed shipments in non-trivial statuses so each shipping state transition is one request away. Add the carrier-webhook scenario with a sample payload.

- Pre-built shipments owned by `customer-happy`'s confirmed order(s):
  - one `Pending` (so `pick` is testable),
  - one `Picked` (so `pack` is testable),
  - one `Packed` (so `dispatch` is testable directly — the most common QA target),
  - one `Dispatched` (so `deliver`, `fail`, and `return` are testable),
  - one `Created` to exercise `cancel`.
- Add a sample carrier-webhook payload bundled in `qa/bruno/04-admin-ops/shipping/webhook.bru` calling `POST /shipping/webhooks/carrier/{carrierKey}` with documented body shape.
- Bruno: `04-admin-ops/shipping/` with one request per transition plus the webhook.
- Runbook: append shipping-ops section to `04-admin-ops.md`, listing each transition's expected status, SQL row check on `Shipments`, expected event (`ShipmentDispatchedEvent` etc.), and the Jaeger span list for the webhook ingestion.

### Acceptance criteria

- [ ] Each of `pick`, `pack`, `dispatch`, `deliver`, `fail`, `return`, `cancel` succeeds against the corresponding pre-seeded shipment without any prior walk-through.
- [ ] `POST /shipping/{packedId}/dispatch` emits `ShipmentDispatchedEvent` and updates the order's tracking fields.
- [ ] Carrier webhook step ingests the sample payload, returns 200/202 per implementation, and updates shipment status visibly in the SQL check.
- [ ] Runbook clearly marks every shipping transition as admin-only (per current authorization policy).
- [ ] `Shipping.Tests` covers seed presence for all five status fixtures.

---

## Phase 3c: Inventory admin ops

**User stories**: 4, 15, 21.

### What to build

Seed the products and stock rows that exercise the admin inventory endpoints — restock, threshold tweak, manual reserve, back-order.

- Add `product-low-stock` priced `10.00` with stock at `1` and a `LowStockThreshold` of `2` so `GET /inventory/{id}` already shows the alert state.
- Add `product-restock-target` priced `10.00` with stock `0` so QA can `POST /restock` and observe the rebound.
- Bruno: `04-admin-ops/inventory/` covering `POST /inventory/{id}/restock`, `PUT /inventory/{id}/threshold`, `POST /inventory/{id}/reserve` (manual reservation), `POST /inventory/{id}/backorder`.
- Runbook: append inventory-ops section to `04-admin-ops.md`. Each step includes the SQL check on `StockItems` / movement table, expected stock-movement audit row, and the relevant event (`StockCommittedEvent` / movement created).

### Acceptance criteria

- [ ] `GET /inventory/{productLowStock}` shows the threshold-tripped state on a fresh boot.
- [ ] `POST /inventory/{productRestockTarget}/restock` raises stock from 0 and writes a movement row.
- [ ] `PUT /inventory/{productLowStock}/threshold` updates the threshold and the `GET` reflects the change.
- [ ] Manual reserve and back-order endpoints succeed against the seeded products with documented event/log assertions.
- [ ] `Inventory.Tests` covers seed presence for both new products and the threshold-tripped row.

---

## Phase 4: Smoke-test automation + CI

**User stories**: 24.

### What to build

Turn the Bruno scenarios into a regression check. Extend `scripts/local-smoke-test.ps1` (and its bash twin if added later) with a `-Scenario` flag that drives one of `happy | decline | stock-out | admin` end-to-end against the running stack and exits non-zero on the first deviation. Wire the smoke pass into CI so a broken seed fails the pipeline before it reaches a manual tester.

- Smoke script: factor a small dispatcher; each scenario function POSTs the same requests as the Bruno collection and asserts the documented final state (HTTP layer only — SQL/event checks remain manual).
- CI workflow: a job that boots `docker compose up -d`, waits for `/health/ready` on every service, runs all four `-Scenario` invocations, then tears down. Fails the build on any non-zero exit.
- Lightweight per-service `WebApplicationFactory<Program>` integration tests: each service's existing test project gains one `[Fact]` that hits a representative endpoint touching seeded data (`GET /product/{seededId}` etc.) and asserts a 200 + key field. Catches schema drift before the smoke job runs.

### Acceptance criteria

- [ ] `pwsh scripts/local-smoke-test.ps1 -Scenario happy` exits 0 against a clean `docker compose up`; same for `decline`, `stock-out`, `admin`.
- [ ] Mutating a seeded ID without updating the dataset causes the corresponding scenario to fail loudly.
- [ ] CI job runs all four scenarios and gates the merge; first failing scenario surfaces in the job log within 5 minutes of the boot completing.
- [ ] Each service's test suite includes the new seed-reachability `[Fact]` and is green.
- [ ] `dotnet format --verify-no-changes` clean; the pre-commit Husky hook still completes within its existing budget.
