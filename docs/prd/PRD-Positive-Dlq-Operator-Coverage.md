# PRD: Positive DLQ Operator Coverage

> Status: Approved. Closes the positive-coverage limitation flagged in [PRD-Postman-Smoke-Testing](./PRD-Postman-Smoke-Testing.md) folder `06 DLQ Operator (authz boundary)`. Sibling to [PRD-Qa-Smoke-Gap-Closure](./PRD-Qa-Smoke-Gap-Closure.md) (saga-operator fixture, same `Qa:Seed` seam). DLQ surface defined by [PRD-DLQ-Replay-UI](./PRD-DLQ-Replay-UI.md).

## Context

The gateway DLQ operator API (`/operator/api/failures*` — list, detail, single replay, batch replay, discard) is gated by the `RequireOperator` policy: claim `user_role == Operator` (`shared-libs/ECommerce.Shared.Platform/Abstractions/AuthorizationPolicies.cs`). The black-box smoke suite covers this surface in folder `06 DLQ Operator (authz boundary)` of `qa/postman/ECommerce-Smoke.postman_collection.json` — but only the **negative** boundary: 401 anonymous, 403 for service-role and Administrator tokens.

The happy paths cannot be smoke-tested black-box because no seeded credential yields an Operator claim. Seeded users are only `Administrator`/`Customer`; the service token is hardcoded to `user_role: service` (`auth-microservice/Auth.Service/Domain/Tokens/ServiceTokenService.cs`). And even with an Operator token there is nothing to act on — `dead_letter_messages` rows are born only from live RabbitMQ DLX capture or the outbox poller, so list/detail/replay/discard have no deterministic fixture.

This PRD lands the two missing seeds — an Operator credential and a `dead_letter_messages` fixture — both env-gated to QA/dev, so the smoke suite gains positive (2xx) coverage of all five operator endpoints without shipping either artifact to production.

## Problem Statement

As a release manager I expect the smoke suite to prove the DLQ operator contract end-to-end on every clean stack, the same way the saga-operator suite does after [PRD-Qa-Smoke-Gap-Closure](./PRD-Qa-Smoke-Gap-Closure.md). Today two facts block that:

1. **No Operator credential exists.** `auth-microservice/Auth.Service/Domain/Tokens/JwtTokenService.cs` copies `user.Role` verbatim into the `user_role` claim, but seeded users carry only `Administrator`/`Customer`. The service-token path hardcodes `user_role: service`. So `RequireOperator` can only ever be observed rejecting — the happy path is unreachable black-box.
2. **No DLQ fixture exists.** `dead_letter_messages` (gateway-owned via the shared `ECommerce.Shared.DeadLetter` context) is populated only by `RabbitMqDeadLetterCapture` (live DLX) or `OutboxFailurePoller`. Neither is deterministic on a freshly booted stack, so even an Operator token would list an empty page.

Concretely:

- Folder 06 pins 3 negative requests and explicitly documents positive coverage as out of scope (the folder description references the PRD-#317 limitation).
- The DLQ operator API — the operator's break-glass surface for replaying/discarding poison messages — has **zero** automated happy-path verification on any PR.
- A regression that breaks list/detail/replay/discard response shapes would pass CI today.

## Solution

Land two env-gated runtime seeders and extend the smoke collection. No new endpoints, no token-service change, no new auth flow, no shared-libs version bump.

1. **Operator credential — runtime seeder in Auth.** Seed a user `operator@qa.test` with `Role = "Operator"`, reusing the existing QA persona password hash, inserted at startup only when QA seeding is enabled. Because `JwtTokenService` copies `user.Role` verbatim, `POST /login` then returns an Operator token with no token-service change. Deliberately a runtime seeder rather than an EF migration (as the existing `SeedQaData_Auth` customers use) so this high-privilege account never ships in a prod migration.
2. **DLQ fixture — runtime seeder in api-gateway.** Idempotently insert five `dead_letter_messages` rows at startup (gated by the same predicate that already gates `ApplyDeadLetterMigrations()`), mirroring Saga's `SeedQaOperatorOutboxFixture` (`IF NOT EXISTS … INSERT`). Distinct rows back single-replay, batch-replay, and discard so a run is order-independent; a never-mutated row backs list+detail. Kept out of the shared `ECommerce.Shared.DeadLetter` migrations so the fixture never pollutes the shared package or prod.
3. **Positive Postman coverage — all five endpoints.** Extend folder 06 with `login-operator` (capture `operatorToken`) then list (200 + fixture present), detail (200), single replay (202 → Replayed), batch replay (200, per-item success), discard (202 → Discarded). The existing negative requests stay. Rename the folder `06 DLQ Operator` and drop the "out of scope" note.
4. **Bruno mirror + CI.** Mirror the requests under `qa/bruno/dlq-operator/` and wire a `dlq-operator` batch into the `bruno-smoke` job next to `saga-operator`.
5. **Docs + ADR.** A `docs/qa/scenarios/06-dlq-operator.md` scenario, updates to the QA env-var/persona tables, and an ADR recording the deliberate divergence to env-gated runtime seeders for the high-privilege Operator credential + DLQ fixtures.

## User Stories

1. As a release manager, I want the DLQ operator suite to pass positively on a clean stack, so that CI proves every PR preserves the list/detail/replay/discard contract — not just the authz boundary.
2. As a release manager, I want the positive coverage gated on the same `Qa:Seed`/Development predicate as the existing dataset, so that the gate stays a single trustworthy signal and no production stack is touched.
3. As a QA engineer, I want a seeded `operator@qa.test` persona, so that I can log in and exercise the operator API from Postman/Bruno desktop without hand-crafting an Operator JWT.
4. As a QA engineer, I want deterministic `dead_letter_messages` fixtures with fixed GUIDs exposed in the env files, so that I can re-run the suite without copy-pasting GUIDs or waiting for a real DLX failure.
5. As a backend engineer touching the operator slices, I want CI to fail when `GET /operator/api/failures`, the detail route, replay, batch-replay, or discard stops returning the documented shape, so that the break-glass surface stays trustworthy.
6. As a backend engineer touching the DLQ replay path, I want the replay smoke to assert the 202 + `Replayed` status transition (not downstream re-consumption), so that the test is deterministic and not coupled to any consumer.
7. As an SRE, I want the Operator credential to be impossible to seed in prod, so that no production environment ever carries an account that can replay or discard poison messages.
8. As an SRE, I want the DLQ fixture rows gated on the same toggle and idempotent across reboots, so that production stacks never see synthetic dead letters and dev reseeds cleanly.
9. As a QA engineer, I want the smoke run to be re-runnable without `docker compose down -v` between runs, so that replay/discard mutating the fixtures does not leave the suite red on a second run.
10. As a documentation maintainer, I want a `docs/qa/scenarios/06-dlq-operator.md` runbook mirroring `05-saga-operator-abort.md`, so that the operator flow has a documented seed + SQL-probe recipe.
11. As an operator running the collection, I want `dlq-list-operator` to return the seeded rows on a freshly booted stack, so that the chain has GUIDs to drive detail/replay/discard without manual setup.

## Implementation Decisions

- **Token service unchanged.** `JwtTokenService` already copies `user.Role` → `user_role`. A seeded `Role = "Operator"` user is sufficient; `ServiceTokenService` is not touched.
- **Single seeding gate.** Both seeders ride `QaSeedingExtensions.IsQaSeedingEnabled` (`Development` OR `Qa:Seed=true`). Auth's `MigrateDatabase()` + `SeedQaData()` already sit behind it (`Auth.Service/Program.cs:38-43`). The gateway already gates `ApplyDeadLetterMigrations()` on `app.Environment.IsDevelopment()` and does **not** reference `ECommerce.Shared.Testing.Qa`; reuse that existing `IsDevelopment()` block (zero new package dependency) rather than widening to `Qa:Seed`. The smoke stack runs Development for both services, so the gate is satisfied with no config change; prod/staging K8s set Production and never set `Qa__Seed`.
- **Runtime seeders, not migrations.** The Operator account and DLQ fixtures are inserted at startup (idempotent, insert-if-absent), diverging deliberately from the migration-based `SeedQaData_Auth` so high-privilege/synthetic data cannot ride a prod migration. Pattern mirrors Saga's inline `SeedQaOperatorOutboxFixture`.
- **Auth seeder seam.** Pass a seed action into the existing no-op `app.SeedQaData(...)` call rather than adding a new gated call site.
- **Fixture layout.** Five rows, fixed GUIDs `f0000000-…-00000000000N`, all seeded `Status=Pending(0)`, `Origin=DeadLetter(0)`, `Service="qa-operator"`, `EventType="Qa.OperatorSmokeEvent"`, `OriginalQueue="qa-dlq-replay-sink"`, `Payload="{}"`. `…0001` = list/detail (never mutated); `…0002` = single replay; `…0003`/`…0004` = batch replay; `…0005` = discard. Replay/discard succeed only on `Status==Pending` (else 409), so every mutating target seeds Pending and the list/detail row stays Pending.
- **Replay is side-effect-free.** `RabbitMqDeadLetterPublisher.Publish` uses the default exchange with `routingKey = OriginalQueue`, `mandatory: false` — a queue no service subscribes to is silently dropped (no throw), returns a `newMessageId`, replay → 202. It throws on an **empty** `OriginalQueue`, so the fixture uses an inert non-empty sink `qa-dlq-replay-sink`. `publish_failed` is reachable only on a broker outage, which gateway readiness already gates against.
- **Idempotent across reruns.** The run itself flips replay/discard targets to terminal status. The gateway seeder resets the four mutating rows back to Pending on every boot (`UPDATE … SET status=0, replayed_at=NULL, discarded_at=NULL`), so a `RESET=0` rerun does not 409. `RESET=1` (the `run-smoke.sh` default) wipes volumes and reseeds.
- **Constants stay local.** Persona + fixture constants live next to their seeders (auth-local, gateway-local), not in shared `QaPersonas` — adding them there would force a shared-libs `<Version>` bump + 10-package repack + consumer re-pin, disproportionate to a smoke fixture. The Postman/Bruno env files remain the black-box source of truth; the lockstep invariant (values agree across Postman env, Bruno env, `AGENT.md`, `scripts/local-smoke-test.ps1 $Qa`) still holds.
- **Additive only.** No new operator endpoints, claims, or roles; the `Operator` role + `RequireOperator` policy already exist in `ECommerce.Shared.Platform`.

## Testing Decisions

Tests observe the external contract operators and CI care about — never the seeder wiring.

1. **Auth seeder.** A seed-presence test asserts `operator@qa.test` exists with `Role="Operator"` and that the literal hash verifies with the QA persona password via the same `IPasswordHasher<User>` login uses. Optional `WebApplicationFactory` test: `POST /login` for the Operator persona → decoded `user_role == "Operator"`. Prior art: `JwtTokenServiceTests` already asserts the role claim; `auth-microservice/Auth.Tests/Qa/`.
2. **Gateway endpoints.** An HTTP integration test via `GatewayWebApplicationFactory.CreateJwt("Operator")` over a list-backed `IDeadLetterStore` seeded with the five fixtures: list 200, detail 200, replay 202 + Replayed, batch 200 + per-item success, discard 202 + Discarded. Prior art: the existing operator handler tests under `api-gateway/ApiGateway.Tests/`. Plus a seeder idempotency test (run twice → five rows; mutating targets reset to Pending).
3. **Black-box suite.** The Postman/Bruno requests are the integration test; the underlying Auth/DLQ slices already carry unit + handler coverage, so no new server-side tests beyond the two seeder guardrails above.

## Out of Scope

- Changing `ServiceTokenService` or adding a per-client role. The user-token path (`/login`) already carries the role; service tokens stay `user_role: service`.
- New operator endpoints, role policies, or claims. The five existing routes and the existing `RequireOperator` policy cover the gain.
- A general "seed arbitrary DLQ state" or internal seed endpoint. The fixed five-row fixture is sufficient; a flexible seed surface is unneeded.
- Asserting downstream re-consumption of a replayed message. Replay coverage is status-transition only (202 + Replayed); coupling to a consumer would add flakiness.
- Triggering real RabbitMQ DLX captures during the smoke run. Deterministic seeded rows replace the slow/flaky live-failure path.
- Moving persona/fixture constants into shared `QaPersonas` (and the attendant shared-libs version bump). Local constants are the chosen trade-off.
- DLQ replay UI / operator HTML. This PRD is API + black-box tooling only, consistent with the operator-surfaces-prefer-tooling convention.

## Further Notes

- The fixture `OriginalQueue` (`qa-dlq-replay-sink`) is intentionally an inert queue no service binds. If a future change sets `mandatory: true` on the DLQ publisher, that queue must be declared or replay will return `publish_failed` — flagged in the ADR.
- The seeded rows are re-seeded (and the mutating rows reset to Pending) on every boot, the same idempotent insert-if-absent shape the seeded customers/shipments use. There is no "un-replay" cleanup; the seeder's Pending-reset is the cleanup.
- Confirm `DeadLetterStatus` serialization shape (numeric vs string) against a real detail response before authoring the Replayed/Discarded assertions.
- The single seeding gate is verified false in all prod/staging manifests (`kubernetes/aks-prod-*.yml` set Production, never `Qa__Seed`); the ADR must explicitly forbid adding `Qa__Seed` to any non-dev manifest.
