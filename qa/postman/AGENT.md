# Postman Smoke Suite (AI QA agent runbook)

This file is the contract an automated agent (Claude Code, an MCP-driven LLM,
or any CI job) uses to run the Postman smoke suite and decide pass/fail purely
from a machine-readable report.

The human-oriented variant (desktop import, Collection Runner) lives in
[README.md](README.md).

## Preconditions

1. Stack is clean and healthy:

   ```bash
   docker compose down -v
   docker compose up --build
   # then poll http://localhost:{8000,8001,8002,8003,8004,8005,8006,8007,8008}/health/ready
   # until each returns 200.
   ```

   Do not run the suite against a stack that was not just reset with `-v`. The
   happy-path order consumes the seeded happy basket; a second run on the
   same volumes will fail at `01 Happy Path / get-basket` (empty cart) and
   produce a misleading non-zero failure count.

2. The collection and environment are at the repo paths the command below
   assumes:

   - `qa/postman/ECommerce-Smoke.postman_collection.json`
   - `qa/postman/qa-local.postman_environment.json`

## Deterministic Newman command

```bash
newman run qa/postman/ECommerce-Smoke.postman_collection.json \
  -e qa/postman/qa-local.postman_environment.json \
  --delay-request 750 \
  -r cli,json --reporter-json-export out.json
```

- `--delay-request 750` is required. The self-contained polling
  (`pm.execution.setNextRequest`) needs the delay between attempts; without it
  the order/shipment loops spin tight and exhaust `MAX_ATTEMPTS = 80` before
  the saga catches up.
- `-r cli,json --reporter-json-export out.json` is the only contract the agent
  reads. Stdout is for humans.

## Reading `out.json`

Pass/fail is a single field:

```bash
jq '.run.stats.assertions.failed' out.json   # 0 = pass, >0 = fail
```

On failure, enumerate the failing assertions:

```bash
jq '.run.failures[] | {
  test:    .error.test,
  message: .error.message,
  request: .source.name,
  folder:  .parent.name
}' out.json
```

Also useful:

- `.run.stats.requests`         — total requests dispatched (68 on a green run;
                                  higher when polling iterates).
- `.run.stats.assertions.total` — total assertions evaluated.
- `.run.executions[]`           — per-request response code, body, timings.

Do not parse the CLI reporter output; field positions are formatted for
terminal display and may shift between Newman versions.

## Scenario → expected outcome

| Folder                            | Terminal assertion                                   | Codes                              |
| --------------------------------- | ---------------------------------------------------- | ---------------------------------- |
| `00 Health`                       | All nine `/health/ready`                             | `200` × 9                          |
| `01 Happy Path`                   | Order `Confirmed`, shipment `Delivered`              | `200`/`201`, polled                |
| `02 Stock Shortage`               | Order `Cancelled`                                    | `200`/`201`, polled                |
| `03 Payment Decline`              | Order `Cancelled`, stock for `productDeclineId` released | `200`/`201`, polled            |
| `04 Admin Ops` (3 sub-folders)    | Inventory restock / payment capture+refund / shipping pick→deliver and alt-paths succeed | `200`/`201`/`204` |
| `05 Saga Operator`                | `retry-saga` and `abort-saga` accepted; saga moves to `Compensating` | `202` × 2          |
| `06 DLQ Operator (authz boundary)`| Operator API rejects non-Operator callers            | `403` (service), `403` (admin), `401` (anon) |
| `07 Auth & Negative`              | JWKS published; AdminOnly rejects customer; protected endpoints reject anon and garbage | `200` (jwks), `403`, `401`, `401` |

A green run is `assertions.failed == 0` *and* every folder above appears in
`out.json` (treat a missing folder as a fail — it means a request errored hard
enough that Newman skipped downstream items).

## Postman MCP `run-collection` path

When invoked via the Postman MCP server, equivalent inputs:

- `collection`: contents of `ECommerce-Smoke.postman_collection.json`
  (or its Postman cloud collection UID).
- `environment`: contents of `qa-local.postman_environment.json`
  (or its environment UID).
- `delayRequest`: `750`.
- `reporters`: include `json` so the same `out.json` schema is returned in the
  tool response. Apply the same `run.stats.assertions.failed == 0` rule.

The collection is self-contained: no setup script, no external polling wrapper,
no global pre-request hook on the workspace is required.

## Overridable environment variables

All thirty-seven keys in `qa-local.postman_environment.json` are overridable
via `--env-var key=value`. The agent should prefer overrides to file edits.
Categories:

- **Base URLs (9):** `gatewayBaseUrl`, `authBaseUrl`, `basketBaseUrl`,
  `productBaseUrl`, `inventoryBaseUrl`, `orderBaseUrl`, `shippingBaseUrl`,
  `paymentBaseUrl`, `sagaBaseUrl`. Override these to point at a non-localhost
  stack.
- **Service credentials (2):** `serviceClientId`, `serviceClientSecret`.
- **Personas (9):** `customerHappyId`/`Email`, `customerDeclineId`/`Email`,
  `customerCancelId`/`Email`, `adminEmail`. Plus `customerPassword`,
  `adminPassword`.
- **Products (5):** `productHappyId`, `productDeclineId`,
  `productZeroStockId`, `productLowStockId`, `productRestockTargetId`.
- **Seeded shipments (10):** `shipmentPickPendingId`, `shipmentPickedId`,
  `shipmentPackedId`, `shipmentDispatchedId`, `shipmentCancelPendingId`,
  `shipmentFailDispatchedId`, `shipmentReturnDispatchedId`,
  `shipmentDispatchedTrackingNumber`, `shipmentFailDispatchedTrackingNumber`,
  `shipmentReturnDispatchedTrackingNumber`.
- **Saga + carrier (2):** `operatorSagaId`, `carrierGroundSecret`.
- **Runtime captures (8, not in env file):** `orderId`, `shipmentId`,
  `customerToken`, `adminToken`, `serviceToken`, `orderStatus`, `orderLocation`,
  `pollAttempts` are collection-level variables initialised at run start. Do
  not pre-seed them.

Personas, products, and seeded GUIDs are dataset constants. Changing them
requires lockstep updates to `qa/bruno/qa-local.bru` and `scripts/local-smoke-test.ps1`
(`$Qa` hash) — see [docs/qa/README.md](../../docs/qa/README.md).
