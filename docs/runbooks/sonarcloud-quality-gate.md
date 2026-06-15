# SonarCloud quality gate — setup & operation

This runbook covers the SonarCloud quality gate for the monorepo: the GitHub
Actions PR analysis ([`.github/workflows/sonarcloud.yml`](../../.github/workflows/sonarcloud.yml))
delivered in Phase A2, and the one-time SonarCloud setup it depends on. Source
plan: [docs/plans/cicd-quality-gate-and-iac-automation.md](../plans/cicd-quality-gate-and-iac-automation.md)
(Phases A1–A4).

## Model

- **One SonarCloud project per service** — ten keys: the eight services +
  api-gateway + shared-libs.
- **Project key convention** — `<org>_<slug>`, where `<org>` is the
  `SONAR_ORGANIZATION` Actions variable and `<slug>` is the per-service slug
  baked into the workflow's `detect` catalog. The workflow composes the key as
  `${SONAR_ORG}_${{ matrix.service.slug }}`, so the projects a human creates
  **must** use exactly these keys:

  | Slug          | Service directory         | Solution analysed                          |
  |---------------|---------------------------|--------------------------------------------|
  | `basket`      | `basket-microservice`     | `Basket.Service.slnx`                      |
  | `order`       | `order-microservice`      | `Order.Service.slnx`                       |
  | `product`     | `product-microservice`    | `Product.Service.slnx`                     |
  | `auth`        | `auth-microservice`       | `Auth.Service.slnx`                        |
  | `inventory`   | `inventory-microservice`  | `Inventory.Service.slnx`                   |
  | `shipping`    | `shipping-microservice`   | `Shipping.Service.slnx`                    |
  | `payment`     | `payment-microservice`    | `Payment.Service.slnx`                     |
  | `saga`        | `saga-microservice`       | `Saga.Service.slnx`                        |
  | `api-gateway` | `api-gateway`             | `ApiGateway.slnx`                          |
  | `shared-libs` | `shared-libs`             | `ECommerce.Shared.slnx`                    |

- **Quality gate** — built-in **"Sonar way" / new-code** (≥80% new-code
  coverage, ≤3% duplication, 0 new bugs/vulnerabilities, new hotspots reviewed,
  maintainability A). New-code reference branch = `main`.
- **Coverage** — coverlet emits opencover; the .NET scanner reads it via
  `sonar.cs.opencover.reportsPaths`. Migrations, `obj/`/`bin/`, and test
  projects are excluded from analysis (coverage + duplication).

## One-time HITL setup

The workflow is a deliberate **no-op (green)** until all three of these exist —
the `analyze` job is gated on `vars.SONAR_ORGANIZATION != ''`, so merging the
workflow before setup never turns PRs red.

1. **SonarCloud org + projects** — create the organization (bind to the GitHub
   org/repo), then create the ten projects above with the "Sonar way" gate and
   `main` as the new-code reference branch.
2. **Install the SonarCloud GitHub App** on the repo — this is what decorates
   PRs with the gate result.
3. **Secrets/variables** — add repo secret `SONAR_TOKEN` (a SonarCloud token)
   and repo variable `SONAR_ORGANIZATION` (the org key).

## How the workflow behaves

- **Path-filtered fan-out** — the `detect` job diffs the PR against `main` and
  emits a matrix of only the touched services. A PR touching one service runs
  only that service's analysis; services it never touched produce no check leg
  (so nothing sits pending).
- **Aggregator** — the `sonar-gate` job always runs and reports a single
  consolidated status: PASS when every analysis that ran passed (or none ran),
  FAIL when any failed. This is the check Phase A3 (#346) makes **required** in
  branch protection — making one aggregator required (instead of ten
  path-filtered per-service checks) is what prevents a never-reported required
  check from deadlocking the merge.
- **Report-only** — until A3, `sonar-gate` is visible but not required; failures
  do not block merge.

## Verifying (post-setup)

Behavioural checks, per the plan's verification approach:

- Open a PR adding clearly uncovered new code to one service → its SonarCloud
  check fails and decorates the PR; `sonar-gate` reflects the failure.
- Open a PR meeting the gate → passes and decorates.
- A single-service PR shows only that service's analysis leg, and `sonar-gate`
  is not held pending by other services.
- A non-zero new-code coverage number appears, proving the opencover import.

## Related

- A4 adds long-lived **branch** analyses in Azure `build-stage.yml` (same
  project keys) plus `main` push analysis — see the plan.
- B-workstream (Bicep IaC) is independent; see
  [docs/plans/cicd-quality-gate-and-iac-automation.md](../plans/cicd-quality-gate-and-iac-automation.md).
