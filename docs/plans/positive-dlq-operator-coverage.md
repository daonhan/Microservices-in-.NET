# Plan: Positive DLQ Operator Coverage

> Source PRD: [docs/prd/PRD-Positive-Dlq-Operator-Coverage.md](../prd/PRD-Positive-Dlq-Operator-Coverage.md)

Builds on [PRD-Postman-Smoke-Testing](../prd/PRD-Postman-Smoke-Testing.md) (folder `06 DLQ Operator`), [PRD-Qa-Smoke-Gap-Closure](../prd/PRD-Qa-Smoke-Gap-Closure.md) (same `Qa:Seed` seam + seeded-fixture pattern), [PRD-DLQ-Replay-UI](../prd/PRD-DLQ-Replay-UI.md) (the operator API).

## Architectural decisions

Durable across all phases:

- **Token service unchanged.** `auth-microservice/Auth.Service/Domain/Tokens/JwtTokenService.cs` copies `user.Role` verbatim into `user_role`. A seeded `Role="Operator"` user yields an Operator token through `POST /login`. `ServiceTokenService` is not touched.
- **Single seeding gate.** Both seeders ride `QaSeedingExtensions.IsQaSeedingEnabled` (`Development` OR `Qa:Seed=true`). Auth already runs `MigrateDatabase()` + `SeedQaData()` behind it (`Auth.Service/Program.cs:38-43`). The gateway already gates `ApplyDeadLetterMigrations()` on `app.Environment.IsDevelopment()` (`ApiGateway/Program.cs:45`) and does not reference `ECommerce.Shared.Testing.Qa` — reuse that block (no new package dependency).
- **Runtime seeders, not migrations.** Insert at startup, idempotent (insert-if-absent), mirroring Saga's inline `SeedQaOperatorOutboxFixture` (`ExecuteSqlInterpolated` + `IF NOT EXISTS … INSERT`). Deliberate divergence from migration-based `SeedQaData_Auth` so the high-privilege account + synthetic dead letters never ride a prod migration.
- **Constants local, not shared `QaPersonas`.** Avoids a shared-libs `<Version>` bump + 10-package repack + consumer re-pin. Operator persona constants → auth-local; fixture GUIDs/values → gateway-local. Postman/Bruno env files are the black-box source of truth.
- **Replay side-effect-free.** `RabbitMqDeadLetterPublisher.Publish` → default exchange, `routingKey=OriginalQueue`, `mandatory:false`. Inert sink `qa-dlq-replay-sink` (no subscriber) → message dropped, `newMessageId` returned, replay 202. Empty `OriginalQueue` throws, so the sink is non-empty.
- **Fixture layout.** Five rows, fixed GUIDs `f0000000-0000-0000-0000-00000000000N`, all `Status=Pending(0)`, `Origin=DeadLetter(0)`, `Service="qa-operator"`, `EventType="Qa.OperatorSmokeEvent"`, `OriginalQueue="qa-dlq-replay-sink"`, `Payload="{}"`, fixed `FailedAt`/`CorrelationId`. `…0001` list/detail (never mutated); `…0002` single replay; `…0003`/`…0004` batch replay; `…0005` discard.
- **Idempotent across reruns.** The gateway seeder resets the four mutating rows to Pending on every boot (`UPDATE … SET status=0, replayed_at=NULL, discarded_at=NULL`), so a `RESET=0` rerun does not 409. `RESET=1` (runner default) wipes volumes + reseeds.
- **Lockstep GUIDs.** Every new GUID/persona is mirrored across `qa/postman/qa-local.postman_environment.json`, `qa/bruno/environments/qa-local.bru`, `qa/postman/AGENT.md`, and `scripts/local-smoke-test.ps1 $Qa` per `docs/qa/README.md`.

---

## Phase 1: Tracer bullet — seeders + Operator login + DLQ list 200

**User stories**: 2, 3, 4, 7, 8, 11.

### What to build

The thinnest end-to-end vertical proving the whole architecture: an Operator credential logs in, a seeded `dead_letter_messages` row is listed.

- **Create** `auth-microservice/Auth.Service/Infrastructure/Data/EntityFramework/AuthQaOperatorSeeder.cs` — `SeedQaOperatorUser` seed action: scope → `AuthContext`, insert `operator@qa.test` / `Role="Operator"` if absent, reusing the existing literal QA PBKDF2 hash (decodes to the QA persona password; same `IPasswordHasher<User>` login uses — no re-hash). Define Operator persona constants here.
- **Modify** `auth-microservice/Auth.Service/Program.cs:43` — pass the seed action into the existing `app.SeedQaData(sp => sp.SeedQaOperatorUser())` (already gated by `IsQaSeedingEnabled`).
- **Create** `api-gateway/ApiGateway/Infrastructure/Seeding/DeadLetterQaFixtureSeeder.cs` — `SeedQaDeadLetterFixture`: scope → `DeadLetterDbContext`, idempotent insert of the five rows + reset-to-Pending UPDATE for the four mutating GUIDs. Mirror `SeedQaOperatorOutboxFixture`. Define fixture constants here.
- **Modify** `api-gateway/ApiGateway/Program.cs:45-48` — inside the existing `IsDevelopment()` block, after `ApplyDeadLetterMigrations()`, call `app.SeedQaDeadLetterFixture()`.
- **Modify** `qa/postman/qa-local.postman_environment.json` — add `operatorEmail`, `operatorPassword` (secret), and the five fixture GUID vars.
- **Modify** `qa/postman/ECommerce-Smoke.postman_collection.json` folder 06 — prepend `login-operator` (capture `operatorToken`) and `dlq-list-operator` (`GET …/failures?service=qa-operator` → 200, `items` contains the list GUID). Keep all negative requests.

Establishes the seeder seam + Operator persona + fixture GUIDs every later slice builds on.

### Acceptance criteria

- [ ] On a clean stack, `POST {{authBaseUrl}}/login` with the Operator persona returns 200 and a token whose decoded `user_role == "Operator"`.
- [ ] `newman run … --delay-request 750 -r cli,json` over folder 06's Phase-1 requests: `login-operator` 200 (captures `operatorToken`), `dlq-list-operator` 200 with `items` containing the seeded list GUID; `assertions.failed == 0`.
- [ ] Existing folder-06 negative requests (401 anonymous, 403 service-role, 403 admin) still green.
- [ ] With `ASPNETCORE_ENVIRONMENT=Production` and no `Qa__Seed`, neither seeder runs (no `operator@qa.test`, no `qa-operator` DLQ rows).
- [ ] `qa-local.postman_environment.json` carries `operatorEmail`, `operatorPassword`, and the five fixture GUIDs.

---

## Phase 2: Remaining four endpoints (Postman)

**User stories**: 1, 5, 6, 9.

### What to build

Complete positive coverage of the operator API in `qa/postman/ECommerce-Smoke.postman_collection.json` folder 06, in order:

- `dlq-detail-operator` — `GET /failures/{{operatorDlqListId}}` → 200, `message.id` matches. Confirm `DeadLetterStatus` serialization (number vs string) against a real response before asserting.
- `dlq-single-replay-operator` — `POST /failures/{{operatorDlqReplayId}}/replay` → 202, `{id,newMessageId}`; follow with a detail re-GET asserting status Replayed.
- `dlq-batch-replay-operator` — `POST /failures/replay-batch` `{"ids":[A,B]}` → 200, `items` length 2, each `status=="success"` + `newMessageId`.
- `dlq-discard-operator` — `POST /failures/{{operatorDlqDiscardId}}/discard` `{"reason":"qa smoke discard"}` → 202; follow with a detail re-GET asserting status Discarded.

Rename the folder `06 DLQ Operator`; update its description to drop "positive coverage out of scope".

### Acceptance criteria

- [ ] Full `RESET=1 ./qa/postman/run-smoke.sh` run: folder 06 shows 5 positive + 3 negative requests green; verdict PASS (`run.stats.assertions.failed == 0`).
- [ ] `dlq-single-replay-operator` returns 202 with `newMessageId`; the follow-up detail shows the row Replayed.
- [ ] `dlq-batch-replay-operator` returns 200 with two `success` items.
- [ ] `dlq-discard-operator` returns 202; the follow-up detail shows the row Discarded.
- [ ] A second run without `down -v` (`RESET=0`) is still green (mutating rows reset to Pending on reboot).
- [ ] Folder description no longer says positive coverage is out of scope.

---

## Phase 3: Bruno mirror + CI wiring

**User stories**: 1, 5.

### What to build

- **Create** `qa/bruno/dlq-operator/*.bru` mirroring `qa/bruno/saga-operator/*` (login-operator, list, detail, single replay, batch replay, discard) with the same assertions as Phase 2.
- **Modify** `qa/bruno/qa-local.bru` — add the seven new vars (operator persona + five fixture GUIDs), lockstep with the Postman env.
- **Modify** `.github/workflows/smoke-test.yml` `bruno-smoke` job — `Copy-Item … 'qa/bruno/dlq-operator'` next to `saga-operator`; add an `Invoke-Bruno 'dlq-operator' @(...)` batch.

### Acceptance criteria

- [ ] `npx --yes @usebruno/cli@3.3.0 run qa/bruno/dlq-operator --env-file qa/bruno/environments/qa-local.bru` is green on a freshly booted stack.
- [ ] `.github/workflows/smoke-test.yml` lists `dlq-operator` in the `Copy-Item` block and runs it as an `Invoke-Bruno` batch.
- [ ] `bruno-smoke` step summary shows a passing `dlq-operator` batch.
- [ ] `qa/bruno/environments/qa-local.bru` mirrors the seven new vars key-for-key with the Postman env.

---

## Phase 4: Tests (hermetic guardrails)

**User stories**: 5, 6.

### What to build

- **Auth** — `auth-microservice/Auth.Tests/Qa/` seed-presence test: `operator@qa.test` exists with `Role="Operator"`; the literal hash verifies with the QA persona password via `IPasswordHasher<User>`. Optional `WebApplicationFactory` test: `POST /login` Operator → decoded `user_role == "Operator"`.
- **Gateway** — `api-gateway/ApiGateway.Tests/Integration/` HTTP test via `GatewayWebApplicationFactory.CreateJwt("Operator")` over a list-backed `IDeadLetterStore` seeded with the five fixtures: list 200 / detail 200 / replay 202+Replayed / batch 200+success / discard 202+Discarded.
- **Gateway** — seeder idempotency test: run `SeedQaDeadLetterFixture` twice → five rows; the four mutating targets are Pending after reset.

### Acceptance criteria

- [ ] `dotnet test auth-microservice` green; the seed-presence test pins `Role="Operator"` and password verification.
- [ ] `dotnet test api-gateway` green; the integration test exercises all five endpoints with an Operator JWT and asserts the status transitions.
- [ ] The idempotency test proves a double-run yields five rows with mutating targets reset to Pending.

---

## Phase 5: Config + docs + ADR

**User stories**: 2, 7, 8, 10.

### What to build

- **Modify** `docker-compose.yaml` — add `Qa__Seed=true` to `auth` and `gateway` as self-documenting (functionally redundant under Development). No prod/staging change.
- **Create** `docs/qa/scenarios/06-dlq-operator.md` mirroring `05-saga-operator-abort.md`: the five fixture GUIDs, the inert `qa-dlq-replay-sink`, the Operator persona, the flow + SQL probes (`SELECT id,status,replayed_at,discarded_at FROM dead_letter_messages WHERE id IN (...)`).
- **Modify** `qa/postman/AGENT.md`, `qa/postman/README.md`, `docs/qa/README.md` — folder-06 outcome row, env-var count, persona/seeded-GUID tables.
- **Modify** `scripts/local-smoke-test.ps1 $Qa` hash — add the Operator persona + five GUIDs (lockstep invariant).
- **Create** an ADR under `docs/adr/` recording: the divergence from migration-based QA seeding to env-gated runtime seeders for the high-privilege Operator credential + DLQ fixtures; the `Qa:Seed`/Development gate (false in all prod/staging manifests; forbid adding `Qa__Seed` to any non-dev manifest); the `qa-dlq-replay-sink` inert-queue choice. Reference it from `api-gateway/CLAUDE.md` + `auth-microservice/CLAUDE.md`.

### Acceptance criteria

- [ ] Operator persona + five GUIDs are consistent across `qa/postman/qa-local.postman_environment.json`, `qa/bruno/qa-local.bru`, `qa/postman/AGENT.md`, and `scripts/local-smoke-test.ps1 $Qa`.
- [ ] `docs/qa/scenarios/06-dlq-operator.md` documents the seed, the inert sink, and the SQL probes.
- [ ] The ADR is committed and referenced from both service `CLAUDE.md` files; it states the prod-safety argument and forbids `Qa__Seed` in non-dev manifests.
- [ ] A full `RESET=1` smoke run is green end-to-end.
