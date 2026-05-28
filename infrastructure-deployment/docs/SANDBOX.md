# Sandbox Runbook

> **Audience:** the operator running the AKS sandbox on a personal Azure
> account for learning and demo purposes.
>
> **Prerequisite:** one-time setup is documented separately in
> [PERSONAL-ACCOUNT-SETUP.md](PERSONAL-ACCOUNT-SETUP.md). This file is
> day-2 operations: cost, schedule, deploy, cold-start, teardown.

---

## 1. Overview

The sandbox environment runs all nine microservices end-to-end on a
single-node AKS cluster for the purpose of **learning and demonstrating
the platform**. It is not production-grade — there is no HA, no DR, no
private networking, and no TLS termination at the ingress.

| Goal                | Value                          |
|---------------------|--------------------------------|
| Cost target         | ~$80 / month                   |
| Hard cap            | $100 / month (budget enforced) |
| Compute schedule    | Weekday 08:00–22:00 UTC only   |
| Cluster shape       | 1 × `Standard_B2ms` (AKS Free) |
| Resource group      | `ecom-sandbox-rg`              |

The cost target assumes the stop/start schedule below is respected and
the cluster is idle outside of weekday working hours. Sustained 24/7 use
exceeds the budget.

---

## 2. Cost Breakdown

Monthly estimate at default sandbox SKUs with the weekday-only schedule
applied. Numbers are list-price guidance for `southeastasia`; exact bills
vary by region, currency, and activity.

| Line item                          | SKU / config                            | Est. $/month |
|------------------------------------|-----------------------------------------|--------------|
| AKS control plane                  | Free tier                               | $0           |
| AKS node                           | 1 × `Standard_B2ms` × ~215 hr/month     | ~$18         |
| SQL Serverless (7 databases)       | `GP_S_Gen5_1`, min 0.5 vCore, auto-pause 60 min | ~$15  |
| Redis                              | Basic C0                                | ~$16         |
| Service Bus                        | Standard namespace                      | ~$10         |
| Load Balancer + public IP          | 1 standard LB, 1 static public IP       | ~$5          |
| Log Analytics                      | Pay-as-you-go, **1 GB/day** ingest cap  | ~$3          |
| Application Insights               | 10% sampling                            | ~$2          |
| ACR (shared, in `ecom-shared-rg`)  | Basic, cost not charged to this RG      | $0           |
| **Total (target)**                 |                                         | **~$69–$80** |
| **Hard cap (budget alert)**        | Forecasted threshold at 80%             | **$100**     |

Notes:

- The PRD targets a 0.1 GB/day Log Analytics cap. The
  `workspaceCapping.dailyQuotaGb` API takes an **integer**, so the
  smallest practical value wired in `sandbox.bicepparam` is `logDailyCapGb
  = 1`. Sub-GB caps need a future Bicep type change.
- AKS node hours assume nightly stop (22:00 UTC daily) + weekends off
  (Saturday + Sunday with no auto-start). Sustained 24/7 use is ~$60/month
  for the node alone.
- SQL Serverless cost is highly activity-dependent. Idle databases approach
  zero compute; the figure above assumes light interactive demo use.

---

## 3. Start / Stop Schedule

Stop and start are driven by two Azure Pipelines under
[`pipelines/ops/`](../pipelines/ops). Both use `az aks stop` / `az aks
start` so the underlying VMSS is fully deallocated — `kubectl scale` would
leave the node billing.

| Pipeline                                        | Cron (UTC)    | When                              | Action                     |
|-------------------------------------------------|---------------|-----------------------------------|----------------------------|
| [`sandbox-stop.yml`](../pipelines/ops/sandbox-stop.yml)   | `0 22 * * *`  | Every day at 22:00                | `az aks stop`              |
| [`sandbox-start.yml`](../pipelines/ops/sandbox-start.yml) | `0 8 * * 1-5` | Weekdays (Mon–Fri) at 08:00       | `az aks start`             |

**Weekend use is manual.** The start pipeline intentionally omits Saturday
and Sunday from its cron. To use the sandbox on a weekend, queue
`sandbox-start.yml` manually from Azure DevOps:

1. Open the pipeline in Azure DevOps.
2. Click **Run pipeline** → **Run**.
3. Wait ~5 minutes for `az aks start` to reallocate the VMSS.
4. The 22:00 UTC stop pipeline will still fire that night.

A stopped cluster takes ~5 minutes to come back. Pods then need another
~60 seconds for readiness probes (see §6).

---

## 4. Budget Alert Wiring

The $100/month hard cap is enforced by
[`modules/budget.bicep`](../bicep/modules/budget.bicep), which wraps
`Microsoft.Consumption/budgets` at resource group scope.

- **Conditional**: `main.bicep` deploys the budget module only when
  `environment == 'sandbox'`. Dev/Staging/Prod environments are
  unaffected.
- **Trigger**: forecasted spend crossing 80% of the cap (`thresholdType:
  'Forecasted'`, `threshold: 80`). The alert fires before the hard cap is
  reached so you have time to react.
- **Reset**: monthly (`timeGrain: 'Monthly'`).

### Where to find the budget in the portal

```
Cost Management + Billing → Cost Management → Scope: ecom-sandbox-rg →
  Budgets → ecom-sandbox-budget
```

The budget shows month-to-date spend vs. forecast, threshold history, and
notification recipients.

### Updating the contact email

The notification recipients are stored in
[`parameters/sandbox.bicepparam`](../bicep/parameters/sandbox.bicepparam):

```bicep
param budgetContactEmails = ['daonhan@gmail.com']
```

To change the recipient:

1. Edit `budgetContactEmails` in `sandbox.bicepparam`.
2. Redeploy: `az deployment group create --resource-group ecom-sandbox-rg
   --parameters Infrastructure\ -\ Deployment/bicep/parameters/sandbox.bicepparam`.
3. Confirm in the portal that the budget shows the new email under
   *Alert recipients*.

Multiple emails are allowed; the parameter is a string array.

---

## 5. Manual Deploy Pipeline Steps

Image rollouts are manual to avoid fighting the nightly stop. See
[`pipelines/ops/sandbox-deploy.yml`](../pipelines/ops/sandbox-deploy.yml).

### Required pipeline variables

These come from a variable group attached to the pipeline at run time. No
secrets or subscription IDs live in the YAML.

| Variable                              | Purpose                                                  |
|---------------------------------------|----------------------------------------------------------|
| `SANDBOX_AZURE_SERVICE_CONNECTION`    | ARM service connection (workload identity federation).   |
| `SANDBOX_RG`                          | Resource group (`ecom-sandbox-rg`).                      |
| `SANDBOX_AKS_NAME`                    | AKS cluster name (e.g. `ecom-sandbox-aks`).              |
| `SANDBOX_ACR_NAME`                    | ACR hostname prefix (e.g. `ecomsharedacr1234`).          |
| `SANDBOX_NAMESPACE`                   | Kubernetes namespace (`ecommerce-sandbox`).              |

### Steps

1. Open **Pipelines → sandbox-deploy** in Azure DevOps.
2. Click **Run pipeline**.
3. Supply the **Image tag to roll across all nine services** input — the
   tag pushed to ACR by the per-service build pipelines (e.g.
   `2026.05.28-1`, `sha-d503ac6`).
4. Click **Run**. The pipeline:
   - Installs `kubectl` + `kubelogin` if absent on the hosted agent.
   - Runs `az aks get-credentials` and `kubelogin convert-kubeconfig
     -l azurecli` to authenticate against the cluster.
   - Iterates the nine `deployment=container=image` triples and runs
     `kubectl set image` for each.
   - Waits for `kubectl rollout status` on each Deployment with a
     5-minute timeout.

If the cluster is currently stopped (overnight or weekend), queue
`sandbox-start.yml` first and wait ~5 minutes before triggering the deploy.

---

## 6. SQL Serverless Cold-Start Note

Sandbox SQL databases use the **General Purpose Serverless** tier
(`GP_S_Gen5_1`, `minCapacity = 0.5`, `autoPauseDelay = 60` minutes). When
a database has been idle for 60 minutes, Azure pauses it. The next
connection triggers a **resume** that takes 20–30 seconds.

Two mitigations make this transparent to the running services:

### Kubernetes readiness probe

Every sandbox manifest at [`kubernetes/aks-sandbox-*.yml`](../../kubernetes) uses:

```yaml
readinessProbe:
  httpGet:
    path: /health/ready
    port: 8080
  initialDelaySeconds: 60
  periodSeconds: 10
  failureThreshold: 6
```

`initialDelaySeconds: 60` gives the pod a full minute before its first
probe — enough for the SQL resume to settle on cluster start. Combined
with `periodSeconds: 10 × failureThreshold: 6`, the pod has up to
`60 + 60 = 120` seconds total before Kubernetes marks it unready and
restarts. That window comfortably covers the 20–30 second resume plus
EF Core warmup.

### EF Core transient retry

The connection-fault during a SQL resume surfaces to EF Core as a
transient SQL error. Each service's `DbContext` registration uses EF
Core's built-in execution strategy:

```csharp
options.UseSqlServer(connectionString,
    sql => sql.EnableRetryOnFailure());
```

`EnableRetryOnFailure()` retries with exponential backoff on transient
errors. No application code changes are required to handle the cold
start — the first request after resume may take a second or two longer
than usual, but it will succeed.

If you see pods crash-looping after a fresh `az aks start`, the most
likely cause is **probe path drift** (the service no longer exposes
`/health/ready`) rather than the resume latency itself. Verify with
`kubectl describe pod` and the service's logs.

---

## 7. Cleanup / Teardown

The sandbox is disposable. Wipe everything in the resource group:

```bash
az group delete --resource-group ecom-sandbox-rg --yes --no-wait
```

This removes AKS, SQL (all databases — **data is unrecoverable**), Redis,
Service Bus, Key Vault, App Insights, Log Analytics, the load balancer,
the public IP, and the budget resource. The shared ACR in
`ecom-shared-rg` is untouched.

To fully reset (also drops the shared ACR — only do this if you're done
with the project on this account):

```bash
az group delete --resource-group ecom-shared-rg --yes --no-wait
```

After teardown:

- Cost Management continues to show historical spend; that's expected.
- Re-running Bicep with `sandbox.bicepparam` recreates everything from a
  clean slate. No state is preserved between teardown and redeploy.
