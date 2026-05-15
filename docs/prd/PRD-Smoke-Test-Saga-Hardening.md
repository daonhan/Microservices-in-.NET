# PRD — Smoke-Test Saga Hardening

> Status: draft. Synthesized from the debugging session that took the GitHub Actions [`QA Smoke Test`](https://github.com/daonhan/Microservices-in-.NET/actions/runs/25597337806/job/75145407328) workflow from a fail-at-step-1 state to all four scenarios green. Builds on top of [#72 — QA dump dataset for manual end-to-end verification](https://github.com/daonhan/Microservices-in-.NET/issues/72), which delivered the seed data and runbook but did not exercise the saga end-to-end in CI.

## Problem Statement

As a release manager I expect the `QA Smoke Test` GitHub Actions workflow to prove every PR can complete the four documented QA scenarios (happy, stock-out, decline, admin) end-to-end against a freshly built `docker compose` stack. In practice, the workflow failed on the very first authenticated GET in the `happy` scenario:

```
GET /basket/5ff2d67e-c6b5-4870-911f-79393ed416fd
Response status code does not indicate success: 401 (Unauthorized).
```

Once that 401 was unblocked, every subsequent step uncovered a different latent defect — an order publishing path that fell back to a Kubernetes DNS name in a Compose stack, RabbitMQ subscribers using server-generated anonymous queues that orphaned on every restart, an integration-event handler that fired asynchronously past the lifetime of its DI scope, an outbox serializer that silently dropped every derived property of every event, an outbox poll interval that made the saga take minutes, plus two smoke-script field-name and enum-value drift bugs.

Together these defects meant the CI gate intended to protect the saga had never actually exercised the saga. A passing smoke run was indistinguishable from one that crashed at step one.

## Solution

Land the minimal set of changes that make the existing four QA scenarios pass deterministically in CI in under three minutes, **without** introducing new endpoints, new tests, or saga refactors. The fix is a configuration alignment and three targeted bug fixes in the shared messaging library:

1. **Cross-service identity alignment in `docker-compose.yaml`** — every service that mints or validates JWTs uses the same Compose-network DNS name for Auth (`http://auth:8080`), so issuer-validation matches across publishers and validators.
2. **Per-service messaging configuration in `docker-compose.yaml`** — every event-consuming service declares a deterministic `EventBus:QueueName`, so RabbitMQ creates a stable named queue rather than a server-generated anonymous queue that strands messages on restart.
3. **`Outbox:PublishIntervalInSeconds=2` in `docker-compose.yaml`** — collapse the saga's wall-clock latency from `30s × hops` to `2s × hops` so the smoke test fits in under sixty seconds per scenario.
4. **Two correctness bugs in `ECommerce.Shared`** — the RabbitMQ subscriber must await its handler before its DI scope unwinds, and the outbox must serialize the runtime type of an event, not its compile-time type. Ship them as `2.11.2` and bump every consumer.
5. **Two contract drift fixes in `scripts/local-smoke-test.ps1`** — read `shipmentId` (not `id`) from the shipping API and expect the `Shipped` status (the event is `ShipmentDispatched` but the enum is `Shipped`).
6. **Lift `-PollSeconds` to 60 in the smoke workflow step** — saga has six outbox hops, default 30s leaves no headroom even after the interval fix.

After these changes, the workflow's `Run smoke scenarios` step runs `happy → stock-out → decline → admin` to completion against a clean stack.

## User Stories

1. As a release manager, I want every push to `main` and every PR against `main` to gate on a passing `QA Smoke Test` run, so saga regressions are caught before merge rather than during manual QA.
2. As a release manager, I want a smoke run to either pass cleanly or fail with a single root-cause-relevant error, not a cascade of derived 401s and timeouts that hide the real defect.
3. As a developer, I want `POST /login` against a freshly booted stack to return a token that every other service in the stack actually accepts, so authenticated flows are not blocked by a configuration mismatch nobody owns.
4. As a developer running the stack locally with `docker compose up --build`, I want the same environment my teammates and CI use, so my reproductions match what CI sees.
5. As a developer, I want `Order.Service` to reach `Product.Service` through Compose service DNS, not through a hard-coded Kubernetes ClusterIP service name, so the order endpoint resolves prices instead of returning a transport error.
6. As a developer, I want every event-consuming service to bind a stable named queue (`<service>-microservice`), so messages published before the consumer reconnects are still waiting for it.
7. As a developer, I never want a service restart to silently leave behind a queue that holds messages forever with zero consumers, so debugging "stuck" sagas does not require interrogating RabbitMQ topology by hand.
8. As a developer placing an order, I want the published `OrderCreatedEvent` to carry `OrderId`, `CustomerId`, `Items`, and `Currency` exactly as I emitted them, so a downstream service does not crash with `Cannot insert NULL into column 'CustomerId'` because the outbox serialized only base-class properties.
9. As a developer writing an event handler, I want my `Task Handle(...)` to be awaited before its DI scope is disposed, so my `DbContext` is alive long enough to finish a query.
10. As a developer reading the saga, I want each outbox poll to fire on the order of seconds, not on the order of half-minutes, so a six-hop saga completes inside a CI step.
11. As a QA engineer running `./scripts/local-smoke-test.ps1 -Scenario happy`, I want the script to find the shipment by the field name the API actually returns (`shipmentId`), not by a guessed-at `id` field that is never present.
12. As a QA engineer running the happy scenario, I want the script to wait for the status the domain enum actually uses (`Shipped`), not for the saga's event verb (`Dispatched`), so post-dispatch verification does not time out on a status that will never appear.
13. As a CI maintainer, I want the workflow to allow each saga step a budget compatible with the outbox interval, so reaching the assertion is not a race against a clock that was set when the outbox was 15× slower.
14. As a developer triaging a failed smoke run, I want each fix in this PRD captured in the smoke workflow's failure-log dump (`docker compose ps`, last 500 lines of `docker compose logs`) so post-mortems do not require local reproduction.
15. As a developer onboarding to the repo, I want the troubleshooting paragraph in the QA runbook to call out the `Authentication__AuthMicroserviceBaseAddress` alignment rule, so the next person who copy-pastes a service into Compose does not relive this bug.
16. As a developer making a Compose change, I want the established pattern for any new event-consuming service to include `EventBus__QueueName` from the start, so anonymous queues never reappear.
17. As a developer extending `ECommerce.Shared`, I want `OutboxContext.AddOutboxEvent` to keep using runtime-type serialization regardless of how callers invoke it, so the bug class (compile-time vs runtime serialization) cannot regress silently when a caller passes a base-typed reference.
18. As a developer, I want the consumer-side message-handling path in `RabbitMqHostedService` to remain async-aware in the future, so a regression that re-introduces fire-and-forget on the handler boundary is caught by an integration test, not by the next saga that quietly stalls.
19. As a release manager, I want a single shared-lib version bump to land all messaging fixes atomically, so a partial upgrade across services cannot land where the publisher and the consumer disagree on event payloads.
20. As a release manager, I want the smoke workflow's tear-down step to keep using `docker compose down -v`, so each CI run starts from a clean SQL/Redis/RabbitMQ state and no orphan-queue artifacts carry between runs.
21. As a developer, I want the service start-up order in the `Wait for /health/ready` step to remain Auth-first → resource services → gateway-last, so JWKS is reachable before any service tries to validate a token under load.

## Implementation Decisions

### Modules touched (no new modules)

- **`docker-compose.yaml`** — single source of truth for cross-service wiring under Compose. Every service listed below has its `environment:` block aligned. No new services, no new networks, no new volumes.
- **`ECommerce.Shared` package** — version bumped from `2.11.0` (most services) / `2.11.0` (`order`, `payment`) to `2.11.2`. Two surgical fixes in the messaging layer; no public API changes; no new types.
- **`scripts/local-smoke-test.ps1`** — fix two field-name / enum drift bugs against the existing shipping API. No new scenarios, no new fixtures.
- **`.github/workflows/smoke-test.yml`** — pass `-PollSeconds 60` to the script. No new steps.

### `docker-compose.yaml` — auth-issuer alignment

Auth's `Authentication__AuthMicroserviceBaseAddress` was `http://localhost:8003`. The Auth `JwtTokenService` and `ServiceTokenService` both copy that string into the JWT `iss` claim. Every other service in the stack — and the gateway — has the same env var pointing at `http://auth:8080` (the Compose-network address) because that is where their `JwtBearer` extension fetches the JWKS document and where their `ValidIssuer` is anchored. Tokens minted with `iss=http://localhost:8003` therefore failed validation everywhere with `category=bad-issuer`.

Decision: Auth's container env now also uses `http://auth:8080`. The JWKS endpoint itself derives `issuer` from `httpContext.Request.Host` (see `JwksEndpoint.cs:21`), so the OIDC-discovery doc remains coherent with whatever host the caller used. Bruno collections continue to talk to Auth via `http://localhost:8003` — they validate nothing locally — and external clients hitting `8003/login` still receive a token whose issuer matches what the in-network validators expect.

### `docker-compose.yaml` — order ↔ product addressability

`Order.Service.Program.cs` reads `ProductService:BaseUrl` and falls back to `http://product-clusterip-service:8080` when the key is missing. That fallback is the AKS Service DNS name and is unreachable from a Compose-network container.

Decision: add `ProductService__BaseUrl=http://product:8080` to the `order` block. Keep the production fallback in code unchanged; AKS deployments override the value via Helm/Bicep configuration as before.

### `docker-compose.yaml` — order JWT validation

The `order` block had no `Authentication__AuthMicroserviceBaseAddress`. Order's `appsettings.json` defaulted to `http://localhost:8003`, which is unreachable from inside the order container, so `JwtBearer` never fetched a JWKS and every authenticated `/order/*` request 401'd with `category=bad-issuer` (no signing keys to even reach the issuer check).

Decision: add `Authentication__AuthMicroserviceBaseAddress=http://auth:8080`, mirroring every other resource service.

### `docker-compose.yaml` — named RabbitMQ queues per consumer

`EventBusOptions.QueueName` defaults to `string.Empty`. `RabbitMqHostedService.StartAsync` calls `channel.QueueDeclare(queue: "", durable: true, exclusive: false, autoDelete: false, ...)` — server generates an `amq.gen-*` name. Because the queue is declared `durable: true` and `autoDelete: false`, it persists when the consumer disconnects. On the next service restart the consumer creates a *new* anonymous queue, leaving the old one bound to `ecommerce-exchange` and accumulating messages with zero consumers. Any saga message that lands in an orphan queue after a restart is invisible to handlers.

Decision: pin queue names per service via env. New env entries:

| Service | `EventBus__QueueName` |
|---|---|
| product | `product-microservice` |
| order | `order-microservice` |
| inventory | `inventory-microservice` |
| shipping | `shipping-microservice` |
| payment | `payment-microservice` |

Basket already sets `EventBus:QueueName` in its `appsettings.json` and is left as-is.

### `docker-compose.yaml` — outbox publish cadence

Every outbox hop in the saga (Order → Inventory → Payment → Inventory → Shipping) waits up to one full poll interval for the next service to publish. With `OutboxOptions.PublishIntervalInSeconds = 30` (default), a clean six-hop saga can take three minutes wall-clock.

Decision: set `Outbox__PublishIntervalInSeconds=2` on every service that uses the outbox (product, order, inventory, shipping, payment, gateway, basket — applied uniformly via Compose env). Production deployments keep the conservative default through their own configuration; this knob is a Compose-stack tuning, not a code change.

### `ECommerce.Shared 2.11.2` — handler awaits

`RabbitMqHostedService.OnMessageReceived` runs the dispatch loop synchronously inside Polly's `Execute`:

```
foreach (var handler in scope.ServiceProvider.GetKeyedServices<IEventHandler>(eventType))
{
    handler.Handle(@event!);
}
```

`Handle` returns `Task` and is **not awaited**. As soon as the loop exits, `using var scope = _serviceProvider.CreateScope()` disposes the DI scope. Any `DbContext` resolved inside the scope is disposed; its underlying `SqlConnection` is returned to the pool and closed. Async work still in flight inside the handler then fails with `Invalid operation. The connection is closed.` while iterating its first query. Symptom: inventory's `OrderCreatedEventHandler` never persists a reservation; saga stalls at `PendingStock`.

Decision: change the call to `handler.Handle(@event!).GetAwaiter().GetResult()`. Polly's pipeline remains synchronous (the consumer callback `EventingBasicConsumer.Received` is sync); the handler's task completes before the scope unwinds.

### `ECommerce.Shared 2.11.2` — runtime-type outbox serialization

`OutboxContext.AddOutboxEvent<T>` calls `JsonSerializer.Serialize(@event)`, which the C# overload-resolver binds to `Serialize<T>(T value)`. The compile-time `T` is whatever the caller passed. `Order.Service.Infrastructure.Data.EntityFramework.OrderContext.ExecuteAsync` translates domain events through `private static Event Translate(IDomainEvent domainEvent) => ...`; its return type is the **base** `Event`. `T` therefore binds to `Event`, and `JsonSerializer` writes only base-class properties (`Id`, `CreatedDate`, `CorrelationId`). Derived properties — `OrderId`, `CustomerId`, `Items`, `Currency` — are silently dropped from the JSON payload. Consumer deserialization picks defaults; payment's `OrderCreatedEventHandler.RecordOrderCustomer(orderId, customerId)` then explodes with `Cannot insert NULL into column 'CustomerId'`.

Decision: serialize against the runtime type — `JsonSerializer.Serialize(@event, @event.GetType())`. The `EventType` column already records `@event.GetType().AssemblyQualifiedName`, so this aligns the JSON shape with the type metadata that already accompanies every outbox row.

### Shared-lib version policy

Every service consuming `ECommerce.Shared` is bumped to `2.11.2` in lockstep. Mixed versions across services would risk a publisher running new behavior while a consumer still expects the old shape (or vice-versa). The bump is mechanical and applies identically to:

- `api-gateway/ApiGateway/ApiGateway.csproj`
- `auth-microservice/Auth.Service/Auth.Service.csproj`
- `basket-microservice/Basket.Service/Basket.Service.csproj`
- `inventory-microservice/Inventory.Service/Inventory.Service.csproj`
- `order-microservice/Order.Service/Order.Service.csproj`
- `payment-microservice/Payment.Service/Payment.Service.csproj`
- `product-microservice/Product.Service/Product.Service.csproj`
- `shipping-microservice/Shipping.Service/Shipping.Service.csproj`

Pack-and-publish steps follow the existing local-feed workflow documented in `CLAUDE.md` (`dotnet pack` + `dotnet nuget push` to `local-nuget-packages/`).

### Smoke script — shipping field name

`Wait-ShipmentForOrder` looked for `$first.id` after `Invoke-RestMethod "$Base/shipping/by-order/$OrderId"`. The shipping API actually returns `shipmentId` (per `ShipmentResponse` shape). The poll never matched and timed out at `30s`. The fix reads `$first.shipmentId` (with a fallback to `$first.id` for forward compatibility) and returns a normalized `[pscustomobject]@{ id; status }` to keep the rest of the script unchanged.

### Smoke script — dispatch status

`ShipmentStatus.Shipped = 3` is the enum value the shipping aggregate transitions to on `dispatch`. The integration *event* is named `ShipmentDispatchedEvent`, but the *status* the API surfaces is `Shipped`. The smoke script's `Wait-ShipmentStatus $aH $shipmentId 'Dispatched'` never matched. Fix: expect `'Shipped'`.

### CI workflow — saga budget

`./scripts/local-smoke-test.ps1` defaults `-PollSeconds 30`. The happy path performs `Wait-OrderStatus` (Confirmed) plus `Wait-ShipmentForOrder` plus four sequential `Wait-ShipmentStatus` calls (`Picked`, `Packed`, `Shipped`, `Delivered`). The first two each consume one `-PollSeconds` budget; the rest consume a fixed 15s each in code. Even with the 2s outbox interval the tail of the saga can graze 30s.

Decision: invoke the script with `-PollSeconds 60` from the workflow step. The script's per-shipment-status wait remains hard-coded at 15s in `Invoke-Happy`, which is sufficient under the new outbox cadence.

### What is intentionally *not* changed

- The default `OutboxOptions.PublishIntervalInSeconds = 30` in code stays. Production deployments inherit it.
- The shared library does not introduce a new async-handler API. The fix preserves the existing sync surface of `RabbitMqHostedService.OnMessageReceived`.
- No new endpoints, no new event types, no schema migrations.
- No changes to how `extra_hosts: host.docker.internal:host-gateway` and `RabbitMq__HostName=host.docker.internal` are wired. They work and a refactor to use Compose service DNS would balloon the diff.
- Bruno collection and runbook authored under [#72](https://github.com/daonhan/Microservices-in-.NET/issues/72) are untouched. The smoke fixes preserve the exact persona/customer/product IDs the runbook documents.

### API contracts

No changes. The shipping endpoints return the same JSON shape they always did; the smoke script adjusts to it. The events crossing RabbitMQ keep the same names, queue topology, and DLQ flow.

### Schema

No migrations. The outbox serialization fix changes the *content* of the `Data` JSON column for events going forward — old rows with the truncated payload were already marked `Sent=1` and are not re-played.

## Testing Decisions

A good test here exercises the **CI-observable behaviour**: a clean stack accepts a customer login, walks the saga to a `Confirmed` order, drives the shipping happy path through `Picked`/`Packed`/`Shipped`/`Delivered`, and observes `Cancelled` final states for the two failure scenarios. We deliberately do not lock in implementation details (specific queue names, specific outbox poll counts) at the test layer; those are configuration and live in `docker-compose.yaml` review.

### Modules to verify

- **`scripts/local-smoke-test.ps1`** drives the four scenarios end-to-end against the live Compose stack. This is the regression test for *all* the fixes in this PRD. Run via `pwsh -NoProfile -Command "./scripts/local-smoke-test.ps1 -Scenario <name> -PollSeconds 60"`.
- **`.github/workflows/smoke-test.yml`** is the CI surface. The job's `Run smoke scenarios` step is the load-bearing assertion: green = saga works on a clean stack, red = saga is broken (or the smoke script drifted from the API again).
- **`shared-libs/ECommerce.Shared.Tests`** — existing tests cover the serialization round-trip (`DualValidatorTests`, `RabbitMqDeadLetterIntegrationTests`). They continue to pass against `2.11.2`. No new test is required for the two messaging fixes because the smoke run is the integration test that actually exercises both code paths under realistic conditions; a unit test for "handler awaited" would couple to internals the fix is keeping stable.
- **Per-service test projects** (`Order.Tests`, `Inventory.Tests`, `Payment.Tests`, `Shipping.Tests`) — already use `WebApplicationFactory<Program>` and would catch a regression in their service's local handler logic. They remain the unit/integration coverage layer; this PRD does not extend them.

### Prior art

- `scripts/local-smoke-test.ps1` is itself the smoke harness shipped under [#72](https://github.com/daonhan/Microservices-in-.NET/issues/72). Pattern: scenario-driven, only HTTP assertions, no SQL or RabbitMQ introspection.
- `.github/workflows/smoke-test.yml` follows the existing per-service `azure-pipelines.yml` structure adapted to GitHub Actions: pack shared lib → boot stack → wait for `/health/ready` per service → run scenarios → tear down with `down -v`.
- `qa/bruno/qa-local.bru` is the manual mirror of the same flows; it was the source for the persona / product / customer constants the smoke script reuses.

### What we are *not* testing

- We are not adding a unit test that asserts `OnMessageReceived` awaits its handler. The shape of that test would either run inside the broker (already covered by smoke) or stub out half the class (locks in internals).
- We are not adding a unit test that asserts outbox JSON contains the runtime-type properties. The smoke run validates this end-to-end via payment's `Cannot insert NULL` regression; an isolated test would couple to `JsonSerializer` internals.
- We are not adding load tests, chaos tests, or partial-failure tests around the saga. Out of scope.

## Out of Scope

- Refactoring the Compose stack to use service DNS (`rabbitmq:5672`, `redis:6379`, `sql,1433`) instead of `host.docker.internal`. The current pattern works and the diff to switch is far larger than this PRD warrants. Tracked separately.
- Promoting any of the messaging-layer fixes to a proper async handler API in `ECommerce.Shared`. The minimum-correct change is the `GetAwaiter().GetResult()` shim. A cleaner async dispatcher is a future shared-lib milestone.
- Cleaning up the orphan `amq.gen-*` queues that may exist in shared environments. The `down -v` tear-down handles CI; for shared environments a separate runbook entry will document the manual purge.
- Tuning the outbox poll interval globally. `2s` is appropriate for the local Compose stack; production cadence should remain a deployment-time configuration choice.
- Adding new QA scenarios. The dataset and runbook under [#72](https://github.com/daonhan/Microservices-in-.NET/issues/72) define the four scenarios CI exercises. New scenarios require their own PRD.
- Replacing the smoke harness with a Bruno-CLI run. The PowerShell harness is what CI runs today; switching test runners is a separate decision.
- Migrating away from RabbitMQ to Azure Service Bus for local development. The Compose stack stays Rabbit-on-localhost.

## Further Notes

- **Root cause classification**. Of the eight defects this PRD lands fixes for:
  - 4 are configuration drift in `docker-compose.yaml` (auth issuer, missing auth env on order, missing product URL on order, anonymous queues).
  - 1 is a wall-clock tuning (outbox interval).
  - 2 are real correctness bugs in `ECommerce.Shared` (unawaited handler, compile-time outbox serialization).
  - 2 are smoke-script drift against the shipping API contract (shipmentId field, Shipped status).
  These cluster suggests where future investment buys the most CI stability: typed configuration validation on Compose env, and a smoke step that runs against a recorded API spec.
- **Order of work** (suggested phasing if the PR is split):
  1. `docker-compose.yaml` env fixes — restores login, restores order POST, restores in-cluster JWT validation.
  2. `EventBus__QueueName` per service — eliminates orphan-queue class.
  3. `ECommerce.Shared 2.11.2` (handler await + runtime-type serialization) and consumer bumps — unblocks payment handler and saga progression.
  4. `Outbox__PublishIntervalInSeconds=2` — collapses saga wall-clock to fit CI budget.
  5. Smoke script field-name / enum fixes — unblocks shipment polling.
  6. Workflow `-PollSeconds 60` — final budget headroom.
- **Verification steps** (mirrors what was run in this session):
  1. `docker compose down -v && docker compose up -d --build` from repo root.
  2. Wait for every service's `/health/ready` to return 200 (Auth → resource services → gateway).
  3. `./scripts/local-smoke-test.ps1 -Scenario happy -PollSeconds 60` → ends with `smoke 'happy' OK`.
  4. Repeat for `stock-out`, `decline`, `admin`.
  5. `gh run watch` on the workflow run; the `Run smoke scenarios` step prints `=== running scenario: <name> ===` for each, no exception output, exit code 0.
- **Why this is one PRD and not eight**. The defects are not independent: until the first three landed nobody could even reach the next one in the saga. Splitting into eight PRs would mean six of them sit on red CI for the duration. One PRD, one PR, one CI gate flip.
- **Linked artifacts**:
  - Failing run that motivated the fix: [actions/runs/25597337806/job/75145407328](https://github.com/daonhan/Microservices-in-.NET/actions/runs/25597337806/job/75145407328).
  - Parent PRD that delivered the smoke harness and dataset: [#72](https://github.com/daonhan/Microservices-in-.NET/issues/72).
  - Workflow file: `.github/workflows/smoke-test.yml`.
  - Smoke harness: `scripts/local-smoke-test.ps1`.
  - Compose file: `docker-compose.yaml`.
  - Shared library: `shared-libs/ECommerce.Shared/` (`2.11.2`).
