# PRD — QA Smoke Gap Closure (saga-operator + shipping terminals)

> Status: draft. Synthesized from the 2026-05-26 `qa/bruno` smoke run that surfaced three gaps not covered by [PRD-Smoke-Test-Bruno-Cli](./PRD-Smoke-Test-Bruno-Cli.md) or [PRD-Smoke-Test-Saga-Hardening](./PRD-Smoke-Test-Saga-Hardening.md). Builds on [PRD-Qa-Dump-Dataset](./PRD-Qa-Dump-Dataset.md). Successor concern: stand up the `saga-operator` Bruno suite as a first-class CI-runnable surface and remove the false-failure noise from `04-admin-ops/shipping`.

## Problem Statement

As a release manager I expect the `qa/bruno/` collection to drive every documented scenario green from a clean stack with one command per suite. Today three known gaps make that impossible:

1. **`saga-operator` suite cannot complete under Bruno CLI 3.3.0.** Step `01-issue-service-token.bru` posts an `application/x-www-form-urlencoded` body to Auth `/token`. Under Bruno CLI 3.3.0 the body is dropped on the wire (Auth replies `400 Unexpected request without body`); `02-list-sagas` then 401s because the service token variable never populates. The same request succeeds via Bruno desktop and via `curl`, so the suite is silently desktop-only. CI cannot extend coverage to operator endpoints.
2. **`saga-operator` 03-detail / 04-retry / 05-abort have no deterministic Running saga to act on.** Happy, decline, and stock-shortage flows all complete (Confirmed / Cancelled) within milliseconds. By the time step 02 lists `?status=Running`, the result set is empty, `operatorSagaId` never sets, and 03–05 either skip or fail. The scenario document (`docs/qa/scenarios/05-saga-operator-abort.md`) describes a manual setup; there is no fixture or seam to park a saga in `Running` automatically.
3. **`04-admin-ops/shipping` reports two by-design failures on a sequential run.** Steps 05/06/07 (`deliver-dispatched`, `fail-dispatched`, `return-dispatched`) all target the same fixture `c0000000-...04`. Per `docs/qa/scenarios/04-admin-ops.md:210`, the three terminal transitions are mutually exclusive and the doc tells operators to `docker compose down -v && up` between each. Run sequentially the suite emits two `409 Conflict` results that look like regressions but are not. The shape invites accidental "the suite is failing" alarms and prevents the shipping admin folder from joining the Bruno CLI smoke job as a single ordered batch.

Concretely:

- The smoke gate covers 41/43 Bruno requests today; 2/43 are noise from gap (3) and the full saga-operator suite is excluded from CI entirely.
- The Bruno desktop / Bruno CLI parity gap means the dataset has surfaces that QA can see but CI cannot.
- Operators have no automated way to verify the operator-API contract (`/operator/api/sagas`, `/retry`, `/abort`) on every PR.

## Solution

Close all three gaps with surgical changes scoped to the QA collection, one seed-side hook for saga parking, and one shipping seeder split. No new operator endpoints, no new auth flow, no new test runner.

1. **Service-token issuer rewritten for Bruno CLI parity.** Replace `qa/bruno/saga-operator/01-issue-service-token.bru` with a shape that Bruno CLI 3.3.0 transmits cleanly. Two viable options, picked by experiment: (a) move the token-fetch into a `script:pre-request` block on `02-list-sagas.bru` that uses `bru.runRequest` against a JSON helper request, OR (b) keep step `01` but author the body as a raw text body with an explicit `Content-Type` header and a tests block that proves `200`. Whichever shape passes against the unchanged Auth `/token` endpoint wins.
2. **Deterministic Running-saga fixture.** Add a seeded saga-state row (driven by the QA dataset toggle, alongside the existing seeded customers/products/shipments) that places a single order saga in `Running` at a step the operator API can list. The fixture must satisfy `GET /operator/api/sagas?type=Order&status=Running` returning at least one row with a known `sagaId`. The fixture's existence is conditioned on the same `Qa:Seed` flag the dataset already gates on, so production stacks never see it. The new `operatorSagaId` is added to `qa/bruno/qa-local.bru` alongside the existing fixed IDs.
3. **Per-terminal shipping fixtures.** Split fixture `c0000000-...04` into three siblings (e.g. `...04`, `...06`, `...07`) so `deliver-dispatched` / `fail-dispatched` / `return-dispatched` each act on their own seeded shipment. Update the seeder, scenario doc, and the three `.bru` files to point at the new IDs. After the split the shipping admin folder runs sequentially 8/8 green.
4. **Docs callouts.** `docs/qa/README.md` gains: (a) a note that Bruno CLI 3.3.0 has a known regression around `body:form-urlencoded` and the workaround we landed; (b) confirmation that shipping terminals are now per-fixture rather than mutually exclusive. Existing `docs/qa/scenarios/04-admin-ops.md` and `docs/qa/scenarios/05-saga-operator-abort.md` are amended in lockstep with the seed/fixture changes.
5. **CI parity.** Once the three suites are deterministic, the `bruno-smoke` job from [PRD-Smoke-Test-Bruno-Cli](./PRD-Smoke-Test-Bruno-Cli.md) gains two new scenario batches: the full `saga-operator` suite (5 requests) and the `04-admin-ops/shipping` suite as a single sequential batch (8 requests). Coverage rises from 41 to 54 CI-gated requests on a clean stack run.

## User Stories

1. As a release manager, I want the `qa/bruno/saga-operator` suite to pass under Bruno CLI on a clean stack, so that CI can prove every PR preserves the operator-API contract.
2. As a release manager, I want a documented Bruno CLI workaround for the `body:form-urlencoded` regression, so that future suites that need form bodies do not silently break on CI while passing on desktop.
3. As a release manager, I want the `04-admin-ops/shipping` suite to run sequentially with no false-positive 409s, so that suite-level pass/fail is a single trustworthy signal in CI.
4. As a QA engineer, I want a deterministic Running-saga fixture, so that I can verify the operator-detail/retry/abort endpoints from Bruno desktop without manually parking a saga via SQL.
5. As a QA engineer, I want the seeded `operatorSagaId` exposed in `qa-local.bru`, so that I can re-run saga-operator scripts without copy-pasting GUIDs.
6. As a QA engineer, I want the shipping admin-ops doc to describe per-terminal fixtures, so that I do not have to `docker compose down -v` between transitions when verifying a regression.
7. As a backend engineer touching the saga-orchestrator, I want CI to fail when my change breaks `/operator/api/sagas`, `/retry`, or `/abort`, so that the dlq-replay operator surface stays trustworthy.
8. As a backend engineer touching the shipping aggregate, I want a CI signal when `deliver`, `fail`, or `return` transitions stop emitting the documented response shape, so that drift between the operator API and the runbook is caught at PR time.
9. As an SRE running QA scripts in production-shape stacks, I want the new Running-saga fixture gated on the same `Qa:Seed` toggle as the existing dataset, so that no production environment ever sees a seeded saga in flight.
10. As an SRE running QA scripts, I want the Bruno suites to require only `bru run` plus the staged-collection convention documented in `docs/qa/README.md`, so that no new tooling enters the CI image.
11. As a documentation maintainer, I want `docs/qa/scenarios/05-saga-operator-abort.md` to drop the manual "park a saga via SQL" preamble once the fixture lands, so that QA runbooks shrink rather than grow.
12. As a documentation maintainer, I want `docs/qa/scenarios/04-admin-ops.md:210` rewritten to describe sibling fixtures, so that the runbook stops telling operators to reset the entire stack between checks.
13. As an operator running the saga-operator collection, I want step `02-list-sagas` to return a non-empty array against a freshly booted stack, so that subsequent requests in the chain have an `operatorSagaId` set without manual intervention.
14. As an operator running the shipping admin-ops collection, I want each terminal transition to leave the rest of the suite green, so that one accidental click does not invalidate the run.
15. As a Bruno-collection author writing a new suite that needs a service token, I want a documented pattern (form-body workaround or pre-request hook), so that I don't reinvent the same fix per suite.

## Implementation Decisions

- **Service-token issuance.** The Auth `/token` endpoint is **not** changed. It already accepts the seeded `api-gateway` / `dev-api-gateway-secret` form payload via curl. Only the Bruno surface changes: rewrite `saga-operator/01-issue-service-token.bru` (or fold its work into a pre-request script on `02-list-sagas.bru`) to a shape Bruno CLI 3.3.0 transmits. The decision between "rewrite step 01" vs "fold into 02 pre-request" is made empirically; whichever shape passes wins.
- **Saga parking seam.** Reuse the existing `Qa:Seed` / `Qa__Seed` toggle that gates the QA dataset. The seeded Running saga lives in the same seeder module that produces shipments and customers — no new flag, no new composition root entry. The fixture's `sagaId` is a fixed GUID, written next to the existing shipment GUIDs in `qa/bruno/qa-local.bru`.
- **Saga step choice.** The fixture parks the saga at the **payment authorization** step, because that is the step Saga's `02-list-sagas` example query targets (the operator runbook also shows examples at this step). The seeded saga sets `status=Running` with no advance work — operators see one row and one transition history entry.
- **Shipping fixtures.** Three new sibling seeded shipments under the existing seed module, all in `Dispatched` state, each labeled with the terminal transition they exist to exercise (Delivered / Failed / Returned). The original `c0000000-...04` is retained as the "Delivered" fixture so the existing 05 step's GUID does not move; the new GUIDs slot into 06 and 07.
- **Carrier + tracking metadata.** Each new shipping fixture gets its own tracking number alongside the existing `QA-TRACK-DISPATCHED-001`, so the carrier webhook test does not contend with terminal transitions on the same row.
- **No new operator endpoints.** Coverage is purely additive on existing `/operator/api/sagas` + transition routes.
- **CI wiring.** The `bruno-smoke` job picks up the new suites by listing the suite directories the same way it lists `01-happy-path` today. No new workflow file. Saga-operator runs after the happy path (so the seeded Running saga is the only Running saga in the listing).
- **No data race with happy/decline/shortage runs.** The seeded Running saga is a synthetic row attached to a synthetic order id that no other scenario references. Happy/decline/shortage create real sagas that complete to terminal states; the seeded fixture remains the only `Running` row at the moment `02-list-sagas` runs.
- **Drift guardrail.** Any new GUID added to the seeder is mirrored in `qa-local.bru` and (if the legacy harness still runs) `scripts/local-smoke-test.ps1` `$Qa` hash, consistent with the existing rule in `docs/qa/README.md`.

## Testing Decisions

A good test here observes the external contract that operators and CI care about — never the seeder's wiring or the saga's internal step machine. The seeded fixtures are valuable because the `.bru` assertions can pin response shape end-to-end without coupling to handler internals.

Module-by-module:

1. **Service-token issuer Bruno step.** Tests live in the `.bru` itself: assert `200`, `token` is a non-empty string, `expiresIn` > 0. Prior art: every `01-login-*.bru` across the suites — same shape, same assertions. The Bruno CLI run is the test runner; no new server-side test needed since Auth `/token` already has unit + endpoint coverage (`auth-microservice/Auth.Tests/Features/IssueServiceToken/`).
2. **Saga harness.** The harness is a seeder, not an endpoint, so the test is the suite itself: `02-list-sagas` must return a non-empty `items` array and a known `sagaId` on a clean stack. Prior art: `04-admin-ops/payment/02-get-authorized-payment.bru` asserts a seeded fixture row exists with exact field values. The new `02-list-sagas` test gains an assertion that the seeded `operatorSagaId` is present in `items`. Step 03 then pins the detail shape (`sagaId`, `transitions[]`); steps 04/05 assert HTTP 202 + status enum exactly as authored today.
3. **Shipping seeder split.** Per-fixture tests already exist on each `.bru` (`expect(res.body.status).to.equal("Delivered")` etc.). The split adds nothing new at the request level; it just makes the existing assertions all reachable in one run. Prior art: the symmetric `c0000000-...01`..`...05` fixtures and their respective steps in `04-admin-ops/shipping/`.

Each module is testable in isolation: the Bruno suite tests the integration contract; the underlying Auth/Saga/Shipping aggregates already have dedicated unit + endpoint tests in their respective `*-microservice/*.Tests/` projects (no new server-side tests required by this PRD).

## Out of Scope

- Replacing the PowerShell smoke harness or modifying the `bruno-smoke` job's wrapper. That sits in [PRD-Smoke-Test-Bruno-Cli](./PRD-Smoke-Test-Bruno-Cli.md).
- Adding new operator-API endpoints, role policies, or claims. The five existing routes (`/operator/api/sagas`, `/operator/api/sagas/{id}`, `/retry`, `/abort`, and the underlying `/token`) cover the coverage gain.
- Persisting the Bruno CLI form-body bug upstream (filing/tracking the regression with `@usebruno/cli`). Workaround is enough to unblock CI; upstream fix is non-blocking.
- DLQ replay scenarios. Covered by [PRD-QA-Scenario-DLQ](./PRD-QA-Scenario-DLQ.md).
- Loadtest, perf, or soak coverage of the operator API. The Bruno suite is a contract gate, not a perf gate.
- Auth `/token` JSON-body alternative. Re-shaping the endpoint to accept JSON would technically work around the CLI bug, but the existing endpoint matches RFC 6749 `application/x-www-form-urlencoded` expectations and clients (api-gateway) already speak the form shape in production.

## Further Notes

- The Bruno CLI form-body regression is reproducible under CLI 3.3.0 with `body:form-urlencoded`, `body:formUrlEncoded`, and `body:multipart-form` — all three produce an empty body on the wire. The same suite works correctly against Bruno desktop. Pin the chosen workaround to the lowest CLI version that runs the workaround clean, so a future CLI upgrade does not silently re-break the suite.
- The seeded Running saga must be re-seeded on every `docker compose down -v && up` cycle. The fixture is idempotent (insert-if-absent) the same way the seeded customers and shipments already are. There is no "advance the saga out of Running" cleanup step — the test acts on a synthetic row that never advances.
- When the chosen saga-operator step is `payment authorization`, the runbook example query in `docs/qa/scenarios/05-saga-operator-abort.md` keeps working unchanged because it already filters on `?type=Order&status=Running` rather than on a specific step name.
- Once the shipping fixtures split lands, the line in `docs/qa/scenarios/04-admin-ops.md:210` that reads "Run `docker compose down -v && up` to reset" between terminals is removed — it stops being true.
