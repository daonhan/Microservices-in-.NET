# Plan: Replace Smoke Harness with Bruno-CLI Run

> Source PRD: [PRD-Smoke-Test-Bruno-Cli.md](../prd/PRD-Smoke-Test-Bruno-Cli.md)

## Architectural decisions

Durable decisions that apply across all phases:

- **Bruno-CLI Invocation**: `npx --yes @usebruno/cli@<pinned-version> run <path> --env qa-local --output json` will be used per request. No global install or `package.json` checked into `qa/bruno/`.
- **Assertion Convention**: Every `.bru` file will have a `tests` block containing: (1) Status code check, (2) Critical fields check, and (3) JSON-schema shape check.
- **Saga Waits**: Polling loops will be inlined in the workflow yaml as `pwsh` loops executing `bru run`. No standalone helper scripts.
- **Reporting**: JSON output will be parsed by the workflow wrapper to print a custom one-line pass/fail per request to `$env:GITHUB_STEP_SUMMARY`.
- **Cutover Strategy**: Dual-run soak period (with `continue-on-error: true` on the new job) requiring 10 consecutive green runs before demoting the PowerShell harness.

---

## Phase 1: CI Skeleton & Happy Path

**User stories**: 1, 2, 5, 8, 9, 10, 11, 13, 14, 15, 16, 17, 18, 19, 20

### What to build

Establish the parallel CI job (`bruno-smoke`) and instrument the happy path scenarios with assertions. We will add `tests` blocks to all `.bru` files under `qa/bruno/01-happy-path`. We will also add a drift-check reminder to the runbook. The workflow job runs with `continue-on-error: true` to avoid blocking merges during the initial integration. 

### Acceptance criteria

- [ ] `.github/workflows/smoke-test.yml` has a new `bruno-smoke` job running in parallel with the `smoke` job.
- [ ] `bruno-smoke` uses `continue-on-error: true`.
- [ ] All `.bru` files in `qa/bruno/01-happy-path` have `tests` blocks asserting status code, critical fields, and body schemas.
- [ ] `bruno-smoke` executes `01-happy-path` via `npx --yes @usebruno/cli@<pinned-version> run ...` and uses inlined `pwsh` polling for saga wait steps.
- [ ] Failures and passes are written to `$env:GITHUB_STEP_SUMMARY`.
- [ ] `docs/qa/` runbook is updated with a drift-check note to keep `qa-local.bru` variables and `scripts/local-smoke-test.ps1` `$Qa` hash in sync.

---

## Phase 2: Failure Scenarios (Stock & Payment)

**User stories**: 1, 6, 7, 19

### What to build

Instrument the stock shortage and payment decline scenarios with assertions and integrate them into the `bruno-smoke` job.

### Acceptance criteria

- [ ] All `.bru` files in `qa/bruno/02-stock-shortage` have `tests` blocks asserting status code, critical fields, and body schemas.
- [ ] All `.bru` files in `qa/bruno/03-payment-decline` have `tests` blocks asserting status code, critical fields, and body schemas.
- [ ] The `bruno-smoke` workflow job is updated to execute these two folders (along with necessary polling wait steps) after the happy path.

---

## Phase 3: Full Admin Operations

**User stories**: 1, 12, 23

### What to build

Expand coverage by instrumenting the full suite of admin operations. Add assertions to the `04-admin-ops` folder and run it within the workflow.

### Acceptance criteria

- [ ] All `.bru` files in `qa/bruno/04-admin-ops/` have appropriate `tests` blocks.
- [ ] The `bruno-smoke` workflow job is updated to execute `04-admin-ops` requests.
- [ ] A failing assertion correctly halts the scenario execution.

---

## Phase 4: Cutover (Soak Window Complete)

**User stories**: 3, 4, 21, 25

### What to build

After the release manager observes 10 consecutive green runs of `bruno-smoke` on `main`, flip the switch. The new Bruno harness becomes the required merge gate, and the legacy PowerShell harness is demoted to a manual debugging tool.

### Acceptance criteria

- [ ] Remove `continue-on-error: true` from the `bruno-smoke` job in `.github/workflows/smoke-test.yml`.
- [ ] Demote the legacy `smoke` (PowerShell) job to only run `on: workflow_dispatch` (remove from push/pull_request).
- [ ] The PowerShell harness file (`scripts/local-smoke-test.ps1`) remains in the repository intact.
