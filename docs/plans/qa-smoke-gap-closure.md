# Plan: QA Smoke Gap Closure (saga-operator + shipping terminals)

> Source PRD: [docs/prd/PRD-Qa-Smoke-Gap-Closure.md](../prd/PRD-Qa-Smoke-Gap-Closure.md)

Builds on [PRD-Smoke-Test-Bruno-Cli](../prd/PRD-Smoke-Test-Bruno-Cli.md), [PRD-Smoke-Test-Saga-Hardening](../prd/PRD-Smoke-Test-Saga-Hardening.md), [PRD-Qa-Dump-Dataset](../prd/PRD-Qa-Dump-Dataset.md).

## Architectural decisions

Durable across all phases:

- **Auth endpoint unchanged.** `POST {authBaseUrl}/token` continues to accept `application/x-www-form-urlencoded` per RFC 6749. Only the Bruno surface changes.
- **Saga operator routes unchanged.** Coverage rides on existing `GET /operator/api/sagas`, `GET /operator/api/sagas/{id}`, `POST /operator/api/sagas/{id}/retry`, `POST /operator/api/sagas/{id}/abort`. No new endpoints, claims, or roles.
- **Single seeding gate.** Every new fixture (saga + shipping) rides the existing `QaSeedingExtensions.IsQaSeedingEnabled` predicate (`Development` OR `Qa:Seed=true`). No new flag, no new composition entry.
- **Seeding mechanism.** New seeded rows ship as EF `Migration.Up` `InsertData` calls inside `<Svc>.Service/Migrations/`, matching `20260508120000_SeedQaPhase3b_Shipping.cs`. Idempotent by primary key, gated by the existing `MigrateDatabase()` call sites that already sit behind `IsQaSeedingEnabled`.
- **Fixed GUIDs, mirrored.** Any new GUID/tracking number added to a seeder is mirrored into `qa/bruno/qa-local.bru` and (where the legacy harness still exercises it) `scripts/local-smoke-test.ps1 $Qa` hash, per `docs/qa/README.md`.
- **Saga parking step.** The seeded `Order` saga parks at `PaymentAuthorizing` with `Status=Running`. Scenario 05 already filters on `?type=Order&status=Running`, so the step choice is non-breaking.
- **No race with happy/decline/shortage.** Seeded saga uses a synthetic `OrderId` no other scenario references; it stays the sole `Running` row at the moment `02-list-sagas` runs.
- **Shipping fixture layout.** `c0000000-...04` stays the Delivered-target shipment. Two new siblings: `c0000000-...06` (Failed-target) and `c0000000-...07` (Returned-target). Each gets its own carrier tracking number.
- **Bruno CLI pinning.** Workaround is exercised against `@usebruno/cli@3.3.0` (the CI-pinned version). Higher CLI versions are out of scope for this plan.

---

## Phase 1: Bruno CLI service-token parity

**User stories**: 2, 15 (workaround + reusable pattern).

### What to build

Make `qa/bruno/saga-operator/01-issue-service-token.bru` transmit the form body cleanly under Bruno CLI 3.3.0 so subsequent steps in the chain have a populated `serviceToken`. Pick empirically between (a) rewriting step 01 with whatever body shape the CLI transmits (raw text body with explicit `Content-Type`, multipart variant, or pre-request script form) or (b) folding the token fetch into a `script:pre-request` on `02-list-sagas.bru` via `bru.runRequest` against a helper request. Whichever variant proves `serviceToken` non-empty on a CLI 3.3.0 run wins.

Add a `docs/qa/README.md` callout naming the `body:form-urlencoded` CLI 3.3.0 regression, the chosen workaround shape, and the version pin so the next CLI upgrade does not silently re-break the suite.

This phase ships independently: steps 02–05 still won't have a deterministic saga to act on (that's P2), but step 01 (or its pre-request equivalent on 02) demonstrably sets `serviceToken` and step 02 returns `200` with an `items` array — even if empty.

### Acceptance criteria

- [ ] `npx --yes @usebruno/cli@3.3.0 run qa/bruno/saga-operator/01-issue-service-token.bru --env-file qa/bruno/qa-local.bru` (or its pre-request equivalent on step 02) returns `200`; the `tests` block asserts `res.body.token` is a non-empty string and `res.body.expiresIn > 0`.
- [ ] On the same CLI version, `02-list-sagas` returns `200` with `items` as an array (length not asserted yet — that's P2) and does not 401.
- [ ] Bruno desktop run of the same step still passes (no regression).
- [ ] `docs/qa/README.md` documents the CLI 3.3.0 form-body regression, the chosen workaround shape, and the CLI version pin.
- [ ] No changes outside `qa/bruno/saga-operator/`, `qa/bruno/qa-local.bru` (if vars shift), and `docs/qa/README.md`.

---

## Phase 2: Seeded Running saga + saga-operator suite green

**User stories**: 1, 4, 5, 7, 9, 11, 13.

### What to build

A deterministic Order-saga fixture so the saga-operator suite runs end-to-end clean on a freshly booted stack. Add an EF migration in `saga-microservice/Saga.Service/Migrations/` that `InsertData`s one `SagaInstances` row plus one `OrderSagaStates` row with a fixed `SagaId` GUID, `SagaType=Order`, `Status=Running`, `CurrentStep=PaymentAuthorizing`, and a synthetic `OrderId` no other scenario references. The migration runs only when `IsQaSeedingEnabled` is true (already true for the `MigrateDatabase()` call in `Saga.Service/Program.cs:106`).

Expose the fixed `SagaId` to Bruno via a new `operatorSagaId` var in `qa/bruno/qa-local.bru` and tighten `02-list-sagas.bru` to assert the seeded id is present in `items` (the `script:post-response` already sets it from the first element; the new assertion proves it's the seeded one). Steps 03/04/05 already act on `{{operatorSagaId}}` — they need no URL change, only the deterministic source.

Rewrite `docs/qa/scenarios/05-saga-operator-abort.md` to drop the `docker compose stop payment` preamble (step 1) and the `start payment` epilogue: a synthetic Running saga makes the manual park unnecessary. Step 2 (service-token) keeps the same instruction since Phase 1 has it working under CLI.

Add a Domain test in `saga-microservice/Saga.Tests/` modelled on `Shipping.Tests/Domain/QaSeedFixturesTests.cs`: assert the seeded `SagaInstance` row exists with the documented `SagaType`/`Status`/`CurrentStep` after migrations apply.

### Acceptance criteria

- [ ] On a clean stack (`docker compose down -v && up`), `GET {{sagaBaseUrl}}/operator/api/sagas?type=Order&status=Running` returns `items.length >= 1` and includes the seeded `operatorSagaId`.
- [ ] Sequential CLI run of `01-issue-service-token`, `02-list-sagas`, `03-get-saga-detail`, `04-retry-saga`, `05-abort-saga` is 5/5 green; `04` returns `202` with the seeded `sagaId`; `05` returns `202` with `status=Compensating`.
- [ ] `02-list-sagas.bru` asserts the seeded `operatorSagaId` is present in the response `items`.
- [ ] `qa/bruno/qa-local.bru` carries the new `operatorSagaId` GUID alongside existing shipment GUIDs.
- [ ] New saga seed-presence test in `Saga.Tests` passes and pins `SagaType=Order`, `Status=Running`, `CurrentStep=PaymentAuthorizing` on the seeded `SagaId`.
- [ ] `docs/qa/scenarios/05-saga-operator-abort.md` no longer instructs `docker compose stop payment` and no longer mentions manual saga parking.
- [ ] Seed migration is no-op when `IsQaSeedingEnabled` is false (verified by running the migration against a non-Development, no-`Qa:Seed` config — the seeding gate prevents `MigrateDatabase()` from running, so the migration itself is never applied).

---

## Phase 3: Per-terminal shipping fixtures

**User stories**: 3, 6, 8, 12, 14.

### What to build

Split the dispatched-shipment fixture into three siblings so `deliver-dispatched`, `fail-dispatched`, and `return-dispatched` each act on their own row. Keep `c0000000-0000-0000-0000-000000000004` (Delivered-target, existing tracking `QA-TRACK-DISPATCHED-001`). Add `c0000000-0000-0000-0000-000000000006` (Failed-target) and `c0000000-0000-0000-0000-000000000007` (Returned-target), each in `Status=Shipped` with carrier `fake-ground`, distinct tracking numbers (e.g. `QA-TRACK-DISPATCHED-FAIL-001`, `QA-TRACK-DISPATCHED-RETURN-001`), distinct `LabelRef`s, and matching `ShipmentLines` + `ShipmentStatusHistory` rows.

Surfaces touched:
- `shipping-microservice/Shipping.Service/Infrastructure/Data/EntityFramework/ShippingQaFixtures.cs` — two new `Shipment*Id` + `ShippingOrder*Id` + tracking constants.
- New `*_SeedQaPhase3c_Shipping.cs` migration (or extend Phase 3b — author's call; new migration is the safer additive choice) inserting the two new shipments/lines/history rows.
- `qa/bruno/qa-local.bru` — `shipmentFailDispatchedId`, `shipmentReturnDispatchedId`, `shipmentFailDispatchedTrackingNumber`, `shipmentReturnDispatchedTrackingNumber`.
- `qa/bruno/04-admin-ops/shipping/06-fail-dispatched.bru` — URL var swap to `{{shipmentFailDispatchedId}}`.
- `qa/bruno/04-admin-ops/shipping/07-return-dispatched.bru` — URL var swap to `{{shipmentReturnDispatchedId}}`.
- `Shipping.Tests/Domain/QaSeedFixturesTests.cs` — extend the dictionary count + assertions to cover seven shipments and the two new tracking numbers.
- `docs/qa/scenarios/04-admin-ops.md:210` rewrite — replace "mutually exclusive — pick one per fixture run / `docker compose down -v && up` to reset" with "each terminal transition acts on its own seeded fixture; the trio runs sequentially with no reset."
- `scripts/local-smoke-test.ps1` `$Qa` hash — mirror the two new IDs if the script references shipping fixtures by name.

This slice is demoable: `npx --yes @usebruno/cli@3.3.0 run qa/bruno/04-admin-ops/shipping --env-file qa/bruno/qa-local.bru` is 8/8 green on a freshly booted stack with no env-var overrides.

### Acceptance criteria

- [ ] `dotnet test shipping-microservice/Shipping.Tests` passes; the dictionary in `QaSeedFixturesTests` asserts seven seeded shipments, the two new ones at `Status=Shipped` with their tracking numbers and `LabelRef`s.
- [ ] Sequential CLI run of the full `04-admin-ops/shipping` folder (`01`..`08` + `webhook.bru`) is green on a clean stack with no `--env-var` overrides.
- [ ] No `409 Conflict` responses on steps 05/06/07 in the same run.
- [ ] `qa/bruno/qa-local.bru` mirrors the two new GUIDs and tracking numbers alongside the existing `shipmentDispatchedId` / `shipmentDispatchedTrackingNumber`.
- [ ] `docs/qa/scenarios/04-admin-ops.md` section 4 documents per-terminal fixtures; the "`down -v && up` to reset" sentence is removed.
- [ ] Carrier webhook test (`webhook.bru`) continues to act on `QA-TRACK-DISPATCHED-001` and is unaffected by the two new tracking numbers.

---

## Phase 4: bruno-smoke CI wiring

**User stories**: 7, 8, 10.

### What to build

Add the two newly deterministic suites to `.github/workflows/smoke-test.yml`'s `bruno-smoke` job so coverage rises from 41 to 54 CI-gated requests.

- Copy `qa/bruno/saga-operator` into the temp collection root next to the existing four scenario folders.
- Add an `Invoke-Bruno 'saga-operator' @('saga-operator/01-issue-service-token.bru', 'saga-operator/02-list-sagas.bru', 'saga-operator/03-get-saga-detail.bru', 'saga-operator/04-retry-saga.bru', 'saga-operator/05-abort-saga.bru')` batch. Place it after the happy-path setup (so the seeded Running saga is still the only one matching `?status=Running`).
- Collapse the three existing `admin-shipping-*-path` batches (`admin-shipping-return-path`, `admin-shipping-deliver-path`, `admin-shipping-fail-path` at `.github/workflows/smoke-test.yml:517-547`) into a single `Invoke-Bruno 'admin-shipping' @('04-admin-ops/shipping/01-login-admin.bru' .. '04-admin-ops/shipping/08-cancel-pending.bru')` sequential batch. Drop the per-batch `--env-var` overrides (no longer needed once P3's fixture split lands).
- Keep `admin-shipping-webhook` and `admin-shipping-cancel-path` separate if the existing wrapping requires it (cancel-path acts on `shipmentCancelPendingId`, distinct from the dispatched siblings).
- Run still `continue-on-error: true` at the job level — no change to the gate's blocking semantics.

### Acceptance criteria

- [ ] `.github/workflows/smoke-test.yml` lists the saga-operator folder in the `Copy-Item` block alongside the existing four.
- [ ] `bruno-smoke` job's step summary shows a passing `saga-operator` batch reporting 5 requests / 5 tests.
- [ ] `bruno-smoke` job's step summary shows a single passing `admin-shipping` batch reporting 8 sequential requests instead of the three split batches.
- [ ] Total `bruno-smoke` request count on a clean run is `54` (41 prior + 5 saga-operator + 8 shipping, minus duplicates from collapsed batches).
- [ ] No new GitHub Actions workflow file added; the new wiring is fully additive inside the existing `bruno-smoke` job.
- [ ] On a stack booted without `Qa:Seed=true` (would only happen in a non-Development image), the saga-operator batch is expected to fail because the seeded saga is absent — CI runs against the Development image, so this stays a green path. Documented in the README callout from P1.
