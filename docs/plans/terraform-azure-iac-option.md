# Plan: Terraform as a second Azure IaC lane (`sbx2`)

> Source PRD: [PRD-Terraform-Azure-IaC-Option.md](../prd/PRD-Terraform-Azure-IaC-Option.md)

Tracer-bullet slicing: every phase cuts the **whole lane** — Terraform code → GitHub Actions static gate → Azure Pipelines `validate → plan-artifact → approved-apply` → real Azure resources. Phase 1 proves the entire pipeline with a near-empty config; each later phase thickens the resource set, then adds app deploy, then end-to-end smoke. No phase builds a layer in isolation.

## Architectural decisions

Durable decisions that apply across all phases:

- **Ownership boundary**: Bicep owns dev/staging/prod **and** its existing `sandbox` (`10.40`). Terraform owns **only** the new `sbx2` env. No resource is ever mutated by both tools; no `terraform import`.
- **Env identity**: `sbx2` — `10.50.0.0/16`, southeastasia, its own resource group. Naming normalized to `sbx2` everywhere (state key `sbx2.tfstate`, var group `SBX2_*`, DevOps Environment `tf-sbx2`).
- **State**: `azurerm` Storage Account backend, blob-lease locking, one state key per env. Backend SA bootstrapped out-of-band by a committed script.
- **Layout**: single Terraform root; per-env `.tfvars` (no workspaces); child modules per concern (`network`, `aks`, `registry`, `sql`, `redis`, `servicebus`, `keyvault`). Terraform + azurerm provider (`~> 4.x`) pinned; `.terraform.lock.hcl` committed; provider `default_tags` (`env=sbx2`, `managedBy=terraform`).
- **CI vs CD split**: GitHub Actions = credential-free static gate (`fmt`/`validate`/`tflint`). Azure Pipelines = credentialed `plan`/`apply`, gated by a `tf-sbx2` DevOps Environment approval. Mirrors the repo's existing kubeconform-gate / Azure-Pipelines-deploy split.
- **Auth**: a new dedicated Workload Identity Federation service connection, scoped to the `sbx2` subscription, Contributor **+** User Access Administrator (required for the `AcrPull` role assignment). No stored secret; existing app-CD service principals untouched.
- **Cost profile**: `sbx2` mirrors the existing sandbox's cheap SKUs (burstable single-node AKS, SQL Serverless auto-pause, Redis Basic C0, Log Analytics daily cap).
- **App deploy**: reuses the shared `deploy-stage.yml` template (extended with a `sbx2` target); IaC and app deploy stay decoupled. Secrets flow TF sensitive outputs → `SBX2_*` vars → existing `createSecret` task. `sbx2` pods run `Qa__Seed=true`, `replicas=1` (self-migrate at startup, no migration race).

---

## Phase 1: Tracer — state backend + full lane provisions an empty `sbx2` RG

**User stories**: 1, 2, 15, 16, 17, 18, 20, 21, 22, 23, 24, 25, 26, 27, 28, 37, 38, 39

### What to build

The complete CI/CD machinery proven end-to-end against the smallest possible resource: just the `sbx2` resource group. Committed bootstrap script creates `rg-tfstate` + hardened Storage Account + state container. A Terraform root with the `azurerm` backend, pinned versions, `default_tags`, `environments/sbx2.tfvars`, and a single `azurerm_resource_group`. The GitHub Actions static gate runs `fmt`/`validate`/`tflint` on the PR. The Azure Pipeline authenticates via the new WIF service connection and runs `validate → plan -out=tfplan (published artifact) → apply tfplan` gated by `tf-sbx2` approval. Merging and approving creates a real, empty `sbx2` resource group in Azure.

### Acceptance criteria

- [ ] Bootstrap script runs once and creates `rg-tfstate` + Storage Account (TLS1.2 min, blob versioning, soft-delete) + `tfstate` container.
- [ ] `terraform init` succeeds against the `azurerm` backend with state key `sbx2.tfstate`.
- [ ] GitHub Actions `terraform-ci.yml` runs `fmt -check`, `validate -backend=false`, `tflint` on a PR touching `terraform/` and goes green; no Azure credentials used.
- [ ] WIF service connection authenticates from the pipeline (`az login` / provider auth succeeds) with no stored secret.
- [ ] Pipeline publishes a `tfplan` artifact, pauses on the `tf-sbx2` Environment approval, and on approval applies the exact saved plan.
- [ ] An empty `sbx2` resource group exists in Azure with `env=sbx2` / `managedBy=terraform` tags after apply.
- [ ] No Bicep file changed.

---

## Phase 2: Compute slice — network + AKS + ACR through the same lane

**User stories**: 3, 6, 7, 8, 9, 14 (compute), 19

### What to build

Thicken the tracer into an empty-but-ready cluster. Add the `network` module (VNet `10.50.0.0/16`, three subnets: AKS `/20`, private-endpoints `/24`, agents `/24`), the `aks` module (system-assigned MI, Container Insights wired to a Log Analytics workspace, single burstable node), and the `registry` module (ACR + `azurerm_role_assignment` granting `AcrPull` to the AKS kubelet identity). All flow through the existing Phase 1 pipeline and static gate. Establishes the child-module-per-concern structure.

### Acceptance criteria

- [ ] `network`, `aks`, `registry` exist as child modules with clean variable/output interfaces; `aks` consumes `network` subnet outputs.
- [ ] `plan` artifact shows exactly the intended compute resources; approved `apply` creates them in `sbx2`.
- [ ] AKS comes up with a system-assigned managed identity and Container Insights reporting to the Log Analytics workspace.
- [ ] The kubelet identity holds `AcrPull` on the ACR (verified by a successful image pull, or role-assignment present in state/portal) — no deterministic `guid()` seed used.
- [ ] Compute SKUs match the cost-minimizing sandbox profile.
- [ ] Static gate stays green; no Bicep file changed.

---

## Phase 3: Data plane slice — SQL + Redis + Service Bus + Key Vault

**User stories**: 5, 10, 11, 12, 13, 14 (data), 19

### What to build

Complete the self-contained `sbx2` data plane. Add `sql` (one logical server, 7 databases via a `for_each` map keyed by service, SQL Serverless with auto-pause, admin password from `random_password`), `redis` (Basic C0), `servicebus` (namespace + 11 topics via `for_each` matching the integration-event set), and `keyvault` (RBAC-enabled, provisioned-but-unwired). Connection strings / keys surfaced as sensitive outputs ready for Phase 4. Same lane.

### Acceptance criteria

- [ ] SQL server + 7 named databases provisioned via `for_each`; admin password generated by `random_password` and present only in state.
- [ ] Redis (Basic C0) and Service Bus namespace with all 11 topics provisioned via `for_each`.
- [ ] Key Vault provisioned with RBAC auth, not wired to pods.
- [ ] SQL/Redis/Service Bus sensitive connection outputs exist and are marked `sensitive = true`.
- [ ] Data-plane SKUs match the cost-minimizing sandbox profile (Serverless auto-pause, Basic C0).
- [ ] Static gate green; approved `apply` creates the full data plane; no Bicep file changed.

---

## Phase 4: First app end-to-end — `auth` runs on `sbx2`

**User stories**: 29, 30, 31, 32, 33, 34

### What to build

Prove the deploy + secret-flow + self-migrate loop with **one** service before rolling all. Extend the shared `deploy-stage.yml` with a `sbx2` environment; add the `SBX2_` variable group and the `tf-sbx2` DevOps Environment. Surface the Phase 3 Terraform sensitive outputs into `SBX2_*` variables and inject them into pods via the existing `KubernetesManifest@0 createSecret` task. Configure `Qa__Seed=true` and `replicas=1`. Deploy the `auth` service; it self-migrates its database at startup and reports healthy. IaC pipeline never triggers this deploy (decoupled).

### Acceptance criteria

- [ ] `deploy-stage.yml` gains a `sbx2` target; `SBX2_` var group and `tf-sbx2` DevOps Environment exist.
- [ ] Terraform sensitive outputs reach pod env via `SBX2_*` vars + the existing `createSecret` task (no new secret mechanism).
- [ ] `auth` deploys to `sbx2` with `Qa__Seed=true`, `replicas=1`.
- [ ] `auth` runs `MigrateDatabase()` + QA seeders at startup; its database schema self-provisions; `/health` is green.
- [ ] The Terraform pipeline does not trigger app deploy (separation preserved).

---

## Phase 5: Roll remaining services + end-to-end smoke

**User stories**: 12 (messaging e2e), 33, 34 (applied to all services)

### What to build

Deploy the remaining services (basket, order, product, inventory, shipping, payment, saga, api-gateway) onto `sbx2` via the same extended template, each self-migrating where applicable, all single-replica with `Qa__Seed=true`. Run the Bruno smoke suite against the `sbx2` API gateway to exercise the full saga flow over real Azure SQL / Redis / Service Bus.

### Acceptance criteria

- [ ] All nine services deploy to `sbx2`; each reports healthy.
- [ ] Services with a datastore self-provision schema + demo data at startup.
- [ ] Messaging works end-to-end over the provisioned Service Bus (saga participants exchange commands/events).
- [ ] Bruno smoke suite passes against the `sbx2` gateway, covering the end-to-end order saga.

---

## Phase 6: Record the dual-tool decision

**User stories**: 4, 35, 36

### What to build

Write `docs/adr/0015-terraform-as-azure-iac-option.md` (Accepted): ownership boundary (Bicep owns dev/staging/prod/sandbox; Terraform owns `sbx2`), greenfield-parallel constraint, ADO+WIF runner/auth, `azurerm` state backend, and explicit non-supersession of Bicep. Add a one-line cross-reference from `docs/prd/azure-infrastructure-deployment.md`. Confirm the cross-cutting invariant that no Bicep-owned environment was touched throughout.

### Acceptance criteria

- [ ] ADR `0015` exists, status Accepted, recording ownership boundary + constraints + "does not supersede Bicep".
- [ ] One-line cross-reference added from the existing PRD's Bicep-over-Terraform note.
- [ ] Verified: no change to any Bicep file or any dev/staging/prod/sandbox environment across all phases.

---

## Open items (resolve before/at the relevant phase — not blockers to start)

- **Phase 1** — bootstrap SA target subscription/region (recommend same subscription, southeastasia); WIF AAD app-registration + federated-credential subject mapped to the `tf-sbx2` pipeline (AAD admin action).
- **Phase 2** — whether `sbx2` manifests keep the redundant `acr-pull-secret` imagePullSecret or rely solely on the kubelet `AcrPull` identity.
- **Phase 4** — exact `SBX2_*` variable names the `createSecret` task expects (inventory from Azure DevOps, mirror the `DEV_*` set).
