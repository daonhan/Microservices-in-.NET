# Plan: SonarCloud Quality Gate + Bicep IaC Pipeline Automation

> Source PRD: [docs/prd/PRD-CICD-Quality-Gate-And-IaC-Automation.md](../prd/PRD-CICD-Quality-Gate-And-IaC-Automation.md)

Two independent workstreams (A = SonarCloud quality gate, B = Bicep IaC pipeline). They share no code and can be implemented in either order or in parallel. Within each workstream the phases are ordered tracer bullets — each is a thin vertical slice, verifiable on its own.

## Architectural decisions

Durable decisions that apply across all phases:

### Workstream A — SonarCloud

- **Platform**: SonarCloud (SaaS), repo **public** — free, native PR decoration + branch analysis. Community Edition rejected (no PR/branch analysis).
- **Project granularity**: one SonarCloud project per service — ~10 keys (8 services + api-gateway + shared-libs). Same key reused across both CIs so PR and branch analyses roll up into one history.
- **Quality gate**: built-in **"Sonar way" / new-code** — on changed code only: ≥80% coverage, ≤3% duplication, 0 new bugs, 0 new vulnerabilities, new security hotspots reviewed, maintainability A.
- **New-code baseline**: reference branch = `main`.
- **Branch model**: GitHub Actions = short-lived **PR analyses** (the merge gate); Azure `build-stage.yml` = long-lived **branch analyses** on dev/staging/prod; `main` long-lived branch analyzed on **push-to-main**.
- **Coverage format**: coverlet emits **both** `cobertura,opencover`. Azure coverage tab reads cobertura; SonarCloud reads `sonar.cs.opencover.reportsPaths`. (Cobertura is not imported for C# by the .NET scanner.)
- **Exclusions**: `**/Migrations/*`, generated code, and test projects excluded from coverage + duplication.
- **Required-check pattern**: a single **aggregator check** reports the required status. Per-service path-filtered runs must never leave a required per-service check "pending" and deadlock the merge.
- **Tooling**: `dotnet-sonarscanner` global tool (GitHub); `SonarCloudPrepare`/`SonarCloudAnalyze`/`SonarCloudPublish` tasks (Azure).
- **Secret**: `SONAR_TOKEN` — GitHub Actions secret + Azure DevOps variable group.

### Workstream B — Bicep IaC

- **Tool**: keep Bicep (Bicep *is* ARM). No Terraform migration.
- **CI**: Azure Pipelines — reuses ARM service connections, branch-gated env model, native Environment approval gates.
- **Pipeline input**: the existing per-environment `.bicepparam` files — the automated path applies exactly what the manual path applies.
- **Gating model**: PR → `what-if` only (reported to the GitHub PR, never applies); merge `dev` → auto-apply; merge `staging`/`prod` → apply behind an Azure DevOps **Environment** manual-approval gate.
- **Identity**: dedicated **per-environment** ARM service connection, **Contributor + User Access Administrator** scoped to the RG (UAA is required because `acr-pull-role.bicep` creates a role assignment), via **workload identity federation** (passwordless). Separate from the app-deploy connections so app pipelines never hold RBAC-admin.
- **Secure params**: `sqlAdminPassword` (and any secure params) from an Azure DevOps variable group linked to a bootstrap Key Vault, passed to the apply; never in `.bicepparam` or logs.
- **Drift**: nightly scheduled `what-if` per env → alert on non-empty diff. (Deployment Stacks + `denySettings` is a future option, out of scope here.)

---

## Phase A1: Sonar pilot — one service, GitHub PR analysis

**User stories**: 1, 3, 10, 11, 14

### What to build

The full vertical path for a **single pilot service**, proving the parts most likely to break before any fan-out. A PR-triggered GitHub Actions job restores, runs `dotnet test` with coverage emitted in opencover, runs the `dotnet-sonarscanner` begin → build → test → end cycle against the service's SonarCloud project (with the Migrations/generated/test exclusions configured), and submits a PR analysis. The SonarCloud GitHub App decorates the PR with the result. The quality gate is configured but **report-only** (not yet required).

### Acceptance criteria

- [ ] A SonarCloud organization exists and the pilot service has a project bound to the GitHub repo with the "Sonar way" / new-code gate.
- [ ] `SONAR_TOKEN` is stored as a GitHub Actions secret.
- [ ] Opening a PR that touches the pilot service triggers analysis and the PR is decorated by the SonarCloud GitHub App.
- [ ] The analysis reports a **non-zero new-code coverage** number on a PR that adds covered code — proving opencover import works.
- [ ] Migrations, generated code, and test projects are absent from the coverage/duplication figures.
- [ ] The gate result is visible on the PR but does **not** block merge yet.

---

## Phase A2: Fan out to all services + aggregator check

**User stories**: 7, 9, 16

### What to build

Generalize A1 across all ~10 services. The PR job runs analysis per changed service (matrix + path filters), each against its own SonarCloud project (same key reused). Add a single **aggregator job** that depends on the per-service analyses and reports one consolidated status check. Still **report-only** — the aggregator check exists but is not yet required.

### Acceptance criteria

- [ ] All ~10 services (8 services + api-gateway + shared-libs) have SonarCloud projects with the new-code gate.
- [ ] A PR touching one service runs only that service's analysis (path filters work); a PR touching several runs all affected.
- [ ] The aggregator job reports exactly one status check that reflects the combined gate result of the analyses that ran.
- [ ] A PR touching only one service does **not** sit pending on checks for services it never ran.
- [ ] Each service's PR and (later) branch analyses use the same project key.

---

## Phase A3: Make the gate required (merge-blocking go-live)

**User stories**: 2, 4, 5, 6, 8

### What to build

The deliberate go-live: mark the aggregator check as a **required status check** in GitHub branch protection on `main`. The new-code "Sonar way" gate now blocks merges. Because the gate judges only changed code, this is safe to enable without a brownfield remediation project.

### Acceptance criteria

- [ ] The aggregator check is required in branch protection on `main`.
- [ ] A PR that adds clearly uncovered new code **fails the gate and is blocked from merging**.
- [ ] A PR that introduces a new bug or unreviewed security hotspot is blocked.
- [ ] A PR that meets the gate (or touches no production code) passes and is mergeable.
- [ ] A PR touching only legacy code it did not change is **not** blocked by pre-existing debt.

---

## Phase A4: Azure branch analysis + main baseline

**User stories**: 12, 13, 15, 17

### What to build

Add long-lived branch analysis in Azure `build-stage.yml` via the `SonarCloud*` tasks, wrapped around the existing build+test, producing branch analyses on dev/staging/prod (same project keys). Switch the test coverage collection to emit **both** cobertura and opencover so the existing Azure coverage tab is preserved while Sonar reads opencover. Ensure `main` is analyzed on push so the new-code baseline has an owner.

### Acceptance criteria

- [ ] Azure `build-stage.yml` runs Sonar analysis on dev/staging/prod branch builds against each service's project.
- [ ] The existing Azure coverage tab still renders (cobertura unchanged); existing `dotnet test`/coverage publish stays green.
- [ ] `main` is analyzed on push-to-main and is set as the new-code reference branch.
- [ ] Each service's SonarCloud project shows both PR analyses (from GitHub) and branch trend (from Azure) under one history.
- [ ] `SONAR_TOKEN` is available to Azure via a variable group.

---

## Phase B1: IaC pipeline — dev end-to-end

**User stories**: 18, 19, 20, 21, 23, 24, 25, 28, 29

### What to build

The full IaC vertical for the **dev** environment. Provision a dedicated dev ARM service connection (Contributor + User Access Administrator, workload identity federation) and a dev variable group linked to a bootstrap Key Vault supplying `sqlAdminPassword`. Build the Azure Pipeline: a `pr:` trigger runs `az deployment group what-if` against dev (using the existing `dev.bicepparam`) and reports the diff to the GitHub PR; a merge to `dev` runs `az deployment group create` and applies. A PR never applies anything.

### Acceptance criteria

- [ ] A PR changing Bicep shows a rendered `what-if` diff reported on the GitHub PR and applies nothing.
- [ ] Merging an infra change to `dev` applies it to the dev resource group automatically.
- [ ] The apply runs under the dedicated dev connection (Contributor + UAA) via workload identity federation — no stored secret.
- [ ] The `acr-pull-role.bicep` role assignment applies successfully (UAA rights present).
- [ ] `sqlAdminPassword` is supplied from the KV-linked variable group; it appears in neither the repo nor the pipeline logs.
- [ ] The apply uses the existing `dev.bicepparam` unchanged.

---

## Phase B2: Staging + prod with approval gates

**User stories**: 22, 26

### What to build

Extend B1 to staging and prod. Provision per-env service connections (each Contributor + UAA, federated) and per-env variable groups. Create Azure DevOps **Environments** for staging and prod with manual-approval checks, and bind the apply stage to them. Extend the pipeline branch triggers so merge to `staging`/`prod` runs the apply behind the approval gate; the approver reads the `what-if` before approving.

### Acceptance criteria

- [ ] Merging to `staging` (and `prod`) halts at a manual-approval gate before applying.
- [ ] After approval, the change applies to the corresponding resource group using that env's dedicated connection.
- [ ] A dev-branch run cannot reach staging/prod resources (per-env identity isolation).
- [ ] The app-deploy pipelines still hold no RBAC-admin rights (infra UAA lives only on the infra connections).
- [ ] Each env applies its own `.bicepparam` and sources its own KV-linked `sqlAdminPassword`.

---

## Phase B3: Nightly drift detection

**User stories**: 27

### What to build

A scheduled (cron) pipeline run that executes `az deployment group what-if` per environment and fails / alerts when the diff is non-empty, surfacing out-of-band portal changes.

### Acceptance criteria

- [ ] A scheduled run executes `what-if` for each environment on a nightly cadence.
- [ ] The run reports green (success) when an environment matches its templates.
- [ ] The run reports red / alerts when a deliberate out-of-band change is introduced, and returns to green once reverted.
- [ ] The drift check never applies changes — it is read-only.
