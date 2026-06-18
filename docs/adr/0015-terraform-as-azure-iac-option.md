# ADR-0015 — Terraform as a second Azure IaC lane owning the `sbx2` environment

- **Status**: Accepted
- **Date**: 2026-06-18

## Context

The platform provisions all Azure infrastructure with **Bicep** — 15 files orchestrated by `main.bicep` under [`infrastructure-deployment/bicep/`](../../infrastructure-deployment/bicep/), applied via `az deployment group create`, owning the **dev**, **staging**, **prod**, and cost-optimized **sandbox** environments. That choice was recorded as a PRD implementation decision in [azure-infrastructure-deployment.md](../prd/azure-infrastructure-deployment.md) ("Use **Bicep** (not Terraform or ARM)…"), never as an ADR.

A developer goal — gain Terraform experience and stand up the full microservices stack on Azure through a second, independently-owned provisioning lane — has no home today. Adding Terraform naively is hazardous: two IaC tools mutating the same resources produce drift and ownership wars, and adopting the live 7-database SQL server, `listKeys`-backed secrets, and seeded role assignments via `terraform import` is high-risk. The effort therefore needs a clear ownership boundary, its own state backend, its own CI/CD lane, and a recorded decision — none of which existed.

PRD: [PRD-Terraform-Azure-IaC-Option.md](../prd/PRD-Terraform-Azure-IaC-Option.md). Plan: [terraform-azure-iac-option.md](../plans/terraform-azure-iac-option.md). Implementation: [`terraform/`](../../terraform/).

## Decision

Introduce Terraform as a **second, parallel** Azure IaC lane that owns a brand-new, self-contained environment, **`sbx2`** (`10.50.0.0/16`, southeastasia, its own resource group). Bicep keeps dev/staging/prod **and its existing `sandbox` env** untouched. No resource is ever owned by both tools.

- **Ownership boundary.** Bicep owns dev/staging/prod/sandbox; Terraform owns `sbx2` and only `sbx2`. The two lanes never write to the same resource, so there is nothing to `terraform import` and no drift contest. `sbx2` is a deliberately new name (not Bicep's `sandbox`) and uses a non-overlapping `10.50.0.0/16` CIDR so it could be peered later without collision.
- **Greenfield-parallel constraint.** Terraform provisions a complete, self-contained data plane for `sbx2` — VNet + AKS + ACR + SQL (7 databases) + Redis + Service Bus (11 topics) + Key Vault — as child modules under [`terraform/modules/`](../../terraform/modules/), mirroring Bicep's module-per-concern layout and the sandbox cost-minimizing SKU profile. No live resource is adopted; everything is created fresh.
- **State backend.** `azurerm` Storage Account backend with blob-lease locking and one state key per environment (`sbx2.tfstate`), bootstrapped once out-of-band by a committed script ([`terraform/bootstrap/`](../../terraform/bootstrap/bootstrap-tfstate.sh)). No extra lock table; the blob lease serializes concurrent applies. The SQL admin password (`random_password`) and `listKeys`-equivalent outputs live only in this access-controlled, soft-delete-protected state.
- **Runner + auth (ADO + WIF).** The plan/apply lane runs in **Azure Pipelines** ([`terraform-pipeline.yml`](../../infrastructure-deployment/pipelines/terraform-pipeline.yml)) — `validate → plan-artifact → approved-apply`, gated by a `tf-sbx2` DevOps Environment so the reviewed plan equals the applied plan. It authenticates through a dedicated **Workload Identity Federation** service connection (no stored secret) scoped to the `sbx2` subscription with Contributor **and** User Access Administrator (the latter required so the kubelet `AcrPull` role assignment can be created). The existing app-CD service principals are untouched. A credential-free GitHub Actions gate ([`terraform-ci.yml`](../../.github/workflows/terraform-ci.yml)) runs `fmt`/`validate`/`tflint` at PR time, mirroring the existing kubeconform manifest gate — preserving the repo's CI-on-GitHub-Actions / CD-on-Azure-Pipelines split.
- **Does not supersede Bicep.** This ADR adds a parallel lane; it does **not** retire, replace, or deprecate Bicep for any environment. The original Bicep-over-Terraform rationale in [azure-infrastructure-deployment.md](../prd/azure-infrastructure-deployment.md) stands for the environments Bicep owns.

## Consequences

- A second IaC tool now exists in the repo, scoped to exactly one environment. Readers must understand the boundary: changes to dev/staging/prod/sandbox go through Bicep; `sbx2` goes through Terraform. The boundary is the safety property — it is what guarantees no resource is mutated by two tools.
- Zero regression risk to existing environments was verified structurally: across all phases the branch added only new files under `terraform/`, the two new pipeline/CI files, and these docs — **no `*.bicep`/`*.bicepparam`, no existing Kubernetes manifest, and no dev/staging/prod/sandbox config was modified.**
- Secrets (SQL admin password, Redis/Service Bus keys) land in Terraform state. The `azurerm` backend (encrypted at rest, access-controlled, soft-delete) is the accepted mitigation for a sandbox; this would need revisiting if `sbx2` were ever promoted toward production-shaped behavior.
- `apply` is Azure-Pipelines-only and always human-gated, even for a low-risk sandbox — no self-mutation without approval. Terraform Cloud/HCP, Terratest/checkov policy scanning, and Key Vault CSI pod wiring are explicitly deferred.
- Live apply remains HITL: the bootstrap run, the WIF AAD app-registration + federated-credential mapping, and the first `terraform apply` require human Azure/ADO setup (tracked separately). The committed code is verified only by the static gate until then.

## Composes

- **Does not supersede any prior ADR.** The Bicep choice it sits beside was a PRD implementation decision, not an ADR, so there is nothing to mark superseded; Bicep remains the owner of dev/staging/prod/sandbox.
- **Composes [ADR-0007](0007-ef-core-database-per-service.md) by reference.** The 7-database `for_each` map on the `sbx2` SQL server reflects the existing database-per-service boundary; Terraform provisions the same shape, it does not change it.
