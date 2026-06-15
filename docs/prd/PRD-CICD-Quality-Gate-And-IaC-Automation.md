# PRD: SonarCloud Quality Gate + Bicep IaC Pipeline Automation

## Problem Statement

The monorepo already has a mature CI/CD surface, but two named competencies are only half-present, and one is missing entirely.

**Automated code quality has no central gate.** Today the pipelines enforce formatting (`dotnet format`), warnings-as-errors, architecture boundaries (NetArchTest + a Roslyn `LayoutAnalyzer`), and they *collect* test coverage (cobertura) in the Azure `build-stage.yml` — but coverage is published and then ignored. There is **no coverage threshold**, no static-analysis quality gate, no per-PR signal on whether a change introduced bugs, vulnerabilities, security hotspots, duplication, or untested code. GitHub Actions, where PRs actually merge (`main`), runs **only** Docker image builds + a smoke test — it does not even run the unit tests. So a developer can merge a PR that drops coverage or adds a code smell and nothing objects.

**Infrastructure-as-Code is written but not automated.** The platform is fully described in Bicep (VNet, AKS, ACR, SQL, Redis, Key Vault, Log Analytics, App Insights, Service Bus, budgets, alerts) with per-environment `.bicepparam` files and a cost-profile switch. But nothing deploys it through a pipeline. The documented procedure is a human running `az deployment group create` from a workstation (`infrastructure-deployment/bicep/README.md`). There is **no `what-if` preview on a PR**, **no gated apply on merge**, and **no drift detection** — so an infra change is reviewed as raw Bicep with no rendered diff, applied by hand with whatever rights the operator happens to hold, and any out-of-band portal edit silently diverges from the templates until the next manual apply.

The third item from the originating brief — "Terraform or ARM templates" — is, on inspection, **already satisfied**: Bicep is Microsoft's DSL over ARM and transpiles to ARM JSON. The repo is Azure-only, so Terraform's multi-cloud advantage does not apply, and a Bicep→Terraform migration would be a full rewrite that discards the existing param/cost/module work and adds Terraform state to operate — for no Azure capability gain. That migration is explicitly rejected (see Out of Scope).

## Solution

Two independent, additive, surgical workstreams.

**Workstream A — SonarCloud quality gate.** Adopt SonarCloud (free on a public repo, with native PR decoration) with **one project per service** (~10 keys: the eight services + api-gateway + shared-libs). Analysis runs in **both** CIs, using SonarCloud's branch model so they never collide: GitHub Actions runs short-lived **PR analyses** (the merge gate, decorated by the SonarCloud GitHub App), and Azure `build-stage.yml` runs long-lived **branch analyses** on dev/staging/prod where coverage already exists; `main`'s long-lived branch is analyzed on push-to-main so the new-code baseline has an owner. The gate is the built-in **"Sonar way" / new-code** definition (≥80% coverage on new code, ≤3% duplication, zero new bugs/vulnerabilities, all new security hotspots reviewed, maintainability A) — chosen because it judges only *changed* code, so it can be made a **required, merge-blocking** check on `main` immediately without a brownfield remediation project. A single **aggregator check** reports the required status so per-service path-filtered runs don't deadlock the merge on never-reported checks.

**Workstream B — Bicep IaC pipeline.** Keep Bicep; automate it with a new Azure Pipeline. A PR runs **`what-if` only** (preview reported to the GitHub PR). Merge to `dev` auto-applies (small blast radius); merge to `staging`/`prod` applies **behind an Azure DevOps Environment manual-approval gate** where the approver reads the `what-if` first. A **nightly scheduled `what-if`** per environment alerts on drift. The apply runs under a **dedicated per-environment ARM service connection** holding **Contributor + User Access Administrator** (required because `acr-pull-role.bicep` creates a role assignment) via **workload identity federation** (passwordless), kept separate from the app-deploy connections so app pipelines never hold RBAC-admin rights.

## User Stories

### Code quality — developer perspective

1. As a developer, I want every PR to `main` decorated with a SonarCloud analysis, so that I see the quality impact of my change before it merges.
2. As a developer, I want the quality gate to judge only the code I changed, so that I am never blocked by pre-existing legacy debt I did not touch.
3. As a developer, I want new code to require ≥80% test coverage, so that I cannot merge meaningful untested logic.
4. As a developer, I want zero new bugs and vulnerabilities to be a hard condition, so that regressions are caught at review time, not in production.
5. As a developer, I want new security hotspots flagged for review, so that risky patterns are consciously acknowledged before merge.
6. As a developer, I want duplicated new code held under 3%, so that copy-paste growth is caught early.
7. As a developer, I want each service to have its own SonarCloud project and gate, so that a regression in one service is attributed to that service and does not blur into the others.
8. As a developer, I want the gate to be a required, merge-blocking status check, so that a failing gate genuinely stops the merge rather than being advisory.
9. As a developer, I want a PR that touches only one service to not deadlock on quality checks for services it never ran, so that path-filtered PRs can still merge.
10. As a developer, I want generated code (EF migrations, generated files) and test projects excluded from coverage and duplication metrics, so that the numbers reflect real production code.
11. As a developer, I want coverage emitted in a format SonarCloud can actually import for C#, so that my coverage shows up rather than silently reading as zero.
12. As a developer, I want the existing Azure coverage tab to keep working unchanged, so that I do not lose the report I already use.
13. As a developer, I want the new-code baseline to be the `main` branch, so that "new code" means "different from trunk."

### Code quality — operator / reviewer perspective

14. As a reviewer, I want the SonarCloud check visible on the PR, so that I can factor quality into my review without leaving GitHub.
15. As a maintainer, I want long-lived branch analyses on dev/staging/prod, so that I can see each environment branch's quality trend over time, not just per-PR snapshots.
16. As a maintainer, I want the same project key used across both CIs per service, so that PR and branch analyses roll up into one coherent project history.
17. As a maintainer, I want the SonarCloud token stored as a secret in both CIs, so that analysis authenticates without leaking credentials.

### IaC automation — developer perspective

18. As a developer, I want a PR that changes Bicep to show a rendered `what-if` diff, so that I review the actual resource changes, not raw template text.
19. As a developer, I want the `what-if` result reported back to the GitHub PR, so that the infra review happens where code review happens.
20. As a developer, I want a Bicep PR to never apply anything, so that opening a PR is always safe.
21. As a developer, I want merging an infra change to `dev` to apply automatically, so that the lowest environment stays in sync without manual steps.

### IaC automation — operator perspective

22. As an operator, I want staging and prod applies gated behind a manual approval, so that a human reviews the `what-if` before production infrastructure changes.
23. As an operator, I want the apply to run under a dedicated per-environment identity, so that a dev-branch run can never reach prod resources.
24. As an operator, I want that identity to use workload identity federation, so that there is no long-lived secret to rotate or leak.
25. As an operator, I want the infra identity to hold exactly the rights the templates need (Contributor + User Access Administrator for the role assignment) and no more, so that least privilege is preserved.
26. As an operator, I want the app-deploy pipelines to *not* hold RBAC-admin rights, so that the application delivery path cannot alter role assignments.
27. As an operator, I want a nightly `what-if` per environment that alerts on any non-empty diff, so that out-of-band portal changes are surfaced instead of sitting undetected.
28. As an operator, I want the SQL admin password sourced from a Key Vault-linked variable group rather than a plaintext parameter, so that the secret never lives in the repo or pipeline logs.
29. As an operator, I want the apply to use the existing per-environment `.bicepparam` files, so that the automated path applies exactly what the documented manual path applies.

## Implementation Decisions

### Workstream A — SonarCloud

**Modules (deep, isolatable units):**

- **SonarCloud organization + per-service projects** — ~10 project keys (eight services + api-gateway + shared-libs), each bound to the GitHub repo with the "Sonar way" / new-code quality gate. The stable interface is the project key + gate; everything downstream references the key.
- **GitHub Actions PR-analysis job** — a PR-triggered job that runs `dotnet test` with coverage and the `dotnet-sonarscanner` begin/build/test/end cycle for the changed service(s), submitting a PR analysis (`sonar.pullrequest.*`). The SonarCloud GitHub App decorates the PR.
- **GitHub Actions aggregator check** — one job that depends on the analysis matrix and reports a single status check; this is the name made *required* in branch protection, so path-filtered PRs that skip some services do not deadlock on never-reported per-service checks.
- **Azure `build-stage.yml` Sonar steps** — `SonarCloudPrepare` / `SonarCloudAnalyze` / `SonarCloudPublish` tasks wrapped around the existing build+test, producing long-lived branch analyses on dev/staging/prod.

**Technical decisions:**

- **Platform:** SonarCloud, repo public (free, native PR decoration + branch analysis). Self-hosted Community Edition rejected — no PR/branch analysis.
- **Branch model:** GitHub = short-lived PR analyses; Azure = long-lived branch analyses (dev/staging/prod); `main` long-lived branch analyzed on GitHub push-to-main. Same project key per service across both CIs so they roll up, not collide.
- **Gate:** built-in "Sonar way" / new-code — ≥80% new-code coverage, ≤3% new duplication, 0 new bugs, 0 new vulnerabilities, new hotspots reviewed, maintainability A on new code. Required, merge-blocking on `main`.
- **Coverage wiring:** the coverlet collector emits **both** cobertura and opencover (`Format=cobertura,opencover`); the Azure coverage tab keeps reading cobertura, SonarCloud reads `sonar.cs.opencover.reportsPaths`. (Cobertura is not imported for C# by the .NET scanner, which is why opencover is added.)
- **Exclusions:** `**/Migrations/*`, generated code, and test projects excluded from coverage and duplication.
- **New-code definition:** reference branch = `main`.
- **Tooling:** `dotnet-sonarscanner` global tool (GitHub); `SonarCloud*` marketplace tasks (Azure).
- **Secret:** `SONAR_TOKEN` — GitHub Actions secret + Azure DevOps variable group.

### Workstream B — Bicep IaC pipeline

**Modules (deep, isolatable units):**

- **IaC pipeline (Azure DevOps)** — the orchestrator: `pr:` trigger → `what-if` stage; branch triggers (dev/staging/prod) → apply stage. Reuses the per-environment `.bicepparam` files as its single source of input.
- **`what-if` stage** — runs `az deployment group what-if` for the target environment and surfaces the diff (reported to the GitHub PR for PR runs). The only stage a PR ever runs.
- **Apply stage** — runs `az deployment group create`; bound to an Azure DevOps **Environment** so the approval gate (staging/prod) and the per-environment service connection attach to it. `dev` auto-applies; `staging`/`prod` require manual approval.
- **Scheduled drift-check** — a cron-triggered pipeline (or scheduled run of the same definition) that runs `what-if` per environment and fails/alerts on a non-empty diff.

**Technical decisions:**

- **Tool:** keep Bicep (Bicep *is* ARM). No Terraform migration.
- **CI:** Azure Pipelines — reuses ARM service connections, the branch-gated env model, and native Environment approval gates; a second CI for infra was rejected.
- **Identity:** dedicated per-environment ARM service connection, **Contributor + User Access Administrator** scoped to the resource group (UAA needed for the `acr-pull-role.bicep` role assignment), via **workload identity federation** (passwordless). Separate from app-deploy connections.
- **Gating:** PR → `what-if` only; merge `dev` → auto-apply; merge `staging`/`prod` → apply behind Environment manual-approval gate.
- **Drift:** nightly scheduled `what-if` per environment → alert on non-empty diff. (Deployment Stacks with `denySettings` recorded as a future option for drift *prevention*.)
- **Secure params:** `sqlAdminPassword` (and any other secure params) sourced from an Azure DevOps variable group linked to a bootstrap Key Vault, passed to the apply; never in the `.bicepparam` or in logs.

## Testing Decisions

This work produces CI/CD and IaC artifacts (pipeline YAML, scanner config, Bicep automation), not application code, so there are no new unit-testable production modules. Verification is behavioral and gate-driven:

- **Quality gate verification:** prove the gate works by behavior, not by inspecting config — open a throwaway PR that adds clearly uncovered new code and confirm the SonarCloud check **fails and blocks merge**; open a PR that meets the gate and confirm it **passes and decorates** the PR. Confirm a single-service PR is *not* blocked by other services' analyses (aggregator-check behavior). Confirm coverage actually imports (a non-zero new-code coverage number appears), validating the opencover wiring.
- **IaC pipeline verification:** confirm a Bicep PR produces a readable `what-if` and applies nothing; confirm merge to `dev` applies; confirm `staging`/`prod` halt at the approval gate; confirm the scheduled `what-if` reports green on an unchanged environment and red after a deliberate out-of-band portal edit (then reverts).
- **Bicep templates** continue to be validated by `bicep build` / `what-if`, consistent with existing practice (the repo's Bicep is validated this way, not by unit tests).
- **Existing suites must stay green:** the change to `build-stage.yml` (added Sonar steps, dual coverage format) must not break the existing `dotnet test` / coverage publish; the existing GitHub `docker-build.yml` and `smoke-test.yml` are untouched.

## Out of Scope

- **Bicep → Terraform migration.** Rejected: Bicep is already ARM, the platform is Azure-only, and a rewrite discards existing param/cost/module work for no capability gain. (A small, additive Terraform *showcase* — e.g. provisioning SonarCloud projects or GitHub branch protection via their TF providers, coexisting with Bicep — was identified as the cheap honest way to demonstrate the Terraform keyword if ever needed, but is not part of this PRD.)
- **Deployment Stacks (`az stack` + `denySettings`).** Noted as a stronger future option for drift *prevention*; this PRD does drift *detection* via scheduled `what-if`.
- **Adjacent security gates** — dependency/SCA scanning of vulnerable NuGet packages, container image scanning (Trivy/Grype), and broader SAST beyond SonarCloud — not included; SonarCloud's own SAST comes with the gate, but the others are a separate effort.
- **Raising overall (whole-codebase) coverage** or any brownfield remediation — the new-code gate deliberately avoids requiring this.
- **Migrating the app-deploy pipelines** or changing the existing branch-per-environment promotion model.
- **Backfilling unit tests in GitHub Actions beyond what the Sonar PR job needs** to compute coverage.

## Further Notes

- The two workstreams are fully independent and can ship in either order or in parallel; neither depends on the other.
- The originating brief named three competencies (pipeline automation, SonarQube, "Terraform or ARM"). The grill established the repo already covers pipeline automation broadly and that "ARM" is satisfied by Bicep; the genuine gaps are the **quality gate** (absent) and **IaC pipeline automation** (IaC exists, automation does not). This PRD targets exactly those gaps.
- Per-service required checks + path filters are the one real footgun in Workstream A: a GitHub *required* check that never reports sits pending and blocks the merge forever. The aggregator-check pattern is the deliberate mitigation and must land with the gate, not after.
- The infra identity's **User Access Administrator** requirement is non-obvious and load-bearing: it exists solely because `acr-pull-role.bicep` creates a role assignment. A Contributor-only connection will fail the apply on that single resource.
- `sqlAdminPassword` is already injected at deploy time (not stored in `.bicepparam`), so the automated path inherits the same secret-handling expectation — the variable-group-from-Key-Vault decision formalizes how the pipeline supplies it.
