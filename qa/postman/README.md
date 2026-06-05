# Postman Smoke Suite (human runbook)

A self-contained Postman v2.1.0 mirror of the Bruno smoke suite. Seventy-six
requests across eight folders cover health, the happy path end-to-end, the two
canonical failure paths, admin ops on inventory/payment/shipping, the saga
operator API, the DLQ operator API (positive + authz boundary), and the auth
negative boundary.

The agent-oriented variant (Newman + MCP, deterministic JSON report) lives in
[AGENT.md](AGENT.md).

## Files

| File                                          | Purpose                                  |
| --------------------------------------------- | ---------------------------------------- |
| `ECommerce-Smoke.postman_collection.json`     | The collection (schema v2.1.0).          |
| `qa-local.postman_environment.json`           | Mirrors `qa/bruno/environments/qa-local.bru` (44 keys). |

The environment file is one of three dataset surfaces that must stay in
lockstep with `Qa.Seed` (see [docs/qa/README.md](../../docs/qa/README.md)).

## Prerequisites

1. A clean local stack:

   ```bash
   docker compose down -v
   docker compose up --build
   ```

   `down -v` is required before each full-suite run. The happy-path order
   consumes the seeded happy basket; rerunning without volume reset makes
   `get-basket` return an empty cart and the place-order step fails.

2. All nine services healthy. Wait for `200` from `/health/ready` on each of
   `8000` (basket), `8001` (order), `8002` (product), `8003` (auth), `8004`
   (gateway), `8005` (inventory), `8006` (shipping), `8007` (payment), `8008`
   (saga). The collection's `00 Health` folder asserts this.

## Run from the Postman desktop app

1. **Import the collection.** `File → Import` →
   `qa/postman/ECommerce-Smoke.postman_collection.json`.
2. **Import the environment.** `File → Import` →
   `qa/postman/qa-local.postman_environment.json`. Select **qa-local** in the
   environment dropdown.
3. **Open the Collection Runner.** Select `E-Commerce Smoke`, leave all folders
   selected, set **Delay** to `750` ms, then **Run**.

The 750 ms delay is the runner equivalent of `--delay-request 750`. Polling is
self-contained: `poll-order` and `list-shipping-by-order` re-queue themselves
via `pm.execution.setNextRequest(pm.info.requestName)`, bounded at
`MAX_ATTEMPTS = 80` (~60 s at the 750 ms delay). The runner only supplies
the delay; do not add any external polling wrapper.

Expected outcome: all seventy-six requests pass (green `Test Results`). Scenario
endings:

- Happy path → order `Confirmed`, shipment `Delivered`.
- Stock shortage → order `Cancelled`.
- Payment decline → order `Cancelled`, stock for the decline product released.
- Admin ops → seeded `9004/9005`, payment `b0…`, shipping `c0…` GUIDs flip
  through their admin transitions.
- Saga operator → `202` on retry/abort against seeded `operatorSagaId`
  (`e000…0001`).
- DLQ operator → `operator@qa.test` lists/details/replays/batch-replays/discards
  the five seeded `qa-operator` fixtures (`f000…0001`–`0005`), pinning the
  `Pending`→`Replayed`/`Discarded` transitions; non-Operator callers get `403`
  for service/admin tokens, `401` for anonymous.
- Auth negative → `401` for anonymous/garbage, `403` for AdminOnly with a
  customer token.

## Newman one-liner

`docker compose down -v && docker compose up --build` (wait for all nine
`/health/ready`), then:

```bash
newman run qa/postman/ECommerce-Smoke.postman_collection.json \
  -e qa/postman/qa-local.postman_environment.json \
  --delay-request 750 \
  -r cli,json --reporter-json-export out.json
```

A clean run exits `0` with `run.stats.assertions.failed == 0` in `out.json`.

## One-shot script

[`run-smoke.sh`](run-smoke.sh) mechanizes everything above — clean stack, wait
for all nine `/health/ready`, run Newman, print the verdict — and exits non-zero
on any failed assertion:

```bash
./qa/postman/run-smoke.sh                 # full gate: down -v, up --build, run
RESET=0 ./qa/postman/run-smoke.sh         # run against the current stack
SKIP_BUILD=1 ./qa/postman/run-smoke.sh    # reset without rebuilding images
```

## Overriding individual variables

Each environment value can be overridden at the CLI without editing the file:

```bash
newman run ... -e qa/postman/qa-local.postman_environment.json \
  --env-var "gatewayBaseUrl=http://stack.local:8004" \
  --env-var "customerHappyId=<new-guid>"
```

The variables most likely to need overriding for a non-localhost stack are the
nine `*BaseUrl` keys and `serviceClientSecret`/`carrierGroundSecret`. Persona
GUIDs, product IDs, and seeded shipment GUIDs are dataset constants and should
only change in lockstep with `Qa.Seed`.
