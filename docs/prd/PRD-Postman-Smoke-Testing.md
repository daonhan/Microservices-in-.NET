# PRD: Postman Smoke Testing (port + extend the Bruno suite)

> Status: Appoved.

## Context

QA smoke testing today lives in `qa/bruno/` (49 `.bru` requests across 5 scenarios) plus the legacy `scripts/local-smoke-test.ps1` harness. CI runs both via `.github/workflows/smoke-test.yml` — the PowerShell `smoke` job is the blocking gate; `bruno-smoke` runs `@usebruno/cli@3.3.0` non-blocking during a soak. The dataset, personas, and fixtures are documented in `docs/qa/README.md` and seeded automatically in Development.

Postman/Newman is the other dominant API-testing ecosystem and the one most AI QA agents and the Postman MCP target natively. This PRD introduces a **Postman** edition of the smoke suite that faithfully ports the Bruno coverage, covers cases Bruno currently misses, and is runnable by **both a human** (Postman desktop) **and an AI QA testing agent** (Newman CLI / Postman MCP) with no external wrapper script.

Per project conventions this is an additive QA artifact (operator surfaces prefer API + tooling over HTML). It does not change app code, the Bruno collection, the legacy harness, or CI.

## Problem Statement

Two structural facts limit the current Bruno suite as an agent-runnable artifact:

1. **Bruno talks to each service directly on its own port** (e.g. basket `:8000/{customerId}`, payment `:8007/by-order/{id}`), not through the gateway. That is fine, but it is encoded only in Bruno's env file.
2. **Bruno has no in-file polling.** The `.bru` poll requests only assert that `status` is a string; the real "wait until `Confirmed`/`Cancelled`" loop lives in external PowerShell in the CI workflow. A consumer therefore cannot run the happy/failure scenarios end-to-end from the collection alone — it needs the workflow's wrapper. That makes the suite awkward for the Postman desktop Runner and for an autonomous AI QA agent.

The suite also has coverage gaps: no per-service health assertion, no authorization-boundary (negative) tests, and no exercise of the gateway DLQ operator API.

## Solution

A new `qa/postman/` artifact set:

- `ECommerce-Smoke.postman_collection.json` (Postman schema v2.1.0) — a faithful port of all five Bruno scenarios, hitting services directly on their ports, with **self-contained polling** embedded in test scripts (a request loops back to itself via `pm.execution.setNextRequest` with a bounded counter; the runner supplies the inter-iteration delay). The whole collection runs end-to-end in the Postman Runner, Newman, or Postman MCP with **zero external scripting**.
- `qa-local.postman_environment.json` — mirrors `qa/bruno/qa-local.bru` 1:1 (base URLs, personas, product IDs, seeded GUIDs).
- `README.md` — human runbook (desktop import + Newman one-liner).
- `AGENT.md` — deterministic AI-QA-agent recipe (Newman command, JSON-report interpretation, scenario→expected-outcome table, Postman MCP path, overridable variables).

New coverage beyond parity:

- **`00 Health`** — `GET /health/ready` on all nine services.
- **`07 Auth & Negative`** — JWKS fetch; AdminOnly endpoint rejects a customer JWT (403); protected endpoint rejects anonymous (401) and a garbage token (401).
- **`06 DLQ Operator (authz boundary)`** — the gateway `/operator/api/failures` endpoint rejects a service token (403), an admin user JWT (403), and anonymous (401).

## User Stories

1. As a QA engineer, I want a Postman collection I can import and run from the desktop Runner so I can smoke the whole stack without the CLI or the PowerShell harness.
2. As an AI QA testing agent, I want a self-contained collection (polling included) plus a deterministic `AGENT.md` recipe so I can run it via Newman or the Postman MCP and parse pass/fail from a JSON report.
3. As a QA engineer, I want every existing Bruno scenario reproduced (happy path, stock shortage, payment decline, admin ops, saga operator) so the Postman edition is at least at parity.
4. As a release manager, I want per-service `GET /health/ready` checks in the suite so a smoke run also confirms basic liveness across all nine services.
5. As a security-minded reviewer, I want negative/authorization tests (AdminOnly rejects customer JWT, anonymous/garbage rejected, DLQ operator boundary enforced) so the suite catches authz regressions Bruno does not cover.
6. As a maintainer, I want the new environment file called out as a third dataset surface to keep in lockstep so dataset drift is prevented.

## Implementation Decisions

### Direct-to-port parity
Requests target each service on its own port via the same env vars Bruno uses (`{{basketBaseUrl}}`, `{{paymentBaseUrl}}`, …). The gateway (`:8004`) appears only as the `api-gateway` service-token client identity and as the DLQ operator host.

### Self-contained polling (the key differentiator)
Poll requests (`poll-order`, `list-shipping-by-order`) loop to themselves with a `pollAttempts` collection variable bounded at ~80 attempts (≈60s, matching the CI budget). Inter-iteration delay is supplied by the runner — Newman `--delay-request 750`, Postman Runner "Delay 750ms" — documented in both runbooks. No external wrapper.

### Bruno → Postman translation
`bru.setVar` → `pm.collectionVariables.set`; `bru.getEnvVar` → `pm.environment.get`; Chai `tests{}` → `pm.test`/`pm.expect`; per-request bearer auth with `{{customerToken}}`/`{{adminToken}}`/`{{serviceToken}}`; `orderId` captured from the `Location` header. Each request keeps the three-layer assertion convention from `docs/qa/README.md` (status + downstream-field capture + response-shape check).

### Intentional divergences from Bruno
- The service-token request uses clean `application/x-www-form-urlencoded` (RFC 6749). Bruno's multipart workaround existed only for the `@usebruno/cli@3.3.0` form-body regression; Newman has no such bug.
- Polling is in-collection rather than external PowerShell.

### Artifacts only
New files live under `qa/postman/`. No changes to `smoke-test.yml`, the Bruno collection, or `scripts/local-smoke-test.ps1`.

## Testing Decisions

A good run is verified against a healthy local stack (`docker compose up --build`, all nine `/health/ready` green):

- `newman run qa/postman/ECommerce-Smoke.postman_collection.json -e qa/postman/qa-local.postman_environment.json --delay-request 750 -r cli,json --reporter-json-export out.json` exits `0` with `assertions.failed == 0`.
- Parity: happy → `Confirmed` then `Delivered`; stock-shortage → `Cancelled`; decline → `Cancelled` + stock released; admin-ops state transitions; saga-operator retry/abort return `202`.
- New folders: Health (9×200); Auth-negative (JWKS 200, 403, 401, 401); DLQ-authz (403/403/401).
- Both consumer paths spot-checked: Postman desktop Runner and one Postman MCP `run-collection`.
- Idempotent folders (Health, negatives) still pass on a rerun without `down -v`; the happy-path basket being empty on rerun is expected (documented), not a collection failure.

Exact `/health/ready` vs `/health`, `/jwks` vs `/.well-known/jwks.json`, and 401-vs-403 codes are verified against the live stack during implementation and assertions adjusted to match.

## Out of Scope

- **Positive DLQ operator coverage.** `RequireOperator` needs claim `user_role == Operator` (`shared-libs/ECommerce.Shared.Platform/Abstractions/AuthorizationPolicies.cs`), but the Auth service token is hardcoded to `user_role: service` (`auth-microservice/Auth.Service/Domain/Tokens/ServiceTokenService.cs`) and seeded users are only `Administrator`/`Customer`. No seeded credential yields an Operator claim, so list/detail/replay/discard happy-paths cannot be smoke-tested black-box. Only the DLQ authz boundary (negative) is in scope. Full positive DLQ coverage requires an Operator-role seed (and likely a `dead_letter_messages` fixture) — a separate follow-up.
- No CI workflow changes (the Postman edition stays a human/agent on-demand artifact; `bruno-smoke` remains the soak gate).
- No changes to the Bruno collection or the legacy PowerShell harness.
- No app or seed-data changes.

## Further Notes

The new `qa-local.postman_environment.json` is a **third dataset surface**. Any PR changing persona emails, passwords, product IDs, customer IDs, seeded shipment GUIDs, or the saga GUID must update all three: `qa/bruno/qa-local.bru`, `scripts/local-smoke-test.ps1` `$Qa`, and `qa/postman/qa-local.postman_environment.json` (extend the existing lockstep warning at `docs/qa/README.md`).

Implementation plan: [docs/plans/postman-smoke-testing.md](../plans/postman-smoke-testing.md).
