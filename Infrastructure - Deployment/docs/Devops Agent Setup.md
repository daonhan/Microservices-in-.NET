# DevOps Agent Setup — Migration Guide

> Audience: operators who manage the Azure Pipelines agent split: Dev uses
> Microsoft-hosted agents, while Staging and Production deploy from
> **self-hosted agents** running inside (or near) the AKS VNet.
>
> The build stage and Dev deployments run on Microsoft-hosted
> `ubuntu-latest` agents. Self-hosted agents are required for Staging and
> Production when:
>
> - The AKS API server is set to **private cluster** mode
> - SQL / Redis / Service Bus are switched to **private endpoints only**
> - Pipelines need outbound IPs that fall inside an enterprise allow-list
> - Build performance demands persistent NuGet / Docker layer caches

This is both the target operating model and the migration guide for the
self-hosted pool used by Staging and Production.

## Agent split

| Stage / environment | Agent type | Pool |
|---------------------|------------|------|
| Build               | Microsoft-hosted | `ubuntu-latest` |
| Dev deploy          | Microsoft-hosted | `ubuntu-latest` |
| Staging deploy      | Self-hosted | `ecommerce-self-hosted` (recommended name) |
| Production deploy   | Self-hosted | `ecommerce-self-hosted` (recommended name) |

Use Microsoft-hosted agents for fast Dev feedback. Use self-hosted agents
for Staging and Production so deployments can reach private AKS API
servers, private endpoints, and any network-restricted Azure resources.

## Decision: where to host

| Option                      | Pros                                                              | Cons                                                              |
|-----------------------------|-------------------------------------------------------------------|-------------------------------------------------------------------|
| **VM Scale Set (VMSS)**     | Native Azure DevOps support (Elastic Pool). Auto-grow/shrink.     | You manage the image. Slowest cold start.                         |
| **Single VM**               | Simplest. Persistent caches.                                      | Single point of failure. Manual capacity.                         |
| **AKS pod (KEDA-scaled)**   | Reuses the existing cluster. KEDA scales 0→N on queue depth.      | Extra moving parts. Pods need Docker-in-Docker or Kaniko.         |
| **Azure Container Apps**    | Serverless, scale-to-zero.                                        | Newer agent integration; image-build still needs Docker support.  |

**Recommended starting point**: a small **VMSS Elastic Pool** with
`Standard_D4s_v5` nodes attached to the AKS VNet. It is the lowest-risk
move: no cluster changes, no Docker-in-Docker.

## Networking

The agent must reach:

- **GitHub** (`https://github.com`) — pull source
- **Azure DevOps** (`*.dev.azure.com`, `*.visualstudio.com`) — pipeline coordination, log upload, NuGet feeds
- **ACR** (`<acr>.azurecr.io`) — push images
- **AKS API server** — `kubectl apply`. If the cluster is private, this
  must be the **private endpoint**, which is the entire reason for
  self-hosting.
- **NuGet** (`api.nuget.org`) and any private feeds

Place the agent VMSS in a **dedicated subnet** of the same VNet as AKS
(or a peered VNet). Allow outbound HTTPS to GitHub, Azure DevOps, ACR,
NuGet. Inbound: no public IP needed — agents poll Azure DevOps over HTTPS.

## Image baseline

The agent image must include:

- Ubuntu 22.04+
- .NET SDK 10.0.x (`dotnet-sdk-10.0`)
- Docker engine + buildx
- `kubectl` (matching AKS version)
- `az` CLI + `az aks` extension
- `Azure.DevOps.Pipelines.Agent` (the agent itself)
- Git, jq, yq, gettext (for `envsubst`)
- Husky / `dotnet-husky` (only if you want pre-commit checks to run)

Two reasonable approaches:

1. **Pre-baked image** — build a custom Azure VM image with Packer.
   Fastest startup. Lock in tool versions.
2. **Init script** — start from `Ubuntu 22.04 LTS` and run a
   provisioning script on first boot. Slower startup; easier to update.

## Agent registration

For each agent, register against an **agent pool** (e.g.
`ecommerce-self-hosted`):

```bash
# As an Azure DevOps PAT-authenticated user
./config.sh --unattended \
  --url https://dev.azure.com/<org> \
  --auth pat --token "$AZP_TOKEN" \
  --pool ecommerce-self-hosted \
  --agent "$(hostname)" \
  --acceptTeeEula \
  --replace

sudo ./svc.sh install
sudo ./svc.sh start
```

For VMSS Elastic Pools the registration is handled automatically by
Azure DevOps after you connect the pool to the scale set.

## Pipeline switch

The pipeline should keep Build and Dev on Microsoft-hosted agents and run
Staging/Production deploy jobs on the self-hosted pool:

```yaml
# Build and Dev deploy
pool:
  vmImage: ubuntu-latest

# Staging and Production deploy
pool:
  name: ecommerce-self-hosted
  demands:
    - Agent.OS -equals Linux
```

Both forms are interchangeable in Azure Pipelines, but they are used for
different environments here. The shared
[`build-stage.yml`](../pipelines/templates/build-stage.yml) should remain
on `vmImage: ubuntu-latest`. The deploy template should expose a pool
parameter so Dev can keep `ubuntu-latest` while Staging and Production
pass the self-hosted pool:

```yaml
parameters:
  - name: agentPool
    type: object
    default:
      vmImage: ubuntu-latest

stages:
  - stage: Build
    pool: ${{ parameters.agentPool }}
```

Each per-service pipeline then passes the Microsoft-hosted form for Dev
and `{ name: ecommerce-self-hosted }` for Staging and Production.

## AKS access

For private-cluster `kubectl apply`:

1. Create an **Azure DevOps service connection** of type
   *Azure Resource Manager* with **Workload Identity Federation** (or a
   Managed Identity attached to the agent VMSS).
2. Grant the identity `Azure Kubernetes Service Cluster User Role` on
   the AKS resource and `Azure Kubernetes Service RBAC Writer` (or
   tighter) inside the cluster.
3. The deploy stage already uses `azureSubscription` — keep that name
   stable when swapping connections.

## Caching

Microsoft-hosted agents are stateless. Self-hosted agents can keep
durable caches that significantly speed up builds:

- **NuGet** — set `NUGET_PACKAGES=$AGENT_TOOLSDIRECTORY/nuget` and let
  it persist between jobs. `dotnet restore` reuses it.
- **Docker layer cache** — ensure the local Docker daemon survives
  between jobs (it does, on a persistent VMSS). Use `--cache-from` /
  `--cache-to` or buildx with a registry-backed cache for cross-agent
  reuse.
- **Workspace cleanup** — `clean: false` on `checkout` to keep the
  cloned repo across runs. Add periodic janitor jobs to prune `bin/`
  and `obj/` if disks fill.

## Observability for the agents

Send agent host metrics to the same Log Analytics Workspace that backs
Application Insights:

- Install the **Azure Monitor Agent** on the VMSS image
- Forward `syslog`, `dmesg`, and Docker daemon logs
- Add a Grafana / Application Insights workbook for agent CPU, queue
  wait time, and job duration

## Rollback

The migration is a per-environment switch. To roll back Staging or
Production temporarily, set that deploy stage's `pool` back to
`vmImage: ubuntu-latest` if the target AKS API server is publicly
reachable. Dev and Build already use Microsoft-hosted agents. The agent
pool can keep running idle — Azure DevOps does not bill for unused
self-hosted minutes (only the underlying VMSS).

## Cost notes

- Microsoft-hosted: free monthly minutes per parallel job, then
  consumption-billed. No infra to manage.
- Self-hosted on VMSS: pay for VMs (and disks / outbound traffic).
  Cheaper at high pipeline volume; more expensive at low volume.
- KEDA-on-AKS: cheapest at high volume, most operational complexity.

## When to revisit

Re-run this decision when any of these change:

- AKS goes private
- Build volume exceeds free Microsoft-hosted minutes consistently
- Compliance requires builds in a dedicated network
- A team standardizes on internal base images that aren't on the
  Microsoft-hosted agents
