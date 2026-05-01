# Bicep IaC — VNet, AKS & ACR

This directory holds the foundational Azure infrastructure for the e-commerce microservices platform: a Virtual Network, Azure Container Registry (ACR), and an Azure Kubernetes Service (AKS) cluster wired to ACR for image pulls.

Slice 2 of the [Azure Infrastructure & Deployment plan](../../docs/plans/azure-infrastructure-deployment-plan.md). Slice 3 (SQL, Redis, Key Vault, Monitor, Service Bus) extends these modules.

## Layout

```
bicep/
├── main.bicep                  # Orchestration (resource-group scope)
├── parameters/
│   ├── dev.bicepparam
│   ├── staging.bicepparam
│   └── prod.bicepparam
└── modules/
    ├── vnet.bicep              # VNet + 3 subnets (aks, private-endpoints, agents)
    ├── acr.bicep               # Azure Container Registry
    ├── aks.bicep               # AKS cluster (system pool, system-assigned identity)
    └── acr-pull-role.bicep     # AcrPull role assignment on ACR for the AKS kubelet identity
```

## Prerequisites

- Azure CLI (`az`) with the `bicep` extension (`az bicep install`).
- An Azure subscription and a target resource group (one per environment is recommended).
- Permissions to create role assignments (the AcrPull grant on the ACR).

## Deploy

```bash
# 1. Pick an environment and a resource group
ENV=dev
RG=rg-ecommerce-${ENV}-eastus
LOCATION=eastus

# 2. Create the resource group (idempotent)
az group create --name "$RG" --location "$LOCATION"

# 3. Validate (preview the diff)
az deployment group what-if \
  --resource-group "$RG" \
  --template-file ./main.bicep \
  --parameters ./parameters/${ENV}.bicepparam

# 4. Deploy
az deployment group create \
  --resource-group "$RG" \
  --template-file ./main.bicep \
  --parameters ./parameters/${ENV}.bicepparam
```

The same three-line command pattern works for `staging` and `prod` by changing `ENV`.

## Notes

- **ACR names are globally unique.** Override `acrName` on the command line if the default in the bicepparam file is taken: `--parameters acrName=mycustomacr1234`.
- **AKS uses a system-assigned managed identity** and the kubelet identity is granted `AcrPull` on the ACR. No admin credentials, no image-pull secrets needed for nodes.
- **Network plugin defaults to Azure CNI.** The AKS subnet has service endpoints for SQL, Storage and Key Vault to support Slice 3.
- **Service CIDR is non-overlapping** with the VNet. Each environment's `serviceCidr` is on a different `10.x.0.0/16`.
- **`kubernetesVersion` is empty** so AKS picks the regional default. Pin a version per-env when stability is required.
- **Sandbox limitation:** `az bicep build` and `what-if` were not run locally — the sandbox firewall blocks Azure endpoints. CI or a workstation with `az` will validate.
