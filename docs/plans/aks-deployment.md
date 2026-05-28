# Plan: AKS Sandbox Deployment

> Source PRD: [docs/prd/PRD-Aks-Deployment.md](../prd/PRD-Aks-Deployment.md)

## Architectural decisions

Durable decisions that apply across all phases:

- **Environment model**: `main.bicep` gains a fourth `@allowed` value `sandbox` alongside `dev`, `staging`, `prod`. Resource naming follows the existing `${workload}-${environment}-*` convention (e.g. `ecom-sandbox-sql`, `ecom-sandbox-aks`).
- **Cost-gate parameter**: a new `costProfile` string parameter (`@allowed(['minimal', 'standard'])`, default `standard`) threads through every SKU-bearing child module. Existing parameter files (`dev.bicepparam`, `staging.bicepparam`, `prod.bicepparam`) are untouched.
- **SQL Serverless path**: activated exclusively when `costProfile == 'minimal'`. Uses `GP_S_Gen5` tier on `Microsoft.Sql/servers/databases@2023-05-01-preview` with `minCapacity = 0.5` and `autoPauseDelay = 60` (minutes). The existing provisioned SKU path (`dbSkuName`/`dbSkuTier`) is unchanged for all other environments.
- **Sandbox AKS**: single-node `Standard_B2ms` burstable cluster (AKS Free-tier control plane, no extra cost). Kubernetes network plugin remains `azure` to match existing modules.
- **Sandbox ACR**: no new ACR is provisioned. The sandbox param file accepts an existing `acrName`; the ACR module invocation in `main.bicep` is skipped conditionally for sandbox.
- **Kubernetes manifests**: one Deployment + ClusterIP Service per service, named `aks-sandbox-<service>.yml`. All nine services (basket, order, product, auth, api-gateway, inventory, shipping, payment, saga). Resource requests `50m / 128Mi`; limits `200m / 256Mi`. Readiness probe `initialDelaySeconds: 60`, `periodSeconds: 10`, `failureThreshold: 6`. Image tag placeholder `$(IMAGE_TAG)` for pipeline variable substitution.
- **Ingress**: a single `aks-sandbox-ingress.yml` deploys Nginx Ingress Controller and the `IngressClass` resource. All services remain `ClusterIP`; external traffic enters via one public IP assigned to the Ingress load balancer.
- **Ops pipelines location**: `infrastructure-deployment/pipelines/ops/`. Stop (`sandbox-stop.yml`) runs `az aks stop` at 22:00 UTC daily. Start (`sandbox-start.yml`) runs `az aks start` at 08:00 UTC Mon–Fri only. Deploy (`sandbox-deploy.yml`) has `trigger: none` and a manual `imageTag` input parameter.
- **Budget enforcement**: `modules/budget.bicep` wraps `Microsoft.Consumption/budgets` at resource group scope. Deployed from `main.bicep` only when `environment == 'sandbox'`. $100/month cap, 80% forecasted threshold alert.

---

## Phase 1: Bicep `environment` + `costProfile` parameter extension

**User stories**: 3, 4, 24

### What to build

Extend `main.bicep` and all SKU-bearing child modules to accept the new `sandbox` environment value and a `costProfile` parameter. No sandbox-specific SKUs are wired yet — this phase establishes the plumbing so existing environments remain unbroken and the new parameter is accepted at the CLI.

Add `sandbox` to the `@allowed` enum on `main.bicep`'s `environment` parameter. Add a `costProfile` string parameter (`@allowed(['minimal', 'standard'])`, default `standard`) and thread it through to `sql.bicep`, `redis.bicep`, `aks.bicep`, `monitor.bicep`, and `appinsights.bicep` as a passthrough parameter (no branching logic yet — just accept and ignore).

### Acceptance criteria

- [ ] `az bicep build --file main.bicep` succeeds after adding `sandbox` to the enum.
- [ ] `costProfile` parameter exists in `main.bicep` with default `standard` and is passed to each SKU-bearing child module.
- [ ] Each child module (`sql.bicep`, `redis.bicep`, `aks.bicep`, `monitor.bicep`, `appinsights.bicep`) accepts `costProfile` without using it yet.
- [ ] `az deployment group what-if` against a dev resource group with `environment=dev` produces no changes (existing dev param file continues to work unchanged).
- [ ] `az bicep build` on `dev.bicepparam`, `staging.bicepparam`, `prod.bicepparam` all succeed — no new required parameters added to them.

---

## Phase 2: SQL Serverless and observability cap extensions

**User stories**: 5, 6, 12, 13

### What to build

Wire the actual `costProfile == 'minimal'` branching logic into `sql.bicep`, `monitor.bicep`, and `appinsights.bicep`.

In `sql.bicep`: add optional `minCapacity` (default `0.5`), `autoPauseDelay` (default `60`) parameters and a `serverlessTier` computed variable (`GP_S_Gen5`) gated on `costProfile == 'minimal'`. The `databases` resource loop uses a conditional `sku` block and sets `properties.autoPauseDelay` and `properties.minCapacity` only on the serverless path.

In `monitor.bicep`: add `dailyCapGb` parameter (type `int`, default `-1`). When value is positive, set `workspaceCapping.dailyQuotaGb` on the workspace resource.

In `appinsights.bicep`: add `samplingPercentage` parameter (default `100`). Wire to `properties.SamplingPercentage` on the App Insights component resource.

### Acceptance criteria

- [ ] `az bicep build` succeeds on all three modified modules.
- [ ] When `costProfile == 'minimal'` is passed, the SQL module's ARM output contains `sku.name == 'GP_S_Gen5'` and `properties.autoPauseDelay == 60` (verifiable via `az deployment group what-if --query` or ARM JSON diff).
- [ ] When `costProfile == 'standard'`, the SQL module output is unchanged from today (Basic/Standard tier, no autoPauseDelay property).
- [ ] `monitor.bicep` sets `workspaceCapping.dailyQuotaGb` only when `dailyCapGb > 0`.
- [ ] `appinsights.bicep` sets `SamplingPercentage` to the supplied value.
- [ ] Existing `dev.bicepparam` deploy produces no changes to SQL, monitor, or App Insights resources.

---

## Phase 3: Budget module and `sandbox.bicepparam`

**User stories**: 2, 7, 8, 20, 23, 24

### What to build

Create the `modules/budget.bicep` module and the `parameters/sandbox.bicepparam` parameter file, then wire the budget module into `main.bicep` under a `environment == 'sandbox'` conditional.

`budget.bicep` wraps `Microsoft.Consumption/budgets` with parameters: `budgetName`, `amount` (default `100`), `contactEmails` (array), `firstThresholdPercent` (default `80`), `timeGrain` (default `Monthly`), `startDate`. The threshold uses `thresholdType: 'Forecasted'`. Exposes the budget resource ID as output.

`sandbox.bicepparam` sets `environment = 'sandbox'`, `costProfile = 'minimal'`, `aksSystemNodeCount = 1`, `aksSystemNodeVmSize = 'Standard_B2ms'`, SQL serverless params, Redis Basic C0, Log Analytics 0.1 GB/day cap, App Insights 10% sampling, Service Bus Standard, an `acrName` pointing to an existing registry. No `acrSku` — the ACR module invocation in `main.bicep` is skipped when `environment == 'sandbox'` (the sandbox reuses an existing ACR).

### Acceptance criteria

- [ ] `az bicep build --file modules/budget.bicep` succeeds.
- [ ] Budget module ARM output contains `thresholds[0].thresholdType == 'Forecasted'` and `amount == 100`.
- [ ] `az bicep build --file parameters/sandbox.bicepparam` succeeds.
- [ ] `main.bicep` invokes `budget.bicep` only when `environment == 'sandbox'` (conditional module deployment).
- [ ] `main.bicep` skips the ACR provisioning module when `environment == 'sandbox'`.
- [ ] `az deployment group what-if` with `sandbox.bicepparam` targets a sandbox resource group and produces a valid plan without errors (manual review of SKUs in the output).
- [ ] Existing `dev.bicepparam`, `staging.bicepparam`, `prod.bicepparam` `what-if` runs show no changes.

---

## Phase 4: Sandbox Kubernetes manifests (nine services + ingress)

**User stories**: 1, 9, 10, 11, 22, 25

### What to build

Create ten Kubernetes manifest files under `kubernetes/`:

- `aks-sandbox-<service>.yml` for each of: basket, order, product, auth, api-gateway, inventory, shipping, payment, saga (nine files).
- `aks-sandbox-ingress.yml` for the Nginx Ingress Controller and `IngressClass` resource.

Each service manifest contains a `Deployment` and a `ClusterIP` `Service`. Deployment spec: `replicas: 1`, resource requests `cpu: 50m / memory: 128Mi`, limits `cpu: 200m / memory: 256Mi`. Readiness probe: HTTP GET `/health/ready` on the service port, `initialDelaySeconds: 60`, `periodSeconds: 10`, `failureThreshold: 6`. Image reference: `$(ACR_NAME).azurecr.io/<service>:$(IMAGE_TAG)`. Environment variables sourced from existing Kubernetes secret conventions matching the dev manifests (connection strings, JWT keys, App Insights, Redis, Service Bus).

The ingress manifest deploys the `ingress-nginx` controller Deployment and the `IngressClass` resource. An `Ingress` resource routes all path prefixes to the `api-gateway` ClusterIP service.

### Acceptance criteria

- [ ] `kubectl --dry-run=client -f "kubernetes/aks-sandbox-*.yml"` passes for all ten manifests.
- [ ] Each of the nine service manifests has `replicas: 1`, requests `50m/128Mi`, limits `200m/256Mi`.
- [ ] Each readiness probe has `initialDelaySeconds: 60`, `periodSeconds: 10`, `failureThreshold: 6`.
- [ ] Image references contain the `$(ACR_NAME)` and `$(IMAGE_TAG)` placeholders.
- [ ] The ingress manifest deploys an `IngressClass` and routes to `api-gateway`'s ClusterIP service.
- [ ] All nine services are present (basket, order, product, auth, api-gateway, inventory, shipping, payment, saga).

---

## Phase 5: Sandbox ops pipelines (stop / start / deploy)

**User stories**: 14, 15, 16, 17, 18, 19

### What to build

Create three Azure Pipelines YAML files under `infrastructure-deployment/pipelines/ops/`:

`sandbox-stop.yml`: `trigger: none`. A `schedules:` block with cron `0 22 * * *` (22:00 UTC daily, including weekends). One `AzureCLI@2` step that runs `az aks stop --resource-group $(SANDBOX_RG) --name $(SANDBOX_AKS_NAME)`. No kubectl involved — VMSS is fully deallocated.

`sandbox-start.yml`: `trigger: none`. A `schedules:` block with cron `0 8 * * 1-5` (08:00 UTC Monday–Friday only). One `AzureCLI@2` step running `az aks start --resource-group $(SANDBOX_RG) --name $(SANDBOX_AKS_NAME)`.

`sandbox-deploy.yml`: `trigger: none`. A `parameters:` block with one string input `imageTag`. Steps: `az aks get-credentials` to set kubectl context, then `kubectl set image` for each of the nine Deployments substituting `$(imageTag)`. Uses `AzureCLI@2` with `addSpnToEnvironment: true` and an AKS service connection via `kubelogin`.

### Acceptance criteria

- [ ] `sandbox-stop.yml` cron expression is `0 22 * * *` and the `az aks stop` command references the correct resource group and cluster name variables.
- [ ] `sandbox-start.yml` cron expression is `0 8 * * 1-5` (weekdays only; no Saturday/Sunday entry).
- [ ] `sandbox-deploy.yml` has `trigger: none` and a `parameters:` block accepting `imageTag`.
- [ ] All three files are stored under `infrastructure-deployment/pipelines/ops/`.
- [ ] YAML schema validation (e.g. `az pipelines run --validate` or equivalent) passes without errors on all three files.
- [ ] No per-service `azure-pipelines.yml` files are modified.

---

## Phase 6: Sandbox runbook (`SANDBOX.md`)

**User stories**: 21

### What to build

Create `infrastructure-deployment/docs/SANDBOX.md` as the operator-facing runbook. Sections:

1. **Overview** — purpose (learning/demo, not production), $80/month target, $100/month hard cap.
2. **Cost Breakdown** — table with line items: AKS node (`Standard_B2ms`), SQL Serverless (7 databases), Redis Basic C0, Service Bus Standard, Load Balancer / public IP, Log Analytics (0.1 GB/day cap), App Insights (10% sampling), ACR (shared, cost not charged to this RG). Total estimated and hard cap.
3. **Start/Stop Schedule** — table showing weekday 08:00 UTC start, daily 22:00 UTC stop; note that weekend start requires manual pipeline trigger.
4. **Budget Alert Wiring** — how the Bicep budget module is wired, where to find the budget resource in the Azure portal, and how to update the contact email.
5. **Manual Deploy Pipeline Steps** — how to trigger `sandbox-deploy.yml` manually with an image tag input in Azure DevOps.
6. **SQL Serverless Cold-Start Note** — ~20–30 second resume latency; readiness probe configuration (`initialDelaySeconds: 60`, `failureThreshold: 6`) absorbs it; EF Core `EnableRetryOnFailure` handles the transient error.
7. **Cleanup / Teardown** — `az group delete` command to remove all sandbox resources.

### Acceptance criteria

- [ ] `SANDBOX.md` exists at `infrastructure-deployment/docs/SANDBOX.md`.
- [ ] All seven sections listed above are present.
- [ ] Cost breakdown table shows individual line items summing to the ~$80/month estimate with a $100 cap call-out.
- [ ] Start/stop schedule table correctly notes weekday-only auto-start and manual weekend start procedure.
- [ ] SQL Serverless cold-start section references the `initialDelaySeconds: 60` and `failureThreshold: 6` probe values and EF Core retry.
- [ ] No confidential credentials, subscription IDs, or tenant IDs appear in the document (use placeholder variables referencing pipeline variable groups).
