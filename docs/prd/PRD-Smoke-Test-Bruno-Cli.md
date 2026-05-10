# PRD — Replace Smoke Harness with Bruno-CLI Run

> Status: draft. Scope: swap CI's `Run smoke scenarios` step from `scripts/local-smoke-test.ps1` to a Bruno-CLI-driven run of the existing `qa/bruno/` collection. Successor to [PRD-Qa-Dump-Dataset](./PRD-Qa-Dump-Dataset.md) (which delivered the Bruno collection) and [PRD-Smoke-Test-Saga-Hardening](./PRD-Smoke-Test-Saga-Hardening.md) (which hardened the PowerShell harness now in production).

## Problem Statement

As a release manager I have two parallel descriptions of the same QA flows: a PowerShell harness (`scripts/local-smoke-test.ps1`) that drives the saga end-to-end in CI, and a Bruno collection (`qa/bruno/**/*.bru`) that QA uses interactively. They are not the same harness — they are two re-implementations of the same scenarios that must be kept in lockstep manually.

Concrete pain:

1. The PowerShell harness exercises only a sliver of admin coverage (a single inventory low-stock GET plus a restock POST). The Bruno collection covers the full inventory/payment/shipping admin tree (`qa/bruno/04-admin-ops/`). CI never runs the wider admin surface.
2. Persona/product/shipment IDs are duplicated: PowerShell hash `$Qa` in `scripts/local-smoke-test.ps1:46-62` mirrors `qa/bruno/qa-local.bru:1-32`. A change in one drifts silently from the other; nothing fails until QA opens the collection or CI dies on a moved ID.
3. New QA scenarios authored as `.bru` files cannot be merge-gated without porting them to PowerShell. The cost of "land it in CI" is a re-implementation, not a wiring change.
4. The PowerShell harness uses `Invoke-RestMethod`/`Invoke-WebRequest` plumbing; the Bruno collection uses Bruno scripts. A response-shape regression (e.g. `id` vs `shipmentId`, see `PRD-Smoke-Test-Saga-Hardening` decision #6) has to be patched in two places.

## Solution

Add a parallel `bruno-smoke` CI job that drives the existing `qa/bruno/` collection via Bruno-CLI against the same `docker compose` stack the PowerShell harness uses today. Run both jobs in parallel during a soak period. After ten consecutive green runs of the Bruno job on `main`, flip Bruno to required and demote the PowerShell job to manual-trigger only (`workflow_dispatch`). The PowerShell harness file itself stays in the repo as an ad-hoc local debugging tool.

Specifically:

1. **Bruno-CLI invocation** — workflow uses `npx --yes @usebruno/cli@<pinned-version> run ...` per request. No global install, no `package.json` checked into `qa/bruno/`. Version pin lives in the workflow yaml.
2. **Per-step assertions in `tests` blocks** — every `.bru` in `qa/bruno/` gains a `tests` block asserting HTTP status, critical body fields (token presence, location header, IDs, status enums), and JSON-schema-style shape checks against the response. The `.bru` is the single source of truth for what a "passing" call looks like.
3. **Saga waits inlined in the workflow** — Bruno has no native poll-until-condition. The workflow step inlines a pwsh polling loop that calls `bru run <wait.bru> --output json` repeatedly with a deadline, parses the JSON output, and asserts the saga state. No helper script extracted; the loop is copy-pasted per wait point. (Decision: explicitly user-chosen over a helper to keep the wrapper inside the workflow yaml.)
4. **JSON output + custom summary** — `bru run --output json` per request; the workflow wrapper consumes the JSON, prints a one-line pass/fail per request to stdout, and writes failures to `$env:GITHUB_STEP_SUMMARY`. No JUnit reporter, no test-reporter action.
5. **Scenario coverage = parity + full admin folder** — Bruno-CLI runs `01-happy-path` (matches PowerShell `happy`), `02-stock-shortage` (matches `stock-out`), `03-payment-decline` (matches `decline`), and the **entire** `04-admin-ops` tree (inventory, payment, shipping, webhook). This is the explicit reason to switch; CI covers more than it does today.
6. **Single CI job, sequential scenarios** — mirrors today's `Run smoke scenarios` step shape: one stack boot, scenarios run in order. Stuck saga blocks subsequent scenarios just like today.
7. **Drift between `qa-local.bru` and `$Qa` audited at PR time** — both files remain. The runbook gains a "drift-check" call-out and the PR template (or runbook) documents the expectation. No tooling enforces it.
8. **Soak phasing** — the `bruno-smoke` job lands with `continue-on-error: true`. After ten consecutive green runs on `main`, a follow-up PR removes that flag and reduces the PowerShell `smoke` job to `on: workflow_dispatch` only.

After cutover, the merge-gating CI surface is wider (full admin coverage), authored in the same DSL QA uses interactively, with no PowerShell re-implementation step in the loop.

## User Stories

1. As a release manager, I want CI to gate on the same `.bru` files QA exercises by hand, so adding a new scenario to the Bruno collection automatically widens CI coverage with no PowerShell port.
2. As a release manager, I want a soak period where Bruno runs alongside the PowerShell harness without blocking merges, so I can compare green/red verdicts on real PRs before flipping which one is required.
3. As a release manager, I want the cutover to require ten consecutive green Bruno runs on `main` before the flip, so a flaky harness does not become the merge gate.
4. As a release manager, I want the PowerShell harness to remain in the repo (as a manual-trigger CI job and a local script) post-flip, so a Bruno regression has an immediate fall-back diagnostic path.
5. As a QA engineer, I want every `.bru` in the collection to declare its own assertions, so opening a single request file tells me what "pass" means without reading a separate harness.
6. As a QA engineer, I want the same `.bru` files I run interactively to be the ones CI runs, so a green CI run is evidence the manual flow still works.
7. As a developer, I want CI to fail at the first request whose response shape regresses, with the body/expected/actual visible in the GitHub Actions step summary, so I can diagnose without re-running locally.
8. As a developer, I want the saga polling loops to live inside the workflow yaml, so the entire CI behavior is readable in one file and not split between yml and pwsh helpers.
9. As a developer, I never want to install Bruno-CLI globally on a CI runner; an `npx --yes @usebruno/cli@<pinned>` invocation is enough.
10. As a developer, I want the Bruno-CLI version pinned in the workflow, so an upstream Bruno release cannot silently change CI behavior.
11. As a developer running locally, I want `bru run qa/bruno/01-happy-path --env qa-local` to work the same way it does in CI, so I can reproduce a CI failure on my laptop without building a PowerShell shim.
12. As a developer touching the saga, I want the full admin admin/payment/shipping operations exercised on every PR, so a regression in `POST /shipping/{id}/dispatch` carrier-key validation is caught before merge.
13. As a developer touching `qa-local.bru`, I want the runbook (or PR template) to remind me to mirror the change to `scripts/local-smoke-test.ps1`'s `$Qa` hash during the dual-write soak, so the two harnesses stay in lockstep.
14. As a developer adding a new `.bru` request, I want the `tests` block convention documented in the QA runbook, so I always declare status + critical fields + a schema check at minimum.
15. As a developer hitting a saga timeout in CI, I want the JSON-output dump from the failed `bru run` printed alongside `docker compose logs`, so I see both the response payload and the surrounding stack state in one place.
16. As a developer, I want the workflow's `Wait for /health/ready` step unchanged (Auth-first → resource services → gateway-last), so the readiness contract that protects JWKS lookup is preserved verbatim.
17. As a developer, I want the workflow's `Tear down` step to keep using `docker compose down -v`, so each Bruno-CLI run starts from a clean SQL/Redis/RabbitMQ state.
18. As a developer, I want polling loops inside the workflow to use the same deadline+poll pattern the PowerShell harness uses (deadline-driven, ~750ms cadence), so the CI step's wall-clock budget is predictable and matches today's tuned `Outbox__PublishIntervalInSeconds=2`.
19. As a developer, I want Bruno's per-request `tests` block to include a JSON-schema check on response bodies that the saga consumes downstream (login `token`, order `Location` header → orderId, shipment `shipmentId`), so a contract drift fails at the failing request, not three steps later.
20. As a developer, I never want to add a `qa/bruno/package.json` or a node module checkout; `npx --yes` is the entire Node footprint of this PRD.
21. As a release manager, I want one PR for the parallel-soak landing and a separate, smaller PR for the cutover (flip required-status + demote PowerShell to `workflow_dispatch`), so the soak period is recorded as a real range of `main` commits.
22. As a developer, I want the workflow's existing failure-log dump (`docker compose ps` + last 500 lines of `docker compose logs`) to remain in the Bruno job, so post-mortem material is identical between the two harnesses.
23. As a QA engineer running the full admin folder via Bruno-CLI, I want the runner to stop the scenario at the first failed `tests` assertion, so subsequent steps that depend on the failed call's state do not produce noisy cascades.
24. As a QA engineer adding a new scenario folder under `qa/bruno/`, I want the workflow to pick it up by glob, so I do not need to edit the workflow yaml to add a scenario. (Stretch — see Out of Scope.)
25. As a developer, I want the PowerShell harness to remain hard-coded with the same persona/product IDs as `qa-local.bru` until the cutover PR lands, so during the soak both harnesses tell the same story.

## Implementation Decisions

### Modules touched

- **`qa/bruno/**/*.bru`** — every existing request file gains a `tests` block (status + critical fields + JSON-schema-style body assertions). No structural reorganization, no new folders, no env-file extraction. New `.bru` files are added only where the saga requires a poll-step that the collection does not already have (e.g. shipment-by-id polling, if not present in `04-admin-ops/shipping/`). Existing `script:post-response` blocks are left in place — they pass variables forward and are orthogonal to assertions.
- **`.github/workflows/smoke-test.yml`** — a second job `bruno-smoke` is added alongside the existing `smoke` job. Same `runs-on`, same `Checkout`, same `Set up .NET`, same `Pack shared library`, same `Boot stack`, same `Wait for /health/ready` step verbatim. The `Run smoke scenarios` step is the only divergence: pwsh shell, inlined polling loops, `npx --yes @usebruno/cli@<pinned>` calls. `Tear down` is identical. The job lands with `continue-on-error: true`; cutover removes the flag.
- **`scripts/local-smoke-test.ps1`** — unchanged during soak. Cutover PR neither deletes nor edits this file.
- **`docs/qa/` runbook** — a "drift-check" paragraph documenting the expectation that `qa-local.bru` vars and `scripts/local-smoke-test.ps1`'s `$Qa` hash stay in lockstep until cutover. No tooling enforces it; the convention is human-driven at PR review.

### `.bru` `tests` block convention

Every request file declares assertions in three layers, in this order:

1. **Status code** — `expect(res.status).to.equal(<expected>)`. Required.
2. **Critical fields** — `expect(res.body.token).to.exist`, `expect(res.headers.location).to.exist`, `expect(res.body.shipmentId).to.exist`, `expect(res.body.status).to.equal('Confirmed')`. Required for any field a downstream request reads. Optional for purely-informational responses.
3. **JSON-schema check** — declarative shape assertion on the response body (field names + types). Required for responses the saga consumes (`/login`, `POST /order/*`, `GET /shipping/by-order/*`, `GET /order/*/*`, `POST /inventory/*/restock`). Optional for opaque admin operations.

The schema layer uses Bruno's `tests` Chai-like syntax (no external schema validator dependency); a missing field fails the assertion with the body printed.

### Bruno-CLI invocation

```
npx --yes @usebruno/cli@<pinned-version> run <path> \
    --env qa-local \
    --output json
```

- `<path>` is a folder for whole-scenario runs (`qa/bruno/01-happy-path`) or a single `.bru` for a poll step.
- `--env qa-local` resolves variables from `qa/bruno/qa-local.bru` (already the collection's env file shape — kept verbatim, no migration to `environments/`).
- `--output json` emits a JSON document the wrapper consumes for the custom summary.
- The pinned version is a single string in the workflow yaml; bumping is a one-line PR.

### Saga polling — inlined in workflow

Each saga wait point in the `Run Bruno scenarios` step is a copy-pasted pwsh block of this shape:

```pwsh
$deadline = (Get-Date).AddSeconds(60)
$matched = $false
do {
    $raw = npx --yes @usebruno/cli@<pinned> run qa/bruno/01-happy-path/07-poll-order.bru `
        --env qa-local --output json
    $result = $raw | ConvertFrom-Json
    if ($result.requests[0].response.body.status -eq 'Confirmed') { $matched = $true; break }
    Start-Sleep -Milliseconds 750
} while ((Get-Date) -lt $deadline)
if (-not $matched) {
    "::error::order did not reach Confirmed within 60s" | Out-File $env:GITHUB_STEP_SUMMARY -Append
    exit 1
}
```

The block is repeated per wait point (Confirmed, ShipmentForOrder, Picked, Packed, Shipped, Delivered, plus the cancellation polls in stock-out and decline scenarios). The repetition is intentional per the chosen wrapper shape; no helper extraction.

### CI scenario boundary

Single job, sequential scenarios. The `Run Bruno scenarios` step runs:

1. `bru run qa/bruno/01-happy-path/01-login-customer.bru` through `06-place-order.bru` in sequence (Bruno preserves the request seq order within a folder).
2. Inlined pwsh poll for order Confirmed.
3. `bru run qa/bruno/01-happy-path/02-login-admin.bru` (admin token).
4. Inlined pwsh poll for shipment-by-order present.
5. `bru run` for `09-pick-shipment.bru` and inlined pwsh poll for `Picked`.
6. ...repeated for pack/dispatch/deliver.
7. Whole-folder runs for `02-stock-shortage`, `03-payment-decline`, `04-admin-ops/inventory`, `04-admin-ops/payment`, `04-admin-ops/shipping`.

A scenario failure stops the step. The `Dump container logs on failure` step continues to run unchanged.

### Soak and cutover

- **Landing PR** — adds the `bruno-smoke` job to `.github/workflows/smoke-test.yml` with `continue-on-error: true`. Adds `tests` blocks to every `.bru` file. Adds the runbook drift-check paragraph. Bumps neither shared-library version nor service code.
- **Soak window** — ten consecutive green `bruno-smoke` runs on `main` (post-merge runs only; PR-run results do not count toward the soak counter). The release manager tracks this manually against the GitHub Actions history; no automation.
- **Cutover PR** — removes `continue-on-error: true` from `bruno-smoke`. Edits the existing `smoke` job (PowerShell) so its `on:` set is `workflow_dispatch` only and removes its `pull_request`/`push` triggers. Branch protection on `main` is updated to require `bruno-smoke` and drop the requirement for the PowerShell `smoke` job. PowerShell harness file (`scripts/local-smoke-test.ps1`) is **not** deleted.

### What is intentionally not changed

- No promotion of `qa-local.bru` to a Bruno `environments/local.bru` file. The collection structure today already works with `bru run --env qa-local` against a top-level `qa-local.bru`; restructuring is out of scope.
- No `qa/bruno/package.json`, no `npm ci`, no Node lockfile in the repo. `npx --yes` is the only Node touch.
- No JUnit / HTML / dashboards. JSON output → custom one-line summary is the only reporter.
- No new harness scenarios beyond what already exists in the Bruno collection. The PRD is a runner swap, not a coverage expansion (other than the already-authored full admin folder coming online in CI).
- No deletion of the PowerShell harness, even after cutover. It remains for ad-hoc local debugging and as the manual-trigger CI escape hatch.
- No change to `Wait for /health/ready`, `Boot stack`, `Pack shared library`, `Tear down`, or service start-up ordering.
- No change to `docker-compose.yaml`, `ECommerce.Shared`, or any service code.
- No tooling that automatically detects drift between `qa-local.bru` and `$Qa`. Drift-audit is a runbook expectation.

### API contracts

No changes. The `.bru` `tests` blocks codify the contract `scripts/local-smoke-test.ps1` already exercises today — they describe existing API behavior, they do not change it.

### Schema

No migrations.

## Testing Decisions

A good test here exercises the **CI-observable behavior**: a clean stack accepts a customer login, walks the saga to a `Confirmed` order, drives the shipping happy path, and observes `Cancelled` final states for the two failure scenarios — same shape as today's PowerShell harness — plus the full admin-ops surface that today's harness does not exercise.

We deliberately do not unit-test the polling loops or the Bruno-CLI wrapper. Both are CI plumbing exercised end-to-end on every workflow run; an isolated test would couple to internals (JSON output shape of `bru run`, GitHub Actions environment variables) that change with upstream releases.

### Modules to verify

- **`qa/bruno/**/*.bru`** — each `.bru` is itself the assertion. Running the collection via `bru run` with no further wrapper is the unit-of-test for that file. Local verification: `npx --yes @usebruno/cli@<pinned> run qa/bruno/<scenario> --env qa-local`.
- **`.github/workflows/smoke-test.yml`** — the `bruno-smoke` job is the integration surface. Green = saga + admin operations work on a clean stack. Red = saga is broken, an admin endpoint regressed, or a `tests` block disagrees with the API.
- **`scripts/local-smoke-test.ps1`** — unchanged during soak; the existing `smoke` job continues to assert what it asserts today. Post-cutover, the script remains in-repo but is not a regression gate.

### Prior art

- `scripts/local-smoke-test.ps1` is the prior-art harness (delivered in [PRD-Qa-Dump-Dataset](./PRD-Qa-Dump-Dataset.md), hardened in [PRD-Smoke-Test-Saga-Hardening](./PRD-Smoke-Test-Saga-Hardening.md)). Pattern: scenario-driven, only HTTP assertions, no SQL or RabbitMQ introspection. This PRD preserves that pattern, swapping only the runner.
- `.github/workflows/smoke-test.yml` follows the existing per-service `azure-pipelines.yml` skeleton (pack shared lib → boot stack → wait for `/health/ready` → run scenarios → tear down with `down -v`). The `bruno-smoke` job is added without disturbing this skeleton.
- `qa/bruno/qa-local.bru` is the variables source the new harness reads. It already mirrors the PowerShell `$Qa` hash by construction.

### What we are not testing

- Whether `bru run` itself behaves correctly. Pinning the CLI version is the mitigation; bumping it is a one-line PR.
- Whether two different harnesses agree on every detail. The soak is the de facto agreement test; if Bruno is green for ten runs while PowerShell is also green, they agree enough.
- Schema-level fuzz of API responses. The `tests` blocks check shape, not invariance under arbitrary inputs.

## Out of Scope

- Replacing the PowerShell harness file. It stays. Even post-cutover the `scripts/local-smoke-test.ps1` script is preserved for local ad-hoc debugging and as the `workflow_dispatch` escape-hatch CI job.
- Promoting `qa-local.bru` into a Bruno `environments/local.bru` file. Tracked separately if Bruno's env model becomes load-bearing.
- Extracting a `scripts/Invoke-BrunoPoll.ps1` helper for the saga polling loops. The chosen wrapper shape is "strictly inline in workflow yml"; a helper would change that contract.
- Automated drift-detection between `qa-local.bru` and `scripts/local-smoke-test.ps1`'s `$Qa` hash. Audit is human at PR-review time during the soak; post-cutover the duplication remains but is no longer load-bearing because the PowerShell job is no longer a merge gate.
- Adding new QA scenarios. This PRD swaps runners, it does not widen the dataset. The widening that does happen — the full `04-admin-ops` tree coming online in CI — is purely a side-effect of running the existing collection.
- A JUnit reporter, GitHub-test-reporter integration, or any HTML report artifact. Plain JSON → custom summary is the reporter contract.
- Auto-discovery of new scenario folders by glob. The workflow names scenarios explicitly. (User story #24 is acknowledged but explicitly deferred.)
- Migrating off `npx --yes` to a `package.json`-based install with a lockfile. Considered and rejected to keep the Node footprint zero-files-in-repo.
- Restructuring `qa/bruno/` (folder layout, naming convention). Existing structure is the structure CI consumes.
- Replacing RabbitMQ subscribers, the outbox cadence, the auth/JWKS plumbing, or any production-path code. The fixes from `PRD-Smoke-Test-Saga-Hardening` are assumed in place; this PRD adds a parallel CI runner against the same already-tuned stack.

## Further Notes

- **Why two PRs, not one** — the soak window is the entire point of the cutover gate. A single PR that lands Bruno *and* flips the required-status removes the soak observability. Two PRs make the soak window a measurable range of `main` commits.
- **Why ten green runs** — empirical guess at the threshold where flake noise drops below signal. If a single Bruno run is ~95% reliable, ten green in a row is ~60% probability without intervention; observed-in-real-PRs gives confidence the reliability is materially above 95% before flipping. If the soak surfaces flake faster (e.g. one red in five runs), the cutover PR waits.
- **Why keep the PowerShell harness post-flip** — three reasons. (1) It is the diagnostic of last resort if Bruno-CLI itself breaks; rolling back to PowerShell-as-required is a one-PR revert. (2) It is the local script teammates have muscle memory for; deleting it forces an unrelated retraining cost. (3) Demoting it to `workflow_dispatch` keeps a button to re-run it on demand from any commit, which is enough for post-mortem use.
- **Why no helper extraction for polling** — explicit user choice. The trade-off is workflow-yaml verbosity (six-plus copy-pasted pwsh blocks per happy run) against single-file readability of the entire CI behavior. The latter won. If verbosity becomes a maintenance pain, a helper script PR is the natural follow-up.
- **Why no env-file promotion for `qa-local.bru`** — the existing `qa-local.bru` is already what `bru run --env qa-local` resolves; renaming it under `environments/` is churn without a behavior change.
- **Drift-audit during the soak** — every PR that touches either `qa/bruno/qa-local.bru` or the `$Qa` hash in `scripts/local-smoke-test.ps1` is expected (per runbook) to update both. Reviewers are the enforcement layer. Post-cutover the duplication persists but stops being load-bearing for CI; the runbook note can be relaxed to "best-effort sync."
- **Linked artifacts**:
  - PowerShell harness this swap targets: `scripts/local-smoke-test.ps1`.
  - Bruno collection this swap exposes to CI: `qa/bruno/`.
  - Workflow file: `.github/workflows/smoke-test.yml`.
  - Variable source: `qa/bruno/qa-local.bru`.
  - Predecessor PRDs: [PRD-Qa-Dump-Dataset](./PRD-Qa-Dump-Dataset.md), [PRD-Smoke-Test-Saga-Hardening](./PRD-Smoke-Test-Saga-Hardening.md).
