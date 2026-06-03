# Plan: Postman Smoke Testing (port + extend the Bruno suite)

> Source PRD: [docs/prd/PRD-Postman-Smoke-Testing.md](../prd/PRD-Postman-Smoke-Testing.md)

Builds on the existing Bruno suite (`qa/bruno/**`) and the QA dataset documented in [docs/qa/README.md](../qa/README.md). Additive only — no app, CI, Bruno, or legacy-harness changes.

## Architectural decisions

Durable across all phases:

- **Direct-to-port parity.** Requests hit each service on its own port via the same vars Bruno uses (`{{basketBaseUrl}}` `:8000`, `{{orderBaseUrl}}` `:8001`, `{{productBaseUrl}}` `:8002`, `{{authBaseUrl}}` `:8003`, `{{gatewayBaseUrl}}` `:8004`, `{{inventoryBaseUrl}}` `:8005`, `{{shippingBaseUrl}}` `:8006`, `{{paymentBaseUrl}}` `:8007`, `{{sagaBaseUrl}}` `:8008`). The gateway is only the service-token client identity and the DLQ operator host.
- **Two variable scopes.** Static dataset → `qa-local.postman_environment.json` (mirrors `qa/bruno/qa-local.bru` 1:1). Runtime values (tokens, `orderId`, `shipmentId`, `pollAttempts`) → collection variables via `pm.collectionVariables.set`.
- **Self-contained polling.** Poll requests loop to themselves with `pm.execution.setNextRequest(pm.info.requestName)` and a `pollAttempts` counter bounded at `MAX_ATTEMPTS = 80` (≈60s at 750ms). Inter-iteration delay is the runner's job (`newman --delay-request 750` / Runner "Delay 750ms"). Reset `pollAttempts` to 0 in the first poll request's pre-request script of each polled phase.
- **Three-layer assertions.** Every request asserts HTTP status + captures downstream fields + a lightweight response-shape check, per `docs/qa/README.md`.
- **Service token uses `x-www-form-urlencoded`.** Newman has no `@usebruno/cli@3.3.0` form-body regression, so the RFC-6749 body replaces Bruno's multipart workaround.
- **Schema v2.1.0.** Collection authored as `https://schema.getpostman.com/json/collection/v2.1.0/collection.json`.
- **No positive DLQ operator coverage.** `RequireOperator` needs `user_role == Operator`; no seeded credential yields it (see PRD Out of Scope). Only the DLQ authz boundary (negative) is built.
- **Lockstep.** The new environment file is a third dataset surface; dataset-changing PRs must update all three surfaces.

The canonical polling script (TERMINAL = `Confirmed`, or `Cancelled` in scenarios 02/03):

```javascript
const TERMINAL = "Confirmed";
const MAX_ATTEMPTS = 80;
const status = pm.response.json().status;
let n = Number(pm.collectionVariables.get("pollAttempts") || 0);
pm.collectionVariables.set("orderStatus", status);
if (status === TERMINAL) {
  pm.collectionVariables.set("pollAttempts", 0);
  pm.test(`order reached ${TERMINAL}`, () => pm.expect(status).to.equal(TERMINAL));
} else if (n + 1 >= MAX_ATTEMPTS) {
  pm.collectionVariables.set("pollAttempts", 0);
  pm.test(`order reached ${TERMINAL}`, () => pm.expect.fail(`stuck at ${status}`));
} else {
  pm.collectionVariables.set("pollAttempts", n + 1);
  pm.execution.setNextRequest(pm.info.requestName);
}
```

---

## Phase 1: Tracer bullet — env + collection skeleton + Health + happy path (with polling)

**User stories**: 1, 2, 3, 4.

### What to build

The thinnest end-to-end vertical that proves the whole architecture: a valid v2.1.0 collection that Newman runs against the live stack, env vars resolve, token capture works, the self-contained polling loop works, and a JSON report is emitted.

- `qa/postman/qa-local.postman_environment.json` — every key/value from `qa/bruno/qa-local.bru` (base URLs, `serviceClientId`/`serviceClientSecret`, all personas + IDs + passwords, product IDs, seeded shipment GUIDs + tracking numbers, `operatorSagaId`, `carrierGroundSecret`).
- `qa/postman/ECommerce-Smoke.postman_collection.json` with:
  - **`00 Health`** — `GET {{<svc>BaseUrl}}/health/ready` → 200 for all nine services.
  - **`01 Happy Path`** — login-customer (capture `customerToken`), login-admin (capture `adminToken`), get-basket (`:8000/{{customerHappyId}}`), get-product (`:8002/9001`), get-inventory (`:8005/9001`), place-order (`POST :8001/{{customerHappyId}}`, body `{"orderProducts":[{"productId":"{{productHappyId}}","quantity":2}]}`, capture `orderId` from `Location`), **poll-order → `Confirmed`** (self-loop), **list-shipping-by-order → until array non-empty** (capture `shipmentId`), pick, pack, dispatch (carrier `fake-ground` + shipping address body), deliver.

Verify the live `/health/ready` path and adjust if a service exposes only `/health`.

### Acceptance criteria

- [ ] `newman run qa/postman/ECommerce-Smoke.postman_collection.json -e qa/postman/qa-local.postman_environment.json --delay-request 750 -r cli,json --reporter-json-export out.json` runs Phase-1 folders with `assertions.failed == 0`.
- [ ] `00 Health` reports 9 × 200.
- [ ] Happy path reaches order `Confirmed` then shipment `Delivered` end-to-end with no external scripting (polling resolves in-collection).
- [ ] `qa-local.postman_environment.json` matches `qa/bruno/qa-local.bru` key-for-key.
- [ ] Collection imports cleanly into Postman desktop (schema v2.1.0 valid).

---

## Phase 2: Failure scenarios (stock shortage + payment decline)

**User stories**: 3.

### What to build

Reuse the Phase-1 polling pattern with TERMINAL = `Cancelled`.

- **`02 Stock Shortage`** — login-cancel (`customer-cancel@qa.test`), get-basket (`:8000/{{customerCancelId}}`), get-zero-stock (`:8005/9003`, assert on-hand 0), place-order (`9003`), **poll-order → `Cancelled`**.
- **`03 Payment Decline`** — login-decline (`customer-decline@qa.test`), get-basket (`:8000/{{customerDeclineId}}`), get-decline-product (`:8002/9002`, price `9.99`), place-order (`9002`), **poll-order → `Cancelled`**, confirm-stock-released (`GET :8005/9002`, assert available is a number ≥ released amount).

### Acceptance criteria

- [ ] Stock-shortage folder: order reaches `Cancelled` in-collection; assertions pass.
- [ ] Payment-decline folder: order reaches `Cancelled` and stock-released check passes.
- [ ] Both folders share the canonical poll script (only TERMINAL differs); `pollAttempts` resets cleanly between scenarios.

---

## Phase 3: Admin Ops (inventory + payment + shipping)

**User stories**: 3.

### What to build

Faithful port of Bruno's `04-admin-ops`, three sub-folders, each beginning with login-admin (or reusing `adminToken`):

- **Inventory** — low-stock-alert (`GET :8005/9004`, assert threshold/on-hand), restock (`POST :8005/9005/restock` `{"quantity":10}`), set-threshold (`PUT :8005/9004/threshold` `{"threshold":5}`), manual-reserve (`POST :8005/9004/reserve`), backorder (`POST :8005/9005/backorder`).
- **Payment** — get-authorized (`GET :8007/by-order/a0000000-…-000000000001`), capture (`POST :8007/b0000000-…-000000000001/capture`), get-captured (`by-order/a0…02`), refund (`POST :8007/b0…02/refund`), cancel-order (`POST :8001/{{customerHappyId}}/a0…02/cancel`).
- **Shipping** — pick-pending, pack-picked, dispatch-packed (carrier body), deliver-dispatched, fail-dispatched (`{"reason":...}`), return-dispatched (`{"reason":...}`), cancel-pending (`{"reason":...}`), webhook (`POST :8006/webhooks/carrier/fake-ground`, header `X-Carrier-Secret: {{carrierGroundSecret}}`, body tracking/statusCode).

These act on seeded fixtures (`a0…`/`b0…`/`c0…` GUIDs) and need no polling.

### Acceptance criteria

- [ ] Inventory sub-folder green: restock returns `newOnHand: 10`; threshold update returns `threshold: 5`; reserve and backorder return their documented shapes.
- [ ] Payment sub-folder green: authorized→captured→refunded transitions assert correct `status` values; cancel-order returns `Cancelled`.
- [ ] Shipping sub-folder green: pick/pack/dispatch/deliver/fail/return/cancel each return the expected status; webhook returns `InTransit`.
- [ ] Whole `04 Admin Ops` runs sequentially clean on a fresh stack with no env-var overrides.

---

## Phase 4: Saga Operator

**User stories**: 3.

### What to build

Port Bruno's `saga-operator`, using the cleaner urlencoded service-token request.

- **`05 Saga Operator`** — issue-service-token (`POST :8003/token`, `x-www-form-urlencoded` body `grant_type=client_credentials&client_id={{serviceClientId}}&client_secret={{serviceClientSecret}}`, capture `serviceToken`), list-sagas (`GET :8008/operator/api/sagas?type=Order&status=Running`, assert seeded `{{operatorSagaId}}` present), get-saga-detail (`:8008/operator/api/sagas/{{operatorSagaId}}`, assert `transitions` array), retry-saga (`POST …/retry` → 202), abort-saga (`POST …/abort` → 202, `status: Compensating`).

### Acceptance criteria

- [ ] Service-token request returns 200 with non-empty `token` using `x-www-form-urlencoded` under Newman.
- [ ] list-sagas returns 200 with `items` array including the seeded `operatorSagaId`.
- [ ] retry returns 202 with the seeded `sagaId`; abort returns 202 with `status: Compensating`.

---

## Phase 5: New coverage — Auth-negative + DLQ authz boundary

**User stories**: 5.

### What to build

The gap-closing folders that Bruno lacks. All are self-contained and require no seed changes.

- **`07 Auth & Negative`** — fetch JWKS (`GET {{authBaseUrl}}/jwks` → 200, `keys[]`; verify path vs `/.well-known/jwks.json`); AdminOnly rejects customer JWT (`POST :8005/9005/restock` with `customerToken` → 403); protected endpoint rejects anonymous (`POST :8001/{{customerHappyId}}` no auth → 401); protected endpoint rejects garbage token (`GET :8001/{{customerHappyId}}/{{orderId}}` with `Bearer not.a.jwt` → 401).
- **`06 DLQ Operator (authz boundary)`** — `GET {{gatewayBaseUrl}}/operator/api/failures` with service token → 403; with admin user JWT → 403; with no token → 401.

Verify exact 401-vs-403 codes against the live stack and adjust assertions to match.

### Acceptance criteria

- [ ] JWKS fetch returns 200 with a non-empty `keys` array.
- [ ] AdminOnly-with-customer-token returns 403; anonymous returns 401; garbage token returns 401.
- [ ] DLQ operator endpoint returns 403 for service token, 403 for admin JWT, 401 for anonymous.
- [ ] No seed or app changes were required to make this folder pass.

---

## Phase 6: Runbooks + lockstep

**User stories**: 1, 2, 6.

### What to build

- `qa/postman/README.md` (human) — prerequisites (`docker compose up --build`, wait for `/health/ready`); import collection + `qa-local` environment into the desktop app; Collection Runner with 750ms delay; the equivalent Newman one-liner; clean-stack note (`docker compose down -v` so the happy basket isn't already consumed).
- `qa/postman/AGENT.md` (AI QA agent) — the deterministic Newman command with `--delay-request 750 -r cli,json --reporter-json-export out.json`; how to read `out.json` (`run.stats.assertions.failed`, `run.failures[]`); scenario→expected-outcome table (happy→Confirmed+Delivered, stock-shortage→Cancelled, decline→Cancelled+stock-released, saga-operator→202, DLQ-authz→403/401, auth-negative→401/403); the Postman MCP `run-collection` path; the list of overridable environment variables.
- Extend the lockstep warning at `docs/qa/README.md` to name `qa/postman/qa-local.postman_environment.json` as the third dataset surface.

### Acceptance criteria

- [ ] `README.md` lets a human go from clone to green Runner run using only its steps.
- [ ] `AGENT.md` lets an agent run the suite via Newman/MCP and decide pass/fail purely from the JSON report.
- [ ] `docs/qa/README.md` lockstep note lists all three dataset surfaces.
- [ ] Full-suite verification: `down -v && up`, all nine `/health/ready` green, then the Newman one-liner exits 0 with `assertions.failed == 0`; one Postman-desktop and one MCP spot-check confirmed.
