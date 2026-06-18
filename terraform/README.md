# Terraform — `sbx2` Azure lane

A second, parallel Azure IaC lane that owns **only** the greenfield `sbx2`
environment (`10.50.0.0/16`, southeastasia). Bicep keeps dev/staging/prod **and**
its existing `sandbox` (`10.40`) untouched — no resource is ever owned by both
tools, so there is nothing to `terraform import`.

See the PRD (`docs/prd/PRD-Terraform-Azure-IaC-Option.md`) and plan
(`docs/plans/terraform-azure-iac-option.md`). ADR 0015 records the dual-tool
decision (Phase 6).

## Layout

```
terraform/
├── versions.tf            # required_version (~> 1.9) + azurerm (~> 4.0) + random (~> 3.6), pinned
├── backend.tf             # azurerm backend (partial; coordinates via -backend-config)
├── providers.tf           # provider azurerm (auth via ARM_* env vars)
├── locals.tf              # naming + common_tags (env=sbx2 / managedBy=terraform)
├── variables.tf           # location / workload / environment
├── main.tf                # RG (P1) + network/aks/registry (P2) + sql/redis/servicebus/keyvault (P3)
├── outputs.tf             # RG + compute (vnet / aks / acr) + data-plane (sql/redis/sb/kv) outputs
├── .terraform.lock.hcl    # committed, multi-platform provider hashes
├── modules/               # child modules per concern (P2: network/aks/registry; P3: sql/redis/servicebus/keyvault)
│   ├── network/           # VNet 10.50.0.0/16 + aks/private-endpoints/agents subnets
│   ├── aks/               # AKS (system MI, single burstable node) + Log Analytics
│   ├── registry/          # ACR + AcrPull role assignment for the kubelet identity
│   ├── sql/               # SQL Server + 7 serverless databases (random_password admin)
│   ├── redis/             # Azure Cache for Redis (Basic C0)
│   ├── servicebus/        # Service Bus namespace + 11 integration-event topics
│   └── keyvault/          # Key Vault (RBAC auth, provisioned-but-unwired)
├── environments/
│   ├── sbx2.tfvars        # per-env values (mirrors the .bicepparam convention)
│   └── sbx2.backend.hcl   # state backend coordinates for `init`
└── bootstrap/
    └── bootstrap-tfstate.sh   # one-time hardened state-account bootstrap
```

Phase 3 added the data-plane child modules (sql, redis, servicebus, keyvault)
under this same root, completing the self-contained `sbx2` environment.

## CI vs CD split

- **CI gate (GitHub Actions, `.github/workflows/terraform-ci.yml`)** — credential-free
  `fmt -check` / `validate -backend=false` / `tflint` on PRs touching `terraform/`.
  Mirrors the kubeconform manifest gate.
- **CD lane (Azure Pipelines, `infrastructure-deployment/pipelines/terraform-pipeline.yml`)** —
  `validate → plan -out=tfplan (published artifact) → apply tfplan`, authenticated
  by a WIF service connection and gated on the `tf-sbx2` Environment approval.

## First-time setup

```bash
# 1. Bootstrap the remote-state backend (admin, once).
az login
az account set --subscription "<sbx2-subscription-id>"
terraform/bootstrap/bootstrap-tfstate.sh        # creates rg-tfstate + hardened SA + container
# -> set storage_account_name in environments/sbx2.backend.hcl to the created account

# 2. Init against the backend and plan/apply locally (or via the pipeline).
cd terraform
export ARM_SUBSCRIPTION_ID="<sbx2-subscription-id>"
terraform init -backend-config=environments/sbx2.backend.hcl
terraform plan -var-file=environments/sbx2.tfvars
```

## Static checks (what the CI gate runs)

```bash
cd terraform
terraform fmt -check -recursive
terraform init -backend=false
terraform validate
tflint --init && tflint
```

## Notes

- **`default_tags`** — azurerm has no provider-level default-tags block (unlike the
  AWS provider), so the PRD's "provider `default_tags`" is realized as a
  `common_tags` local in `locals.tf`, merged into each resource's `tags`.
- **Auth** is driven entirely by `ARM_*` env vars: `az login` locally; the WIF
  service connection exports `ARM_CLIENT_ID` / `ARM_OIDC_TOKEN` / `ARM_TENANT_ID` /
  `ARM_SUBSCRIPTION_ID` (with `ARM_USE_OIDC=true`) in the pipeline. No secret is
  stored in the repo or the provider block.
- **State secrets** — from Phase 3 onward the SQL admin password and key outputs
  land in state; the hardened `azurerm` backend (TLS1.2, versioning, soft-delete,
  access-controlled) is the mitigation. Acceptable for a sandbox.
