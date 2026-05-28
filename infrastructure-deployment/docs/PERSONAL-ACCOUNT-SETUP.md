# Personal Azure Account Setup — `daonhan` Sandbox

Step-by-step prerequisites for deploying the AKS sandbox (PRD [#305](https://github.com/daonhan/Microservices-in-.NET/issues/305)) using a personal Microsoft / Azure account. Run this **once** before working on any of the implementation phases ([#306–#311](https://github.com/daonhan/Microservices-in-.NET/issues/306)).

The sections run top-to-bottom. Each step's later commands assume the earlier ones completed — e.g. §2 needs the subscription selected in §1; §6's role assignment uses `$acrName` from the create call in the same section. If a snippet references a variable, it was set earlier in the same shell.

---

## 0. Local tooling — install first

All snippets below are PowerShell 7+ on Windows. Install these once, in this order:

| Tool | Why | Install |
|---|---|---|
| **PowerShell 7+** (`pwsh`) | Backtick line-continuation + `-o tsv` parsing assumed. | `winget install Microsoft.PowerShell --silent` |
| **Azure CLI** (`az`) | Every step uses it. | `winget install Microsoft.AzureCLI --silent` |
| **Git** | Clone the repo if you haven't. | `winget install Git.Git --silent` |

Verify:

```powershell
pwsh --version       # 7.4 or newer
az version           # 2.60 or newer
git --version
```

Restart the terminal after install so PATH refreshes. Confirm you can sign in (don't close the browser tab until `az` reports success):

```powershell
az login
az account show --query "user.name" -o tsv     # expect: daonhan@<your-domain>
```

`kubectl`, `helm`, and `bicep` are installed via `az` in §11 — don't install them yet.

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

Poll until every provider reports `Registered`. The loop below blocks until the slowest one finishes:

```powershell
do {
    $pending = @($providers | Where-Object {
        (az provider show --namespace $_ --query registrationState -o tsv) -ne 'Registered'
    })
    if ($pending) {
        "Pending: $($pending -join ', ')"
        Start-Sleep -Seconds 15
    }
} while ($pending)
"All providers registered."
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

`Standard_B2ms` is the sandbox node SKU. Personal subscriptions sometimes ship with quota=0 for B-series in some regions. The B-series sits under the `standardBsFamily` quota bucket.

```powershell
az vm list-usage --location southeastasia `
    --query "[?name.value=='standardBsFamily'].{name:localName, current:currentValue, limit:limit}" `
    -o table
```

Need `limit >= 2` (one B2ms = 2 vCPUs). If `0`, request a quota increase via *Help + Support → New support request → Service & subscription limits → Compute-VM → Quota type: Standard BS Family vCPUs*. Usually approved in 1–4 hours for personal subs.

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

Sandbox reuses an existing ACR — per PRD design. Create once. Capture the name in `$acrName` so the role-assignment block below works without copy-paste:

```powershell
$acrName = "ecomsharedacr$(Get-Random -Maximum 9999)"
$acrName    # echo — write this down

az acr create `
    --name $acrName `
    --resource-group ecom-shared-rg `
    --sku Basic `
    --admin-enabled false
```

The name lands in two places later:
- `sandbox.bicepparam` → `param acrName = '<value>'` (or `--parameters acrName=<value>` at deploy time).
- The `SANDBOX_ACR_NAME` Azure DevOps pipeline variable for `sandbox-deploy.yml`.

ACR names are globally unique; the random suffix avoids collisions. If `$acrName` is lost later, recover it with `az acr list --resource-group ecom-shared-rg --query "[].name" -o tsv`.

Grant your account `AcrPush` so you can push images during initial seeding:

```powershell
$acrId = az acr show --name $acrName --query id -o tsv
$myObj = az ad signed-in-user show --query id -o tsv
az role assignment create --assignee $myObj --role AcrPush --scope $acrId
```

---

## 7. Azure DevOps organization

Sandbox ops pipelines (stop/start/deploy) live in Azure DevOps per CLAUDE.md rule "GitHub Actions is not used."

1. Sign in at https://dev.azure.com using the same Microsoft account.
2. Create an organization (e.g. `daonhan`) if you don't already have one. Free tier: 1 parallel Microsoft-hosted job, 1800 build minutes/month.

   > **⚠ Request the free parallel-jobs grant.** Azure DevOps organizations created since 2021 ship with **0 parallel jobs by default** — pipelines will sit forever in `Queued` until you request the free grant. Fill the form once per organization at https://aka.ms/azpipelines-parallelism-request (response 2–3 business days). Do this *now* so the wait overlaps with the rest of setup. Existing orgs that already had jobs running are unaffected.

3. Create a project (e.g. `Microservices-in-NET-Sandbox`).
4. Connect to GitHub. Two options — App is recommended.

### 7a. Option A — Azure Pipelines GitHub App (recommended)

No token expiry, per-repo access revocable from GitHub, webhooks auto-managed.

1. Open https://github.com/apps/azure-pipelines → **Install** (or **Configure** if already installed).
2. Pick the GitHub account (`daonhan`).
3. **Repository access → Only select repositories** → tick `daonhan/Microservices-in-.NET` → **Install**.
4. GitHub redirects to Azure DevOps. Pick the ADO organization + project (`Microservices-in-NET-Sandbox`) → **Continue**.
5. Service connection is auto-created under *Project settings → Pipelines → Service connections* (type `GitHub`).
6. ADO drops you into the **New Pipeline → Configure** wizard with a template picker ("Deploy to Azure Kubernetes Service", "Build and push to ACR", etc.). **Close the tab / back out.** The service connection already exists — you do not need a pipeline at this step. Do **not** pick the AKS templates here: they require the Azure ARM service connection + AKS cluster from section 8 and Phase 3.
7. Verify: *Project settings → Pipelines → Service connections* shows a `GitHub` entry. Done.

### 7b. Option B — Manual service connection (OAuth or PAT)

Use when corporate policy blocks the GitHub App, or you want explicit PAT scoping.

1. ADO project → **gear icon** (bottom-left) → **Project settings**.
2. Left nav → **Pipelines** group → **Service connections**.
3. **New service connection** (top-right) → search **GitHub** → **Next**.
4. **Authentication method**:
   - **Grant authorization** — OAuth flow (was labelled "OAuth" in older UI).
   - **Personal Access Token** — paste a GitHub PAT with scopes `repo`, `admin:repo_hook`, `read:user`.
5. Pick **Grant authorization** → click **Authorize AzurePipelines** → consent on GitHub.
6. **Service connection name**: `github-daonhan` (this string lands in pipeline YAML `endpoint:` field).
7. Tick **Grant access permission to all pipelines** → **Save**.

### Verify

ADO → **Pipelines → New pipeline → GitHub** → `daonhan/Microservices-in-.NET` appears in the repo list. Cancel out; no pipeline needed yet.

---

## 8. Azure DevOps → Azure subscription service connection

Required for the ops pipelines (`sandbox-stop.yml`, `sandbox-start.yml`, `sandbox-deploy.yml`) to run `az aks stop/start`, pull from ACR, and `kubectl apply`.

### 8a. Create the service connection (Workload Identity Federation)

No client secret to rotate — ADO mints federated tokens against Azure AD.

1. Azure DevOps project → *Project settings → Service connections → **New service connection***.
2. Pick **Azure Resource Manager** → **Next**.
3. Identity type: **App registration or managed identity (automatic)** with **Workload Identity federation**.
4. Scope: **Subscription** → pick your subscription → resource group `ecom-sandbox-rg`.
5. Service connection name: **`azure-sandbox`** — exact spelling matters; it lands in pipeline YAML as `azureSubscription:` and matches the `SANDBOX_AZURE_SERVICE_CONNECTION` variable in [`sandbox-deploy.yml`](../pipelines/ops/sandbox-deploy.yml).
6. Tick **Grant access permission to all pipelines** → **Save**.

### 8b. Find the service-principal object ID

ADO auto-creates a federated identity behind the scenes. `az role assignment create` needs its object ID, which is **not** visible in ADO's UI — fetch it from Azure AD:

1. Open the service connection you just created → top-right **Manage Service Principal** link. Opens the Azure portal on the matching App Registration.
2. From the App Registration overview, click the **Managed application in local directory** link (the value next to it, not the App Registration name). That opens the **Enterprise Application** page.
3. Copy the **Object ID** shown on the Enterprise Application overview. (Do **not** use the App Registration's own Object ID — Azure RBAC wants the enterprise app principal.)

Stash it in the shell for the next block:

```powershell
$spObj = "<paste-enterprise-app-object-id-here>"
```

### 8c. Assign the three required roles

`Contributor` on the sandbox RG, `AcrPull` on the shared ACR, and `Azure Kubernetes Service Cluster User Role` on the AKS cluster. The first two run now; the AKS one runs **after Phase 3** has deployed the cluster.

```powershell
# Now — RG and ACR already exist from §5 and §6.
$rgId  = az group show --name ecom-sandbox-rg --query id -o tsv
$acrId = az acr show   --name $acrName --resource-group ecom-shared-rg --query id -o tsv

az role assignment create --assignee $spObj --role "Contributor" --scope $rgId
az role assignment create --assignee $spObj --role "AcrPull"     --scope $acrId

# After Phase 3 deploys AKS — skip this command until then.
$aksId = az aks show --name ecom-sandbox-aks --resource-group ecom-sandbox-rg --query id -o tsv
az role assignment create --assignee $spObj `
    --role "Azure Kubernetes Service Cluster User Role" `
    --scope $aksId
```

If `$acrName` is empty (new shell), repopulate from §6: `$acrName = az acr list --resource-group ecom-shared-rg --query "[0].name" -o tsv`.

---

## 9. Budget contact email

The `modules/budget.bicep` module emails an alert at 80% forecast.

Set the email in [`sandbox.bicepparam`](../bicep/parameters/sandbox.bicepparam) **before the first deploy** — the param file is read only at deploy time, editing it does not push to Azure on its own:

```bicep
param budgetContactEmails = ['your-email@example.com']
```

To change the recipient after Phase 3, edit the param file then re-run `az deployment group create --resource-group ecom-sandbox-rg --parameters infrastructure-deployment/bicep/parameters/sandbox.bicepparam`. See [SANDBOX.md §4](SANDBOX.md#updating-the-contact-email) for the full update flow.

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

Local tools required to apply manifests after AKS deploy. The first two snippets depend on `az` from §0 — they extend it with sub-tools.

```powershell
az aks install-cli                                # installs kubectl + kubelogin via Azure CLI
az bicep install                                  # installs bicep CLI under ~/.azure/bin
winget install Kubernetes.kubectl --silent        # fallback if `az aks install-cli` fails behind a proxy
winget install Helm.Helm --silent                 # Helm — needed by Phase 4 Nginx ingress controller
```

`az aks install-cli` drops `kubectl` + `kubelogin` into `%USERPROFILE%\.azure-kubectl\` and `%USERPROFILE%\.azure-kubelogin\` on Windows and prints a PATH-export hint — follow it (or open a fresh terminal so PATH refreshes) before running `kubectl`.

Verify all four:

```powershell
kubectl version --client --output yaml
kubelogin --version
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

- [ ] PowerShell 7+ and Azure CLI installed (`pwsh --version`, `az version`)
- [ ] `az account show` reports the expected subscription
- [ ] All 11 resource providers registered (poll loop in §2 exits cleanly)
- [ ] `standardBsFamily` quota >= 2 in target region (enough for one `Standard_B2ms` node)
- [ ] `ecom-shared-rg` + `ecom-sandbox-rg` created
- [ ] Shared ACR created in `ecom-shared-rg`, name recorded for `sandbox.bicepparam` + `SANDBOX_ACR_NAME`
- [ ] `AcrPush` role assigned to your user
- [ ] Azure DevOps org + project created
- [ ] **Free parallel-jobs grant requested** (Microsoft confirmation email received)
- [ ] GitHub service connection created (`daonhan/Microservices-in-.NET`)
- [ ] Azure service connection created (`azure-sandbox`, workload identity federation)
- [ ] Service connection has `Contributor` on `ecom-sandbox-rg` + `AcrPull` on shared ACR (AKS Cluster User deferred to after Phase 3)
- [ ] Budget contact email confirmed in `sandbox.bicepparam` (`your-email@example.com` replaced)
- [ ] `kubectl` + `kubelogin` + `bicep` + `helm` installed locally and all four `--version` checks pass

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
