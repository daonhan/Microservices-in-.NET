## Problem Statement

The existing Azure deployment foundation targets Dev, Staging, and Prod environments and explicitly puts cost optimization out of scope. As a developer learning and demoing this microservices platform, running all nine services end-to-end on Azure currently requires a production-shaped environment that costs far more than a personal learning budget allows. There is no environment optimized for exploration, experimentation, and demonstration where compute and database costs can be reduced to roughly $80/month with a hard $100/month ceiling — without polluting the production-shaped environments.

## Solution

Introduce a fourth environment value, `sandbox`, backed by a `costProfile` parameter that gates SKU selection across all Bicep modules. A new `sandbox.bicepparam` file selects the cheapest viable SKUs: a single-node burstable AKS cluster, SQL Serverless databases (auto-pause after 60 minutes idle), Redis Basic C0, and a Log Analytics workspace with a 100 MB/day ingestion cap. Auto-stop and auto-start Azure Pipelines schedules deallocate and restart the AKS node pool on a weekday schedule, eliminating overnight and weekend compute costs. A Bicep `Microsoft.Consumption/budgets` resource enforces a $100/month hard cap with an 80% forecast email alert. Kubernetes manifests for all nine services use tight resource requests (50m/200m CPU, 128Mi/256Mi RAM) to fit comfortably on a single `Standard_B2ms` node. An operator-facing runbook documents the cost breakdown, the start/stop schedule, manual deploy steps, and the SQL Serverless cold-start behavior.

## User Stories

1. As a developer, I want to deploy all nine microservices to Azure on a sandbox environment for roughly $80/month, so that I can learn and demo the full platform without excessive personal expense.
2. As a developer, I want a single `sandbox.bicepparam` file that selects all cost-minimizing SKUs, so that I can provision the entire sandbox stack with one `az deployment group create` command.
3. As a developer, I want the `environment` Bicep parameter to accept `sandbox` as a valid value, so that resource names and tags follow the existing `${workload}-${environment}-*` convention without hacking the templates.
4. As a developer, I want a `costProfile` Bicep parameter (`minimal` / `standard`) that gates SKU selection in each module, so that existing Dev/Staging/Prod parameter files continue to work unchanged.
5. As a developer, I want the SQL module to support Azure SQL Serverless tier (GP_S_Gen5, minCapacity=0.5, auto-pause after 60 minutes), so that database costs approach zero when the sandbox is idle.
6. As a developer, I want SQL Serverless to be activated only when `costProfile == 'minimal'`, so that the Standard provisioned path used by existing environments is never changed.
7. As a developer, I want a `Microsoft.Consumption/budgets` Bicep module that caps the sandbox resource group at $100/month and sends an email alert at 80% of the forecasted spend, so that I have an automatic guardrail against unexpected cost overruns.
8. As a developer, I want the budget module to be invoked from `main.bicep` only when `environment == 'sandbox'`, so that existing environments are unaffected.
9. As a developer, I want sandbox Kubernetes manifests for all nine services (basket, order, product, auth, api-gateway, inventory, shipping, payment, saga) with resource requests of 50m CPU / 128Mi memory and limits of 200m CPU / 256Mi memory, so that all services fit on a single `Standard_B2ms` node.
10. As a developer, I want each sandbox service manifest to have a readiness probe with an `initialDelaySeconds` of 60, so that pods survive the SQL Serverless cold-start latency (~30 seconds) without being prematurely killed.
11. As a developer, I want a sandbox Nginx Ingress Controller manifest, so that I can reach all services through a single public IP without configuring per-service LoadBalancer services.
12. As a developer, I want the Log Analytics workspace Bicep module extended with a `dailyCapGb` parameter (set to 0.1 for sandbox, unlimited for existing envs), so that I can cap observability ingestion costs.
13. As a developer, I want the App Insights module extended with a `samplingPercentage` knob (set to 10 for sandbox), so that telemetry volume and cost are reduced without losing signal completely.
14. As a developer, I want an Azure Pipelines `sandbox-deploy.yml` pipeline with a manual trigger that accepts an image-tag input parameter and deploys all nine Kubernetes manifests, so that I can roll out a new image set in one click without a branch push.
15. As a developer, I want the sandbox deploy pipeline to have no branch trigger, so that it does not fight with the auto-stop schedule that shuts the cluster down nightly.
16. As a developer, I want an Azure Pipelines `sandbox-stop.yml` pipeline on a cron schedule of 22:00 daily that runs `az aks stop` via the `AzureCLI@2` task to deallocate the VMSS, so that I pay nothing for compute overnight and on weekends.
17. As a developer, I want the stop pipeline to use `az aks stop` (not `kubectl scale`), so that the underlying VMSS is actually deallocated and no node compute cost accrues while the cluster is stopped.
18. As a developer, I want an Azure Pipelines `sandbox-start.yml` pipeline on a cron schedule of 08:00 Monday–Friday (weekdays only, no weekend start) that runs `az aks start`, so that the cluster is ready for use at the start of each working day.
19. As a developer, I want the sandbox ops pipelines stored under `infrastructure-deployment/pipelines/ops/`, so that they are co-located with infrastructure concerns and separate from per-service build pipelines.
20. As a developer, I want the sandbox parameter file to accept the ACR name as an input parameter rather than provisioning its own ACR, so that I reuse an existing registry and avoid the ACR monthly fee.
21. As a developer, I want a `SANDBOX.md` runbook in `infrastructure-deployment/docs/` that shows the cost breakdown table, start/stop schedule, budget alert wiring instructions, manual deploy pipeline trigger steps, and a note about SQL Serverless cold-start behavior, so that I can operate the sandbox without memorizing configuration details.
22. As a developer, I want all nine services to run as single-replica Deployments in the sandbox, so that I keep resource usage minimal while still being able to exercise the full saga flow end-to-end.
23. As a developer, I want the resource naming convention `${workload}-${environment}-*` applied to sandbox resources (e.g., `ecom-sandbox-sql`), so that sandbox resources are immediately identifiable in the Azure portal.
24. As a developer, I want existing Dev/Staging/Prod parameter files to remain untouched when the `environment` enum and `costProfile` param are added, so that there is no regression risk to those environments.
25. As a developer, I want the Saga service included in the sandbox deployment, so that I can practice and demonstrate the full saga orchestration flow as the core architectural lesson of this repo.

## Implementation Decisions

### Module 1: Bicep `environment` + `costProfile` extension

- Extend `main.bicep` `@allowed` enum to include `sandbox` as a fourth value alongside `dev`, `staging`, `prod`.
- Add a `costProfile` string parameter with `@allowed(['minimal', 'standard'])`. Default is `standard` so existing callers require no changes.
- Thread `costProfile` through to every SKU-bearing child module (`sql`, `redis`, `aks`, `monitor`, `appinsights`) as a parameter.
- When `costProfile == 'minimal'`, each module selects the cheapest viable SKU internally; when `standard`, behavior is unchanged from today.

### Module 2: Bicep SQL Serverless module extension

- Extend `sql.bicep` with optional parameters for serverless tier: `dbSkuName` already exists; add `dbSkuTier` path for `GP_S_Gen5`, plus `minCapacity` (default 0.5 vCore) and `autoPauseDelay` (default 60 minutes, expressed in minutes as the ARM API expects).
- The Serverless path is activated when `costProfile == 'minimal'` is passed in; the standard provisioned path (current `dbSkuName`/`dbSkuTier` params) is used otherwise.
- `autoPauseDelay` maps to the `autoPauseDelay` property on `Microsoft.Sql/servers/databases@2023-05-01-preview`.

### Module 3: Bicep budget module (`modules/budget.bicep`)

- New Bicep module wrapping `Microsoft.Consumption/budgets`.
- Parameters: `budgetName`, `amount` (default 100), `contactEmails` (array), `firstThresholdPercent` (default 80), `timeGrain` (default `Monthly`), `startDate`.
- Exposes the budget resource ID as output.
- Called from `main.bicep` using a conditional deployment (`if (environment == 'sandbox')`).
- The 80% threshold uses `thresholdType: 'Forecasted'` to alert before the cap is reached.

### Module 4: `parameters/sandbox.bicepparam`

- New param file: `environment = 'sandbox'`, `costProfile = 'minimal'`.
- AKS: `aksSystemNodeCount = 1`, `aksSystemNodeVmSize = 'Standard_B2ms'`.
- SQL: serverless GP_S_Gen5, `minCapacity = 0.5`, `autoPauseDelay = 60`.
- Redis: `redisSkuFamily = 'C'`, `redisSkuName = 'Basic'`, `redisSkuCapacity = 0`.
- Service Bus: `serviceBusSku = 'Standard'`.
- Log Analytics: `dailyCapGb = 0.1`, `logRetentionDays = 30`.
- App Insights: `samplingPercentage = 10`.
- No ACR provisioned: file includes an `acrName` parameter pointing to an existing registry; `acrSku` is omitted or the ACR module is skipped via conditional.
- Budget: `budgetAmount = 100`, contact email wired to the operator.

### Module 5: Sandbox Kubernetes manifests (`kubernetes/aks-sandbox-*.yml`)

- One Deployment + Service manifest per service: basket, order, product, auth, api-gateway, inventory, shipping, payment, saga. Nine files total, named `aks-sandbox-<service>.yml`.
- All Deployments: `replicas: 1`.
- Resource requests: `cpu: 50m`, `memory: 128Mi`. Limits: `cpu: 200m`, `memory: 256Mi`.
- Readiness probe: `initialDelaySeconds: 60`, `periodSeconds: 10`, `failureThreshold: 6` — allows up to 60 + 60 = 120 seconds for SQL Serverless to wake.
- Image references parameterized via a tag placeholder (`$(IMAGE_TAG)`) compatible with the Azure Pipelines deploy pipeline's variable substitution.
- Plus one `aks-sandbox-ingress.yml` for Nginx Ingress Controller deployment and the `IngressClass` resource.

### Module 6: App Insights / Log Analytics workspace cap extension

- Extend `monitor.bicep` with a `dailyCapGb` parameter (type `int`, default `-1` meaning unlimited, matching Log Analytics API convention). When set to a positive value, the `workspaceCapping` property is set on the workspace resource.
- Extend `appinsights.bicep` with a `samplingPercentage` parameter (default 100, meaning no sampling). Wires to the App Insights `SamplingPercentage` property via the component's `properties` block.

### Module 7: Sandbox ops pipelines (`infrastructure-deployment/pipelines/ops/`)

- `sandbox-deploy.yml`: `trigger: none`. Manual `parameters:` block with `imageTag` string input. Steps: `kubectl set image` for each of the nine Deployments using `$(imageTag)`. Uses `KubernetesManifest@1` or raw `kubectl` via `AzureCLI@2` after running `az aks get-credentials`.
- `sandbox-stop.yml`: `trigger: none`. `schedules:` cron `0 22 * * *` (22:00 UTC daily). Single `AzureCLI@2` step: `az aks stop --resource-group ... --name ...`. No `kubectl` involved — VMSS is fully deallocated.
- `sandbox-start.yml`: `trigger: none`. `schedules:` cron `0 8 * * 1-5` (08:00 UTC Monday–Friday; no weekend entry). Single `AzureCLI@2` step: `az aks start --resource-group ... --name ...`.

### Module 8: Sandbox runbook (`infrastructure-deployment/docs/SANDBOX.md`)

- Operator-facing Markdown document.
- Sections: Overview, Cost Breakdown (table with line items: AKS node, SQL Serverless, Redis, ASB, Load Balancer/IP, App Insights/Log Analytics, ACR shared cost), Start/Stop Schedule, Budget Alert Wiring, Manual Deploy Pipeline Steps, SQL Serverless Cold-Start Note, Cleanup / Teardown.
- Cost table targets the ~$80/month estimate with a $100/month hard cap enforced by the budget module.

## Testing Decisions

### What makes a good test for this feature

Tests should validate externally observable behavior — that a Bicep template produces the expected resource properties when given specific parameter combinations — rather than internal template structure. For pipeline YAML, tests validate the cron expressions and task configuration are syntactically valid. For Kubernetes manifests, tests validate resource request and limit values and probe configuration are within defined bounds.

### Modules to test

- **Bicep parameter validation**: `sandbox.bicepparam` should pass `az bicep build --file` without error. The `costProfile` conditional paths in `sql.bicep` and `monitor.bicep` should be validated by running `az deployment group what-if` against a sandbox resource group.
- **SQL Serverless properties**: when `costProfile == 'minimal'`, the SQL module should produce a deployment with `sku.tier == 'GeneralPurpose'`, `sku.name == 'GP_S_Gen5'`, and `properties.autoPauseDelay == 60`. A Pester or Bicep unit test using `Assert-WhatIf` (or equivalent ARM template JSON comparison) covers this.
- **Budget module**: `modules/budget.bicep` in isolation — verify the `thresholds[0].thresholdType == 'Forecasted'` and `amount == 100`.
- **Kubernetes manifests**: A simple manifest lint (e.g., `kubectl --dry-run=client -f kubernetes/aks-sandbox-*.yml`) confirms all nine manifests are valid. Resource limits and probe `initialDelaySeconds` are verified via `yq` assertions or equivalent in the CI pipeline.
- **Pipeline YAML syntax**: `az pipelines` CLI or a YAML schema validator confirms the cron expressions and task names are syntactically correct.

### Prior art

The existing `infrastructure-deployment/bicep/parameters/dev.bicepparam` serves as a reference for the param file structure. The existing `azure-pipelines.yml` files per service are prior art for Azure Pipelines YAML conventions used in this repo.

## Out of Scope

- Microservice code consolidation (all nine services are deployed as-is).
- Changes to existing Dev, Staging, or Prod parameter files (only the enum and new `costProfile` param are added).
- Multi-region deployment, disaster recovery, or geo-replication for the sandbox environment.
- Private endpoints, private AKS cluster, or network security groups beyond defaults.
- Federated workload identity — sandbox continues to use the existing connection-string-via-Kubernetes-Secret pattern.
- Custom domain or TLS termination for sandbox Ingress (Nginx with default cluster IP and auto-assigned public IP only).
- Per-service Azure Pipelines changes — sandbox uses a separate ops pipeline track.
- ACR provisioning in sandbox Bicep — the sandbox reuses an existing ACR.
- High availability, multiple replicas, or pod disruption budgets for sandbox services.
- Any changes to the existing `azure-infrastructure-deployment` PRD or plan.

## Further Notes

- The AKS Free tier control plane remains running at $0 even while the node pool is stopped via `az aks stop`. Only the VMSS (node) cost is eliminated. This is why `az aks stop` must be used rather than `kubectl scale` — the latter leaves the node running.
- SQL Serverless databases typically resume in 20–30 seconds after the first connection following an auto-pause. The readiness probe `initialDelaySeconds: 60` combined with `failureThreshold: 6` provides up to ~120 seconds total before Kubernetes restarts a pod, which is sufficient for the resume latency.
- EF Core's built-in execution strategy (`EnableRetryOnFailure`) on each service handles the transient connection error during SQL resume without code changes.
- The `sandbox-start.yml` pipeline explicitly omits Saturday and Sunday from the cron schedule (`0 8 * * 1-5`). This means if a developer wants to work on a weekend they must trigger `sandbox-start.yml` manually.
- The budget resource (`Microsoft.Consumption/budgets`) must be deployed at resource group scope. The `targetScope = 'resourceGroup'` already set in `main.bicep` is correct.
- This PRD extends `PRD-azure-infrastructure-deployment` (GitHub issue linked from that PRD); it does not supersede it.
