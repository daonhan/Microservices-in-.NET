# PRD — QA Dump Dataset for Manual End-to-End Verification

> Tracking issue: https://github.com/daonhan/Microservices-in-.NET/issues/72

## Context

QA needs a deterministic dataset that lets them step through every documented business case in this monorepo without hand-crafting requests. Today the repo only seeds one admin user (`auth-microservice/.../UserConfiguration.cs`); other services have empty `*ContextSeed.cs` stubs. There are two thin `.http` files (basket, order), no Bruno/Postman collection, and no manual runbook. To verify the saga (Order → Inventory → Payment → Shipping) — including failure paths and admin ops — QA currently has to guess endpoints, generate JWTs, invent product IDs, and read source to know which amount triggers a payment decline (`InMemoryPaymentGateway` keys off `cents == 99`).

This PRD defines a "QA dump dataset" — **reference data seeded via EF Core `HasData` + a Redis seeder, plus a Bruno collection and a step-by-step Markdown runbook** — so a manual tester can boot the stack with `docker compose up` and walk every scenario end to end.

## Problem Statement

As a QA engineer, when I want to verify the full e-commerce flow across all 8 services, I have to:

- Build my own JWTs (only one admin user exists).
- Invent product IDs and stock levels (no product/inventory data is seeded).
- Discover by reading code that a payment is declined when the cents portion is `.99`.
- Hand-craft REST requests because there is no Postman/Bruno collection.
- Guess what to verify after each step (no runbook documents expected DB state, expected events, or expected traces).
- Repeat all of that for every saga path: happy, stock-shortage, payment-decline, plus manual ops (refund, cancel, dispatch, return, restock, threshold).

The result: setup takes hours, scenarios are inconsistent across testers, and regressions in non-happy paths slip through because nobody re-runs them.

## Solution

Ship three coordinated artifacts:

1. **Seed-data layer** — every service gains a curated `HasData`-based seed plus, for Basket, a `RedisQaSeederHostedService`. Auto-runs in Development; gated behind `Qa:Seed=true` config in other envs.
2. **Bruno collection** at `qa/bruno/` — one folder per scenario, environment file with persona JWTs, base URLs, and product IDs pre-wired.
3. **QA runbook** at `docs/qa/` — one Markdown page per scenario; each step lists HTTP call → expected status/body → SQL row check → RabbitMQ/log assertion → optional Jaeger trace check.

Personas seeded:

| Email | Role | Purpose |
|---|---|---|
| `microservices@daonhan.com` | Administrator | (existing) — admin endpoints (restock, refund, dispatch, return). |
| `customer-happy@qa.test` | Customer | Drives saga happy path. |
| `customer-decline@qa.test` | Customer | Drives payment-decline path (orders products totalling `*.99`). |
| `customer-cancel@qa.test` | Customer | Drives stock-shortage + post-confirm cancellation paths. |

Pricing convention: products priced so the order total cents are deterministic — e.g. `9.99` for decline, `10.00` for happy. Decline trigger is the existing `InMemoryPaymentGateway` rule (cents == 99); no new gateway code.

## User Stories

1. As a QA engineer, I want a single command (`docker compose up --build`) to bring the stack up with all reference data already loaded, so I never have to seed manually.
2. As a QA engineer, I want pre-seeded customer accounts with known passwords, so I can grab a JWT in one request and start testing.
3. As a QA engineer, I want pre-seeded products with known IDs, names, and prices, so I can paste a `productId` into any request and it just resolves.
4. As a QA engineer, I want pre-seeded inventory rows (sufficient stock, zero stock, low-stock-threshold-tripped), so I can hit each inventory branch without restocking first.
5. As a QA engineer, I want pre-seeded baskets in Redis for each customer persona, so I can place an order without a `PUT /basket` warm-up.
6. As a QA engineer, I want a Bruno collection where every request has variables (`{{jwtCustomerHappy}}`, `{{productHappy}}`), so I can switch personas without editing each request.
7. As a QA engineer, I want a Markdown runbook per scenario with numbered steps, so I cannot lose my place mid-flow.
8. As a QA engineer, I want each step to tell me the expected HTTP status and key response fields, so I can spot a mismatch immediately.
9. As a QA engineer, I want each step to give me a `SELECT` snippet for the relevant SQL row, so I can confirm the domain state changed (catches outbox lag).
10. As a QA engineer, I want each step to point to the integration event I should see emitted (queue name, payload field), so I can confirm the saga propagated and not just the local write.
11. As a QA engineer, I want optional Jaeger/Grafana checks called out, so I can confirm cross-service tracing without needing to know which dashboard to open.
12. As a QA engineer, I want to walk the saga happy path end to end (Basket → Order create → StockReserved → PaymentAuthorized → OrderConfirmed → Shipment created → Dispatched → Delivered) using the runbook alone.
13. As a QA engineer, I want to walk the stock-shortage failure (`StockReservationFailedEvent` → order cancelled, stock untouched) using a dedicated zero-stock product.
14. As a QA engineer, I want to walk the payment-decline failure (decline → order cancelled → reserved stock released) using a `*.99` priced product.
15. As a QA engineer, I want to perform admin operations: restock a depleted product, raise/lower the low-stock threshold, manually reserve and back-order stock.
16. As a QA engineer, I want to perform payment ops: capture an authorised payment, refund a captured payment, and observe `PaymentRefundedEvent` propagate.
17. As a QA engineer, I want to perform shipping ops: pick → pack → dispatch → deliver, plus the failure transitions (cancel, fail, return), with each transition seeded with a shipment in the prior status so I never have to walk the whole chain to test one transition.
18. As a QA engineer, I want to cancel an order **after** confirmation and observe inventory release + payment refund cascade.
19. As a QA engineer, I want to verify carrier-webhook ingestion (`POST /shipping/webhooks/carrier/{carrierKey}`) using a sample payload bundled in the collection.
20. As a QA engineer, I want to reset the dataset cleanly with `docker compose down -v && docker compose up --build`, so a corrupted run never blocks the next session.
21. As a QA engineer, I want the runbook to call out which scenarios require admin rights vs customer rights, so I do not waste a request hitting a 403.
22. As a developer, I want the seed module per service to be a single, deep, testable class (`*ContextSeed`/`IBasketQaSeeder`), so unit tests catch regressions when somebody changes a seeded ID.
23. As a developer, I want seeding gated behind `IsDevelopment()` plus an explicit `Qa:Seed=true` opt-in for non-Dev, so test data never lands in Staging/Prod by accident.
24. As a release manager, I want CI to run an automated smoke pass over the dataset (extended `scripts/local-smoke-test.ps1`), so a broken seed fails the pipeline before QA opens it.
25. As a developer onboarding to the repo, I want `docs/qa/README.md` to explain the dataset structure and persona mapping in under five minutes of reading.

## Implementation Decisions

### Format

- **Reference data via EF Core `HasData`** — extend each service's existing `*ContextSeed.cs` (or its `IEntityTypeConfiguration<T>`s) the same way `auth-microservice/.../UserConfiguration.cs` already seeds the admin. Generates a real migration per service so the schema and the data ship together.
- **Basket via `RedisQaSeederHostedService`** — Basket has no SQL store, so a hosted service in `basket-microservice/Basket.Service/Infrastructure/Seeding/` writes basket JSON for each customer persona at startup. Idempotent (`SET NX` semantics) so a restart does not clobber a basket QA has been mutating.
- **No standalone CLI tool, no SQL dump files, no compose init scripts.** Keeps everything in-process and version-controlled.

### Lifecycle

- **Development**: each service's `Program.cs` calls `app.SeedQaData()` (new `ECommerce.Shared` extension) after `app.ApplyMigrationsAsync()` automatically when `app.Environment.IsDevelopment()`.
- **Staging/Prod**: same call is a no-op unless `Qa:Seed=true` (env var `Qa__Seed=true`). Default off.
- **Reset**: `docker compose down -v` (drops SQL Server, Redis, RabbitMQ volumes) + `docker compose up --build`. Document in runbook; no new tooling.

### Personas & data shape

- Three customer rows seeded in Auth alongside existing admin. Passwords bundled in runbook (`Qa!Test123` style); same PBKDF2 hashing as the admin entry.
- Customer GUIDs are stable, hard-coded constants exposed via a single `QaPersonas` static class in `ECommerce.Shared` so every service references the same IDs without duplicating literals.
- Products: 5–6 catalog entries with stable GUIDs and deterministic prices.
  - `product-happy` price `10.00` — used for happy path, refunds, dispatch.
  - `product-decline` price `9.99` — triggers `InMemoryPaymentGateway` decline.
  - `product-zero-stock` — inventory row exists with `quantity = 0` for stock-shortage path.
  - `product-low-stock` — inventory row at `1` with threshold `2` to surface the threshold-alert branch.
  - `product-restock-target` — depleted, used for the admin restock case.
- Inventory: warehouse + stock rows pre-populated so `GET /inventory/{productId}` returns realistic data.
- Orders/Payments/Shipments: a small set of pre-built fixtures in non-trivial statuses so QA can test individual state transitions without walking the whole saga (e.g. one shipment in `Packed` so QA can hit `POST /shipping/{id}/dispatch` directly).

### Module boundaries

- `ECommerce.Shared/Qa/QaPersonas.cs` — static GUIDs for the 4 users + product IDs. Single source of truth.
- `ECommerce.Shared/Qa/QaSeedingExtensions.cs` — `IServiceCollection.AddQaSeeding()`, `IApplicationBuilder.SeedQaData()` honouring `IsDevelopment()` + `Qa:Seed`.
- Per service `Infrastructure/Data/EntityFramework/*ContextSeed.cs` — already exists as stubs; fill in `HasData` configurations rather than runtime inserts so EF migrations carry the data.
- `basket-microservice/.../Infrastructure/Seeding/RedisQaSeederHostedService.cs` — new hosted service. Writes one basket key per customer persona.
- `qa/bruno/` — Bruno collection with env file `qa-local.bru` (base URLs for compose, ports per service via gateway and direct).
- `docs/qa/README.md` — index + persona table.
- `docs/qa/scenarios/01-happy-path.md`, `02-stock-shortage.md`, `03-payment-decline.md`, `04-admin-ops.md` — scenario runbooks.

### Verification depth

Each runbook step has four checks (HTTP, SQL, event/log, optional Jaeger):

1. **HTTP** — exact verb/path, status code, key response fields.
2. **SQL** — copy-pasteable `SELECT` against the right service DB (e.g. `SELECT Status, OutboxState FROM Orders WHERE Id = '{{orderId}}';`).
3. **Event/log** — RabbitMQ Management UI queue check + `docker compose logs <service>` grep target. Ties to `IntegrationEvents/Events/*.cs` event types already in code.
4. **Trace (optional)** — Jaeger search by `traceparent` header echoed in the HTTP response, with the expected list of service spans for that scenario.

### API contract

Existing endpoints — the dataset does not introduce new HTTP routes. The only public surface change is configuration: `Qa:Seed` setting (per-service `appsettings.json` and `appsettings.Development.json`).

### Schema

No schema changes. The new `HasData` calls produce migrations named like `20260507_SeedQaData_*` per service. Designed so re-running migrations is idempotent (HasData semantics handle that).

## Testing Decisions

A good test here exercises **observable behaviour**, not the wiring:

- "After applying migrations, the Auth DB contains 4 users with the documented IDs." ✅
- "After hosted-service start, Redis contains baskets for 3 customer personas." ✅
- Not "the seeder calls `db.SaveChanges` exactly once." ❌

### Modules to test

- `Auth.Service.Tests` — a new fact applies migrations to a SQL Server test container (or `EnsureCreatedAsync` on a fresh `DbContext`) and asserts the four seeded users exist with correct roles.
- `Product.Service.Tests`, `Inventory.Service.Tests`, `Order.Service.Tests`, `Payment.Service.Tests`, `Shipping.Service.Tests` — same pattern: spin up the service `WebApplicationFactory<Program>` in Development, hit a representative GET (`GET /product/{seededId}`, `GET /inventory/{seededProductId}`), assert the seeded entity is reachable end-to-end through the API.
- `Basket.Service.Tests` (new project — the missing test project flagged by exploration) — integration test that boots the basket service against a Redis Testcontainer, waits for the hosted seeder to settle, and asserts each customer-persona basket key resolves to the expected payload.
- `ECommerce.Shared.Tests` — unit test for `QaSeedingExtensions`: seeding is a no-op when `IsDevelopment() == false && Qa:Seed != true`.
- **Smoke regression**: extend `scripts/local-smoke-test.ps1` with `-Scenario happy|decline|stock-out|admin` flags. Each flag drives the corresponding Bruno collection request order via `curl`/Bruno CLI and asserts the documented final state. Wire into CI so a broken seed fails the build.

### Prior art in the codebase

- `auth-microservice/Auth.Tests/` — uses NSubstitute + xUnit; matches the framework choice for new auth seeded-data tests.
- `order-microservice/Order.Tests/OrderWebApplicationFactory.cs` — template for the per-service integration tests above.
- `shared-libs/ECommerce.Shared.Tests/` — already uses Testcontainers (RabbitMQ); the same dependency satisfies the Redis container test for Basket.
- `api-gateway/ApiGateway.Tests/Integration/GatewayTestHarness.cs` — pattern for spinning up a real listener and exercising downstream behaviour.

## Out of Scope

- Production or Staging seed data. The dataset is QA-only and gated.
- A web UI for QA to drive scenarios (existing Swagger + Bruno is the surface).
- Real payment provider integration / sandbox accounts. Decline path uses `InMemoryPaymentGateway`.
- Performance / load datasets (millions of rows). This is for manual case coverage, not perf.
- Localisation / multi-currency seed cases.
- Replacing the existing two `.http` files. They can stay for quick smoke checks; Bruno collection lives alongside them.
- Generating sample carrier webhook signing keys (use plaintext payloads documented in the runbook).
- Auto-resetting the dataset between scenarios. `docker compose down -v` is the documented reset; an in-process reset endpoint is a future enhancement.

## Further Notes

- **Persona password storage**: hashes go into source via `HasData`; plaintext passwords go into `docs/qa/README.md` only. They are test credentials for a non-prod stack — they are not secrets, but the runbook should warn against reusing them anywhere reachable from the public internet.
- **Cents convention discoverability**: the `9.99` decline trigger is currently only documented in `InMemoryPaymentGateway.cs` XML doc. The runbook makes it visible to QA.
- **Idempotency**: HasData re-applies cleanly. The Basket Redis seeder must be idempotent against the case where QA mutated a seeded basket and restarts the service — use `EXISTS`-then-`SET` so we never overwrite live state.
- **Order of work** (suggested phasing for the implementation issue this PRD turns into):
  1. `QaPersonas` static + Auth users + Product catalog seed + first runbook page (happy path) + Bruno env. Gives the smallest end-to-end slice.
  2. Inventory + Basket seeders + happy-path runbook complete.
  3. Stock-shortage scenario + Order/Payment/Shipping fixtures for failure paths.
  4. Payment-decline scenario.
  5. Admin-ops scenario (refund, cancel-after-confirm, dispatch, return, restock, threshold).
  6. Smoke-test extension + CI wiring.

## Critical Files (reference, not exhaustive)

- `auth-microservice/Auth.Service/Infrastructure/Data/EntityFramework/Configurations/UserConfiguration.cs` — extend with 3 customer personas.
- `product-microservice/Product.Service/Infrastructure/Data/EntityFramework/ProductContextSeed.cs` — fill in (currently stub).
- `inventory-microservice/Inventory.Service/Infrastructure/Data/EntityFramework/InventoryContextSeed.cs` — fill in.
- `order-microservice/Order.Service/Infrastructure/Data/EntityFramework/OrderContextSeed.cs` — add (does not exist; mirror payment/shipping pattern).
- `payment-microservice/Payment.Service/Infrastructure/Data/EntityFramework/PaymentContextSeed.cs` — fill in.
- `payment-microservice/Payment.Service/Infrastructure/Gateways/InMemoryPaymentGateway.cs` — **read-only reference** for the decline rule (cents==99).
- `shipping-microservice/Shipping.Service/Infrastructure/Data/EntityFramework/ShippingContextSeed.cs` — fill in.
- `basket-microservice/Basket.Service/` — new `Infrastructure/Seeding/RedisQaSeederHostedService.cs`; new `Basket.Tests/` project.
- `shared-libs/ECommerce.Shared/Qa/QaPersonas.cs`, `QaSeedingExtensions.cs` — new.
- `qa/bruno/` — new collection root.
- `docs/qa/README.md`, `docs/qa/scenarios/*.md` — new runbook.
- `scripts/local-smoke-test.ps1` — extend with `-Scenario` flag.
- Each service `Program.cs` — call `app.SeedQaData()` after migrations (Development branch).

## Verification Plan (end-to-end)

1. `docker compose down -v && docker compose up --build` from repo root → all services healthy on documented ports.
2. `POST /login` (auth, port 8003) with `customer-happy@qa.test` + seeded password → 200 + JWT.
3. Walk `docs/qa/scenarios/01-happy-path.md` end to end via Bruno collection. Each step's HTTP/SQL/event/Jaeger checks pass.
4. Repeat for `02-stock-shortage.md`, `03-payment-decline.md`, `04-admin-ops.md`.
5. `pwsh scripts/local-smoke-test.ps1 -Scenario happy` → exits 0; same for `decline`, `stock-out`, `admin`.
6. `cd <service> && dotnet test` for each touched service → green, including new seed-presence assertions.
7. `dotnet format --verify-no-changes` clean.
8. CI run on PR → smoke-test job passes; pre-commit `Husky` hook (basket tests) green.
