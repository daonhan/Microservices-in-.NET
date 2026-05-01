# Tech Stack — Azure Cloud Deployment

> Inventory of every Azure service used by the cloud deployment, why it is
> there, and how the application talks to it. For the application's
> internal stack (.NET, EF Core, RabbitMQ, OpenTelemetry) see the root
> [`README.md`](../../README.md).

## Compute & runtime

| Service                              | Purpose                                                                                  | Configuration entry point                                                                 |
|--------------------------------------|------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------|
| **Azure Kubernetes Service (AKS)**   | Managed Kubernetes hosting all 8 microservices and the Nginx Ingress (in Prod).         | [`bicep/modules/aks.bicep`](../bicep/modules/aks.bicep), per-env manifests in [`kube/`](../kube/) |
| **Azure Container Registry (ACR)**   | Private image registry. AKS attached via managed identity for image pulls.               | [`bicep/modules/acr.bicep`](../bicep/modules/acr.bicep), [`acr-pull-role.bicep`](../bicep/modules/acr-pull-role.bicep) |
| **Azure Virtual Network (VNet)**     | Single VNet per environment. Subnets for AKS nodes and (future) private endpoints.       | [`bicep/modules/vnet.bicep`](../bicep/modules/vnet.bicep)                                  |

## Data & state

| Service                              | Purpose                                                                                  | Used by                                                                                    |
|--------------------------------------|------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------|
| **Azure SQL Database**               | Managed SQL Server. One database per service that needs relational state.                | Auth, Order, Product, Inventory, Shipping, Payment. EF Core via `ConnectionStrings__Default`. |
| **Azure Cache for Redis**            | Managed Redis. SSL-enabled connection string with key-based auth.                        | Basket (primary store), Order (price cache).                                               |
| **Azure Key Vault**                  | Long-lived secret storage (e.g. SQL admin passwords, signing keys).                      | Pipeline variable group sourcing; future direct integration via Workload Identity.          |

## Messaging

| Service                              | Purpose                                                                                  | Switch                                                                                     |
|--------------------------------------|------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------|
| **Azure Service Bus (topics)**       | Managed pub/sub. Topics map 1:1 to integration event types.                              | Selected via `Messaging__Provider=AzureServiceBus`. Implementation: `AzureServiceBusEventBus` in `ECommerce.Shared`. |
| **RabbitMQ (in-cluster)**            | Default messaging in Dev / local. Fanout exchange `ecommerce-exchange`.                  | Selected via `Messaging__Provider=RabbitMq` (default). Manifests in [`kube/aks-dev-rabbitmq.yml`](../kube/aks-dev-rabbitmq.yml), [`aks-staging-rabbitmq.yml`](../kube/aks-staging-rabbitmq.yml). |

Both providers expose the same `IEventBus`, so handlers and event types
do not change when switching providers.

## Observability

| Service                              | Purpose                                                                                  | Configuration                                                                              |
|--------------------------------------|------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------|
| **Application Insights**             | Distributed tracing, metrics, logs. Backed by Log Analytics.                             | `OpenTelemetry__Exporter=AzureMonitor`, `APPLICATIONINSIGHTS_CONNECTION_STRING`. Implementation in `ECommerce.Shared` via `Azure.Monitor.OpenTelemetry.Exporter`. |
| **Log Analytics Workspace**          | Central log store. Receives AKS diagnostics + App Insights data.                         | [`bicep/modules/monitor.bicep`](../bicep/modules/monitor.bicep)                            |
| **Local Jaeger / Prometheus / Loki / Grafana** | Same OpenTelemetry signals but routed to the local OTel Collector for Docker Compose & local K8s. | `OpenTelemetry__Exporter=Otlp` (default). Manifests under [`kubernetes/`](../../kubernetes/). |

## CI/CD

| Service                              | Purpose                                                                                  | Notes                                                                                       |
|--------------------------------------|------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------|
| **Azure Pipelines**                  | Build, test, package, deploy. One YAML per microservice; shared templates.               | Microsoft-hosted `ubuntu-latest` agents. See [Devops Agent Setup.md](Devops%20Agent%20Setup.md) for self-hosted migration. |
| **Azure DevOps Environments**        | Hosts approval gates and per-env service connections.                                    | Environments: `ecommerce-dev`, `ecommerce-staging`, `ecommerce-prod`.                       |
| **GitHub**                           | Source of truth. Connected to Azure Pipelines via service connection.                    | No mirroring to Azure Repos.                                                                |

## Networking & ingress

| Service                              | Purpose                                                                                  |
|--------------------------------------|------------------------------------------------------------------------------------------|
| **Nginx Ingress Controller (AKS)**   | Production ingress. Routes `/api(/|$)(.*)` to `apigateway-clusterip-service:8080`.       |
| **Azure DNS / custom domain**        | Out of scope (see PRD). Production hostname is configured at the Ingress.               |
| **Azure Firewall / Network Policies**| Out of scope; standard NSG rules from VNet module.                                       |

## Application-layer integrations

These are application-side adapters that talk to the Azure services
above. They live in `shared-libs/ECommerce.Shared`:

| Adapter                               | Replaces / extends                                  |
|---------------------------------------|-----------------------------------------------------|
| `AzureServiceBusEventBus`             | `RabbitMqEventBus` (when provider = `AzureServiceBus`) |
| `AzureServiceBusHostedService`        | `RabbitMqHostedService`                              |
| `AzureServiceBusTelemetry`            | `RabbitMqTelemetry` (OTEL context propagation)       |
| `Azure.Monitor.OpenTelemetry.Exporter` | OTLP exporter (when `OpenTelemetry__Exporter=AzureMonitor`) |

## Versions & SDKs

| Component        | Version                                                         |
|------------------|------------------------------------------------------------------|
| .NET runtime     | net10.0 (matches `Directory.Build.props`)                       |
| ASP.NET Core     | 10.0 Minimal APIs                                                |
| EF Core          | matches .NET 10                                                  |
| Bicep            | latest stable (`az bicep upgrade`)                              |
| AKS Kubernetes   | 1.29+ (set per Bicep parameter)                                 |
| ECommerce.Shared | 2.x (consumed as NuGet from `local-nuget-packages/`)            |

## What is intentionally **not** in the cloud stack

- Cosmos DB (deferred — SQL covers current needs)
- Azure Front Door / API Management (single Ingress is sufficient)
- KEDA (HPA is sufficient at current scale)
- Self-hosted agents (deferred — see [Devops Agent Setup.md](Devops%20Agent%20Setup.md))
- ArgoCD / Flux (pipeline-driven `kubectl apply` is sufficient)
