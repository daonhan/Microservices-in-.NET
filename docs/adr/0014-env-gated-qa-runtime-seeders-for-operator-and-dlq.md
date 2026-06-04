# ADR-0014 — Env-gated runtime seeders for the QA Operator credential and DLQ fixtures

- **Status**: Accepted
- **Date**: 2026-06-04

## Context

The gateway DLQ operator API (`/operator/api/failures*` — list, detail, single replay, batch replay, discard) is gated by `RequireOperator` (claim `user_role == Operator`). The black-box smoke suite could only cover its **negative** boundary (401/403): no seeded credential yields an Operator claim, and `dead_letter_messages` rows are born only from live RabbitMQ DLX capture or the outbox poller, so there was nothing deterministic to act on. [PRD-Positive-Dlq-Operator-Coverage](../prd/PRD-Positive-Dlq-Operator-Coverage.md) closes that gap with two seeds: an Operator credential and a five-row `dead_letter_messages` fixture.

The existing QA customer dataset (`SeedQaData_Auth`) ships through **EF migrations**. That is the wrong tool for these two seeds:

- The Operator account is the platform's break-glass credential — it can replay or discard poison messages. A high-privilege account that rides a migration would be inserted into **every** database that migration is applied to, including any environment that runs migrations on boot.
- Synthetic dead letters under a fake `qa-operator` service would pollute the gateway-owned `dead_letter_messages` table in any environment the migration touched, and would live in the shared `ECommerce.Shared.DeadLetter` migration history.

The pattern to mirror already exists: Saga's inline `SeedQaOperatorOutboxFixture` (gated, insert-if-absent at startup). Related: [ADR-0004](0004-rabbitmq-fanout-with-dlq-and-operator-api.md) (DLQ + operator API), [ADR-0013](0013-shared-libs-multi-package-split.md) (why fixture/persona constants stay local rather than entering shared `QaPersonas`). Plan: [positive-dlq-operator-coverage](../plans/positive-dlq-operator-coverage.md).

## Decision

Seed the Operator credential and the DLQ fixtures with **env-gated runtime seeders that run at startup**, never through EF migrations.

- **Operator credential** — [`AuthQaOperatorSeeder.SeedQaOperatorUser`](../../auth-microservice/Auth.Service/Infrastructure/Data/EntityFramework/AuthQaOperatorSeeder.cs) inserts `operator@qa.test` with `Role = "Operator"` if absent, reusing the existing literal QA PBKDF2 hash (same `IPasswordHasher<User>` that `/login` uses — no re-hash). It is passed into the existing `app.SeedQaData(...)` call, gated by `QaSeedingExtensions.IsQaSeedingEnabled` (Development **OR** `Qa:Seed=true`). Because `JwtTokenService` copies `user.Role` verbatim into `user_role`, `POST /login` then returns an Operator token with no token-service change.
- **DLQ fixtures** — [`DeadLetterQaFixtureSeeder.SeedQaDeadLetterFixture`](../../api-gateway/ApiGateway/Infrastructure/Seeding/DeadLetterQaFixtureSeeder.cs) idempotently inserts five `dead_letter_messages` rows and resets the four mutating targets back to `Pending` on every boot, so a rerun without `docker compose down -v` does not 409. It runs inside the gateway's existing `app.Environment.IsDevelopment()` block (the same one gating `ApplyDeadLetterMigrations()`); the gateway does **not** reference `ECommerce.Shared.Testing.Qa`, and this seeder deliberately does not widen that gate to `Qa:Seed` — zero new package dependency.
- **Constants stay local.** The Operator persona constants live in the auth seeder; the fixture GUIDs/values live in the gateway seeder. They are **not** promoted into shared `QaPersonas`, which would force a shared-libs `<Version>` bump + repack + consumer re-pin (see [ADR-0013](0013-shared-libs-multi-package-split.md)) disproportionate to a smoke fixture. The Postman/Bruno env files remain the black-box source of truth; the lockstep invariant (values agree across `qa/postman/qa-local.postman_environment.json`, `qa/bruno/qa-local.bru`, `qa/postman/AGENT.md`, and `scripts/local-smoke-test.ps1 $Qa`) is unchanged.
- **`qa-dlq-replay-sink` inert queue.** Replay re-publishes via `RabbitMqDeadLetterPublisher.Publish` to the default exchange with `routingKey = OriginalQueue`, `mandatory: false`. The fixtures use `OriginalQueue = "qa-dlq-replay-sink"`, a queue no service binds, so a replayed message is silently dropped — replay returns `202` with a `newMessageId` and no downstream re-consumption, making the smoke coverage a deterministic status transition (`Pending`→`Replayed`) rather than a consumer-coupled assertion. An empty `OriginalQueue` would throw `publish_failed`, so the sink name is intentionally non-empty.

### Production safety — the gate is the contract

The Development / `Qa:Seed` gate is the single trustworthy signal that QA artifacts never reach a non-dev environment. It is verified false in **every** AKS manifest: `kubernetes/aks-{dev,sandbox,staging,prod}-{auth,api-gateway}.yml` all set `ASPNETCORE_ENVIRONMENT=Production` and none set `Qa__Seed`. QA seeding therefore runs **only** in the local `docker-compose` stack (Development).

**Forbidden:** do not add `Qa__Seed` (or `Qa:Seed`) to any non-dev manifest — Kubernetes, Azure Pipelines variables, Bicep, or any deployed config. Doing so would enable the auth seeder and insert the break-glass Operator account into that environment. `docker-compose.yaml` sets `Qa__Seed=true` on `auth` and `gateway` only as self-documenting intent (redundant under Development; the gateway block keys off `IsDevelopment()` and ignores the variable entirely).

## Consequences

- The DLQ operator API gains positive (2xx) smoke coverage on every clean stack without shipping a credential or synthetic dead letters to production. A regression in the list/detail/replay/batch/discard response shapes now fails CI.
- The Operator account and DLQ fixtures cannot be seeded in any cluster: all AKS namespaces run Production with no `Qa__Seed`. The break-glass credential exists only on a developer's local stack.
- Two different gate predicates now coexist by design: auth uses `IsQaSeedingEnabled` (Development OR `Qa:Seed`), the gateway uses `IsDevelopment()` only. Both are false in Production; the gateway's narrower gate avoids a new `ECommerce.Shared.Testing.Qa` dependency. The asymmetry is recorded here so a future reader does not "unify" them and accidentally couple the gateway to the Qa package.
- Cost: persona/fixture constants are duplicated between the seeders and the env files rather than centralised. The lockstep invariant (four dataset surfaces) is the mitigation; any change must touch all four. This is the deliberate trade-off from [ADR-0013](0013-shared-libs-multi-package-split.md).
- Follow-up risk flagged: if a future change sets `mandatory: true` on the DLQ publisher, `qa-dlq-replay-sink` must be declared as a real queue or replay will return `publish_failed`. The inert-sink assumption is load-bearing for the replay smoke.

## Composes

- **Composes [ADR-0004](0004-rabbitmq-fanout-with-dlq-and-operator-api.md) by reference.** The DLQ exchange, operator API surface, and `RequireOperator` policy are unchanged; this ADR only adds an env-gated way to populate the operator's working set for QA.
- **Composes [ADR-0013](0013-shared-libs-multi-package-split.md) by reference.** The "constants stay local, no shared-libs bump for a smoke fixture" decision follows directly from the lockstep-versioning trade-off recorded there.
- Does not supersede any prior ADR.
