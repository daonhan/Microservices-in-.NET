# Personal Azure Account Setup — `daonhan` Sandbox

Step-by-step prerequisites for deploying the AKS sandbox (PRD [#305](https://github.com/daonhan/Microservices-in-.NET/issues/305)) using a personal Microsoft / Azure account. Run this **once** before working on any of the implementation phases ([#306–#311](https://github.com/daonhan/Microservices-in-.NET/issues/306)).

---

## 1. Azure subscription

Pick one. All three work; cost differs.

| Option | Cost | Notes |
|---|---|---|
| **Free trial** | $200 credit, 30 days | One-time per Microsoft account. Hard cap — services disable when credit exhausted. |
| **Pay-As-You-Go** | Real money | Recommended for sandbox use beyond the 30-day trial. Pair with a budget alert. |
| **Visual Studio Subscription** | Monthly credit ($50–$150 depending on tier) | If you have an MSDN/VS subscription, activate the Azure benefit at https://my.visualstudio.com/Benefits. Credit auto-disables resources when exhausted. |

**Spending limit** — Subscription → *Manage spending limit* → leave **On** for trial/VS-sub subscriptions. Off only on PAYG.

**Sign in:**

```powershell
az login
az account list --output table
az account set --subscription "<SUBSCRIPTION_ID>"
```

Verify:

```powershell
az account show --query "{name:name, id:id, tenantId:tenantId, user:user.name}" -o table
```

Expected `user` field: `daonhan@<your-domain>`.

---

## 2. Register required resource providers

Azure providers must be registered on the subscription before bicep can deploy their resource types. New subscriptions have only a subset pre-registered.

```powershell
$providers = @(
    'Microsoft.ContainerService',     # AKS
    'Microsoft.ContainerRegistry',    # ACR
    'Microsoft.Sql',                  # Azure SQL
    'Microsoft.Cache',                # Redis
    'Microsoft.ServiceBus',           # ASB
    'Microsoft.OperationalInsights',  # Log Analytics
    'Microsoft.Insights',             # App Insights
    'Microsoft.KeyVault',             # Key Vault
    'Microsoft.Network',              # VNet / LB / PublicIP
    'Microsoft.Consumption',          # Budgets
    'Microsoft.ManagedIdentity'       # AKS workload identity
)
foreach ($p in $providers) { az provider register --namespace $p }
```

Wait until all are `Registered`:

```powershell
foreach ($p in $providers) {
    az provider show --namespace $p --query "{ns:namespace, state:registrationState}" -o tsv
}
```

Registration takes 1–5 minutes per provider. Idempotent — safe to re-run.

---

## 3. Pick a region

Sandbox runs in one region. Choose by cost + latency.

| Region | Notes |
|---|---|
| `southeastasia` (Singapore) | Lowest latency for VN. B-series + SQL serverless available. |
| `eastasia` (Hong Kong) | Alt for VN. Slightly higher cost. |
| `eastus` | Cheapest US region. Higher latency from VN. |
| `westeurope` | Good for EU. Pair with VS-sub credit. |

Default for `sandbox.bicepparam` will be `southeastasia`. Override per personal preference.

---

## 4. Quota check — B-series VMs

`Standard_B2ms` is the sandbox node SKU. Personal subscriptions sometimes ship with quota=0 for B-series in some regions.

```powershell
az vm list-usage --location southeastasia `
    --query "[?contains(name.value, 'BS') || contains(name.value, 'standardBSFamily')].{name:localName, current:currentValue, limit:limit}" `
    -o table
```

Need `limit >= 2`. If `0`, request quota increase via *Help + Support → New support request → Service & subscription limits → Compute-VM*. Usually approved in 1–4 hours.

---

## 5. Resource groups

Two groups recommended for the sandbox:

| Group | Purpose | Lifetime |
|---|---|---|
| `ecom-shared-rg` | Holds the ACR shared across all envs (sandbox + future dev/staging/prod). | Permanent. |
| `ecom-sandbox-rg` | Holds AKS, SQL, Redis, ASB, KV, monitor, app-insights, budget. | Disposable — `az group delete` to wipe sandbox. |

Create:

```powershell
$region = "southeastasia"
az group create --name "ecom-shared-rg"  --location $region
az group create --name "ecom-sandbox-rg" --location $region
```

---

## 6. Shared ACR (one-time)

Sandbox reuses an existing ACR — per PRD design. Create once:

```powershell
az acr create `
    --name "ecomsharedacr$(Get-Random -Maximum 9999)" `
    --resource-group ecom-shared-rg `
    --sku Basic `
    --admin-enabled false
```

Record the ACR name — it goes into `sandbox.bicepparam` as input. ACR names are globally unique; the suffix avoids collisions.

Grant your account `AcrPush` so you can push images during initial seeding:

```powershell
$acrId = az acr show --name <ACR_NAME> --query id -o tsv
$myObj = az ad signed-in-user show --query id -o tsv
az role assignment create --assignee $myObj --role AcrPush --scope $acrId
```

---

## 7. Azure DevOps organization

Sandbox ops pipelines (stop/start/deploy) live in Azure DevOps per CLAUDE.md rule "GitHub Actions is not used."

1. Sign in at https://dev.azure.com using the same Microsoft account.
2. Create an organization (e.g. `daonhan`) if you don't already have one. Free tier: 1 parallel Microsoft-hosted job, 1800 build minutes/month.
3. Create a project (e.g. `Microservices-in-NET-Sandbox`).
4. Connect to GitHub: *Project settings → Service connections → New → GitHub → OAuth → authorize `daonhan/Microservices-in-.NET`*.

---

## 8. Azure DevOps → Azure subscription service connection

Required for the ops pipelines to run `az aks stop/start` and `kubectl apply`.

**Use Workload Identity Federation (no secrets):**

1. Azure DevOps → *Project settings → Service connections → New → Azure Resource Manager*.
2. Authentication method: **Workload Identity federation (automatic)**.
3. Scope: Subscription → pick `ecom-sandbox-rg` resource group.
4. Service connection name: `azure-sandbox` (this name lands in the pipeline YAML).
5. Grant *Contributor* on `ecom-sandbox-rg` + *AcrPull* on the shared ACR + *Azure Kubernetes Service Cluster User* on the AKS cluster (last one after Phase 3 deploy).

```powershell
$rgId = az group show --name ecom-sandbox-rg --query id -o tsv
$spObj = "<service-principal-objectId-from-ADO-output>"
az role assignment create --assignee $spObj --role Contributor --scope $rgId
```

---

## 9. Budget contact email

The `modules/budget.bicep` module emails an alert at 80% forecast.

Set the email in `sandbox.bicepparam`:

```bicep
param budgetContactEmails = ['your-email@example.com']
```

Test once after Phase 3 deploy by temporarily lowering the cap and verifying the alert fires.

---

## 10. Cost Management access

Cost Management is free on every subscription. Pin it for daily checks.

- https://portal.azure.com/#view/Microsoft_Azure_CostManagement
- Set scope to `ecom-sandbox-rg`.
- Save view as *Sandbox daily cost*.
- Optional: enable daily email digest.

---

## 11. kubectl + helm + bicep CLI

Local tools required to apply manifests after AKS deploy.

```powershell
az aks install-cli                                # installs kubectl + kubelogin
az bicep install                                  # installs bicep CLI
winget install Kubernetes.kubectl --silent        # alt if above fails
winget install Helm.Helm --silent                 # for Nginx ingress controller in Phase 4
```

Verify:

```powershell
kubectl version --client --output yaml
bicep --version
helm version
```

After AKS is deployed (Phase 3 onward):

```powershell
az aks get-credentials --resource-group ecom-sandbox-rg --name ecom-sandbox-aks
kubectl config use-context ecom-sandbox-aks
```

---

## 12. Pre-flight checklist

Before kicking off Phase 1 (issue [#306](https://github.com/daonhan/Microservices-in-.NET/issues/306)):

- [ ] `az account show` reports the expected subscription
- [ ] All 11 resource providers registered
- [ ] `Standard_B2ms` quota >= 2 in target region
- [ ] `ecom-shared-rg` + `ecom-sandbox-rg` created
- [ ] Shared ACR created in `ecom-shared-rg`, name recorded
- [ ] AcrPush role assigned to your user
- [ ] Azure DevOps org + project created
- [ ] GitHub service connection created (`daonhan/Microservices-in-.NET`)
- [ ] Azure service connection created (`azure-sandbox`, workload identity federation)
- [ ] Service connection has Contributor on `ecom-sandbox-rg`
- [ ] Budget contact email confirmed (`your-email@example.com` replaced)
- [ ] kubectl + bicep + helm installed locally

---

## Teardown (when done)

Sandbox is disposable. To wipe everything (except shared ACR):

```powershell
az group delete --name ecom-sandbox-rg --yes --no-wait
```

Budget alerts auto-clear when the resource group is deleted. AKS, SQL, Redis, ASB all go with it. ACR survives in `ecom-shared-rg`.

To completely reset:

```powershell
az group delete --name ecom-shared-rg --yes --no-wait
```

Wipes the shared ACR too. Use only if you're done with the whole project on this account.
