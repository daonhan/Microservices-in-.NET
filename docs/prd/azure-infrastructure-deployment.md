# PRD: Azure Cloud Infrastructure & Deployment for Microservices Platform

> **Status**: Draft — Pending Review
> **Created**: 2026-04-29
> **Author**: Paul Nhan Nguyen Dao
> **Labels**: `enhancement`, `infrastructure`, `PRD`

---

## Problem Statement

The microservices platform currently runs only in a local development environment using Docker Compose. There is no cloud infrastructure, no CI/CD pipeline, no automated deployment, and no production-grade observability. The existing `kubernetes/` manifests are basic and not designed for multi-environment Azure Kubernetes Service (AKS) deployment.

As a developer, I want to deploy this microservices platform to **Microsoft Azure** using industry-standard cloud-native practices. This includes Azure Pipelines CI/CD, Azure Container Registry, AKS with multi-environment deployment (Dev, Staging, Production), managed Azure services, and comprehensive documentation.

The goal is both **practical deployment** and **learning** — to understand how infrastructure-as-code, CI/CD pipelines, Kubernetes deployments, and managed Azure services work together for a real microservices platform.

---

## Solution

Implement a complete Azure cloud infrastructure and deployment solution for the microservices platform, covering:

1. **Infrastructure-as-Code (Bicep)** — Provision all Azure resources declaratively
2. **Per-Service Azure Pipelines** — Independent CI/CD pipeline per microservice
3. **Multi-Environment AKS Deployment** — Dev, Staging, Production environments with per-environment Kubernetes manifests and secrets
4. **Azure Container Registry (ACR)** — Private container image storage with AKS integration
5. **Managed Azure Services** — Azure SQL Database (per-service), Azure Cache for Redis, Azure Service Bus (as an option alongside RabbitMQ), Azure Monitor / Application Insights
6. **Microsoft-Hosted Azure DevOps Agents** — Start with Microsoft-hosted agents for simplicity, with option to add self-hosted agents later if needed
7. **Application-Level Changes** — Configuration updates for Azure services, OpenTelemetry exporters for Azure Monitor, Azure Service Bus adapter for the event bus
8. **Documentation** — Architecture, system design, tech stack, DevOps setup docs

The approach supports a **layered learning path**:
- **Local App Dev**: Docker Compose (existing) for fast inner-loop development
- **Local K8s Practice**: Docker Desktop Kubernetes / Minikube for testing K8s manifests locally
- **Azure Cloud**: Full AKS deployment with CI/CD pipelines

---

## User Stories

### Infrastructure Provisioning (Bicep IaC)

1. As a developer, I want to provision an AKS cluster using Bicep, so that I have a managed Kubernetes environment for deploying microservices
2. As a developer, I want to provision an Azure Container Registry (ACR) using Bicep, so that I have a private registry for storing Docker images
3. As a developer, I want to provision Azure SQL Database instances (one per service that needs SQL Server) using Bicep, so that I have managed, auto-backed-up databases
4. As a developer, I want to provision Azure Cache for Redis using Bicep, so that Basket and Order services have a managed Redis instance
5. As a developer, I want to provision Azure Key Vault using Bicep, so that I have a secure place to store secrets (even if not used by the application directly in phase 1, it's provisioned for future use)
6. As a developer, I want to provision an Azure Virtual Network with subnets using Bicep, so that AKS, agents, and Azure services are network-isolated
7. As a developer, I want to provision Azure Monitor / Log Analytics Workspace using Bicep, so that I have centralized logging and monitoring
8. As a developer, I want to provision Application Insights resources using Bicep, so that I have distributed tracing and application performance monitoring
9. As a developer, I want to provision Azure Service Bus using Bicep, so that I have a managed messaging service as an alternative to RabbitMQ
10. As a developer, I want all Bicep templates to be parameterized by environment (Dev/Staging/Prod), so that I can deploy different configurations per environment from the same templates
11. As a developer, I want a single Bicep deployment command to provision all resources for an environment, so that setup is simple and repeatable

### Container Registry & Docker Images

12. As a developer, I want a production-ready multi-stage Dockerfile for each microservice, so that images are optimized (small size, no SDK in runtime image)
13. As a developer, I want each Dockerfile to build from the repository root (not the service directory), so that shared libraries and `Directory.Build.props` are available during build
14. As a developer, I want Docker images tagged with a combination of branch name and build number (for non-tag builds) or semantic version (for tag builds), so that images are uniquely identifiable
15. As a developer, I want Docker images pushed to ACR automatically as part of the CI/CD pipeline, so that I don't need to manually build and push images

### CI/CD Pipelines (Azure Pipelines)

16. As a developer, I want an independent Azure Pipeline YAML file per microservice, so that I can build and deploy each service independently
17. As a developer, I want the pipeline to trigger on PRs to any branch (for build+test), so that code quality is validated before merge
18. As a developer, I want the pipeline Build stage to include: NuGet restore, `dotnet format` (lint check), `dotnet build`, `dotnet test` (with code coverage), `dotnet publish`, Docker build, Docker push to ACR, so that the build is comprehensive
19. As a developer, I want code coverage reports (Cobertura format) published as pipeline artifacts, so that I can track test coverage over time
20. As a developer, I want a Dev deployment stage that triggers on `dev` branch or `deploy/*` branches, so that Dev environment deployments are automated
21. As a developer, I want a Staging deployment stage that triggers on `staging` branch, so that Staging environment deployments are automated
22. As a developer, I want a Production deployment stage that triggers on `prod` branch, so that Production deployments are automated
23. As a developer, I want the option to add manual approval gates before Staging and Production deployments, so that I can control when releases go to higher environments
24. As a developer, I want each deployment stage to create Kubernetes secrets (ACR pull secret, JWT keys, Application Insights connection string, service-specific secrets), so that pods have access to required secrets
25. As a developer, I want each deployment stage to deploy Kubernetes manifests with image substitution (replacing image tags with the newly built image), so that deployments use the correct image version
26. As a developer, I want pipeline variables to be used for environment-specific secrets (prefixed with `DEV_`, `STAGING_`, `PROD_`), so that secrets are managed in Azure DevOps and injected into Kubernetes

### AKS Kubernetes Manifests

27. As a developer, I want per-environment Kubernetes namespace manifests, so that each environment is isolated in its own namespace
28. As a developer, I want per-environment Kubernetes Deployment manifests for each microservice, with resource requests/limits, liveness probes, readiness probes, and environment variables sourced from secrets, so that services run reliably in AKS
29. As a developer, I want per-environment Kubernetes Service (ClusterIP) manifests for each microservice, so that services can communicate within the cluster
30. As a developer, I want HorizontalPodAutoscaler manifests for each microservice, so that services scale automatically based on CPU utilization
31. As a developer, I want an Ingress manifest (Nginx Ingress Controller) for production, with path-based routing to the API Gateway, so that external traffic reaches the platform
32. As a developer, I want Kubernetes manifests for infrastructure services (RabbitMQ — for environments where Azure Service Bus is not used), so that messaging works in all environments
33. As a developer, I want Kubernetes manifests to be organized under `Infrastructure - Deployment/kube/` with a clear naming convention (e.g., `aks-dev-{service}.yml`, `aks-staging-{service}.yml`, `aks-prod-{service}.yml`), so that manifests are easy to find and manage

### Azure Service Bus Integration (Optional Messaging Adapter)

34. As a developer, I want an `AzureServiceBusEventBus` implementation of `IEventBus` in `ECommerce.Shared`, so that services can publish/subscribe to events via Azure Service Bus
35. As a developer, I want an `AzureServiceBusHostedService` (similar to `RabbitMqHostedService`) that subscribes to Azure Service Bus topics, so that event handlers work with Azure Service Bus
36. As a developer, I want a configuration switch (e.g., `Messaging:Provider` with values `RabbitMq` or `AzureServiceBus`), so that I can choose the messaging provider per environment without code changes
37. As a developer, I want OpenTelemetry context to propagate through Azure Service Bus messages (similar to `RabbitMqTelemetry`), so that distributed tracing works across service boundaries
38. As a developer, I want the same `Event` base type and `IEventBus` interface to work with both RabbitMQ and Azure Service Bus, so that event handlers don't need to change when switching providers

### Application Configuration Changes

39. As a developer, I want connection strings for Azure SQL Database to be configurable via environment variables (already supported by `ConnectionStrings__Default`), so that services connect to Azure SQL in cloud environments
40. As a developer, I want Redis connection strings to support Azure Cache for Redis (with SSL, password, and port), so that Basket and Order services work with managed Redis
41. As a developer, I want OpenTelemetry to export traces, metrics, and logs to Azure Monitor / Application Insights (via the Azure Monitor OpenTelemetry exporter), so that observability works in Azure
42. As a developer, I want the OTLP exporter configuration to be switchable between local (Jaeger/OTel Collector) and Azure (Application Insights) via environment variables, so that the same code works in both environments
43. As a developer, I want health check endpoints (`/health/live` and `/health/ready`) to work correctly with AKS liveness and readiness probes, so that Kubernetes can manage pod lifecycle
44. As a developer, I want the API Gateway (YARP) to be configurable with cluster addresses that resolve within the AKS cluster (e.g., `http://{service}-clusterip-service:8080`), so that routing works in Kubernetes

### Azure DevOps Agents

45. As a developer, I want the pipeline to use Microsoft-hosted agents (e.g., `vmImage: 'ubuntu-latest'`), so that I can start building and deploying without managing agent infrastructure
46. As a developer, I want documentation for migrating to self-hosted agents in the future (when needed for private AKS cluster access), so that I have a clear upgrade path

### Local Kubernetes Practice

48. As a developer, I want documentation for enabling Kubernetes in Docker Desktop, so that I can practice K8s manifests locally
49. As a developer, I want the existing `kubernetes/` manifests to work with local Kubernetes (Docker Desktop / Minikube), so that I can test deployments before pushing to Azure
50. As a developer, I want a step-by-step guide for deploying the full stack to local Kubernetes, so that I can validate the entire platform locally

### Documentation

51. As a developer, I want an `OVERVIEW.md` document in `Infrastructure - Deployment/` that explains what the platform is, how it's deployed, and how environments are structured, so that newcomers can understand the deployment architecture
52. As a developer, I want an `ARCHITECTURE.md` document that describes the cloud architecture (AKS, ACR, Azure SQL, Redis, Service Bus, monitoring), network topology, and deployment flow, so that the infrastructure design is documented
53. As a developer, I want a `SYSTEM_DESIGN.md` document that describes how CI/CD works end-to-end (from code push to production deployment), so that the deployment pipeline is well-understood
54. As a developer, I want a `TECH_STACK.md` document listing all Azure services used and their purpose, so that the technology choices are documented
55. As a developer, I want a `Devops Agent Setup.md` document with step-by-step instructions for setting up self-hosted agents, so that agents can be recreated if needed
56. As a developer, I want the `README.md` to be updated with a "Deployment" section explaining how to deploy to Azure, so that the main readme covers cloud deployment

---

## Implementation Decisions

### Azure Infrastructure (Bicep IaC)
- Use **Bicep** (not Terraform or ARM) for all Azure resource provisioning — native Azure support, simpler syntax, good VS Code tooling
- All Bicep templates are **parameterized by environment** (Dev/Staging/Prod) — one set of templates, different parameter files per environment
- Resources provisioned: AKS, ACR, Azure SQL Database (per-service), Azure Cache for Redis, Azure Key Vault, VNet + Subnets, Azure Monitor / Log Analytics, Application Insights, Azure Service Bus
- Bicep modules organized by resource type (e.g., `modules/aks.bicep`, `modules/acr.bicep`, `modules/sql.bicep`, etc.) with a main orchestration file

### CI/CD (Azure Pipelines)
- **Per-service pipeline** — each of the 8 microservices (product, order, basket, inventory, shipping, payment, auth, api-gateway) has its own `azure-pipelines.yml`
- Pipelines are stored in **each service's directory** (e.g., `product-microservice/azure-pipelines.yml`) — colocated with the service code for discoverability
- Shared pipeline templates are stored in `Infrastructure - Deployment/pipelines/templates/` for reuse across services
- **GitHub + Azure Pipelines** — code stays on GitHub, Azure Pipelines connects to the GitHub repo
- **Microsoft-hosted agents** initially (`vmImage: 'ubuntu-latest'`) — simpler setup, no infrastructure to manage; self-hosted agents can be added later if private VNet access is needed
- **Build stage**: NuGet restore → `dotnet format` check → `dotnet build` → `dotnet test` (with Coverlet coverage) → `dotnet publish` → Docker build → Docker push to ACR
- **Deployment stages**: Dev (auto on `dev` branch), Staging (auto on `staging` branch), Production (auto on `prod` branch), with optional manual approval gates for Staging and Prod
- **Image tagging**: `{branch}-{buildnumber}` for branch builds, `{tag}` for tag/release builds
- **Kubernetes deployments** use `KubernetesManifest@0` task for secret creation and manifest deployment with image substitution

### AKS & Kubernetes
- **3 environments**: Dev, Staging, Production — each in its own Kubernetes namespace
- **Per-service K8s manifests**: Deployment (with probes, resource limits, env vars from secrets), Service (ClusterIP), HorizontalPodAutoscaler
- **Ingress**: Nginx Ingress Controller with path-based routing (Production only initially)
- **Secrets**: Created by the pipeline using `KubernetesManifest@0 createSecret` action — ACR pull secret, JWT keys, App Insights connection string, database connection strings, Redis connection strings, Service Bus connection strings
- K8s manifests organized under `Infrastructure - Deployment/kube/` with naming convention: `aks-{env}-{service}.yml` and `aks-{env}-namespace.yml`

### Docker Images
- Multi-stage Dockerfiles: SDK image for build/publish → ASP.NET runtime image for production
- Build context is the repository root (to access `shared-libs/`, `Directory.Build.props`, etc.)
- Each service's Dockerfile lives in its existing location (e.g., `product-microservice/Product.Service/Dockerfile`) — updated to be production-ready
- Target .NET 10.0 (matching the current project configuration)

### Managed Azure Services
- **Azure SQL Database**: One database per service that currently uses SQL Server (Auth, Order, Product, Inventory, Shipping, Payment) — connection strings injected via K8s secrets
- **Azure Cache for Redis**: Shared instance for Basket and Order services — connection strings injected via K8s secrets
- **Azure Monitor / Application Insights**: Replace local Jaeger/Prometheus/Grafana stack for cloud environments — OpenTelemetry exports to Application Insights via the Azure Monitor exporter
- **Azure Service Bus**: Optional replacement for RabbitMQ in cloud environments — topic/subscription model, switchable via configuration

### Azure Service Bus Integration
- Add `AzureServiceBusEventBus` implementing `IEventBus` in `ECommerce.Shared`
- Add `AzureServiceBusHostedService` for subscription handling
- Add `AzureServiceBusTelemetry` for OpenTelemetry context propagation
- Configuration switch: `Messaging:Provider` (env `Messaging__Provider`) — values `RabbitMq` (default) or `AzureServiceBus`
- Registration via new extension method `AddAzureServiceBusEventBus()` in `ECommerce.Shared`
- Same `Event` base type, `IEventBus` interface, and handler registration — handlers don't change

### Application Configuration
- OpenTelemetry exporter configuration switchable via `OpenTelemetry__Exporter` environment variable: `Otlp` (default, for local Jaeger/OTel Collector) or `AzureMonitor` (for Application Insights)
- Application Insights connection string via `APPLICATIONINSIGHTS_CONNECTION_STRING` environment variable
- All connection strings already use `ConnectionStrings__Default` pattern — no structural changes needed, only the values change per environment
- Health checks already exist at `/health/live` and `/health/ready` — no changes needed for AKS probes

### Local Kubernetes
- Document how to use Docker Desktop Kubernetes or Minikube with the existing `kubernetes/` manifests
- The existing manifests serve as the "local K8s" option; the new `Infrastructure - Deployment/kube/` manifests are for AKS
- Provide a step-by-step guide in documentation

---

## Testing Decisions

### What Makes a Good Test
- Tests should validate **external behavior** (API responses, event publishing, database state changes), not implementation details
- Infrastructure tests should validate that **deployed services are reachable and healthy** (smoke tests)
- Pipeline tests should validate that **builds succeed and tests pass** before deployment

### Modules to Test
- **Azure Service Bus adapter** (`AzureServiceBusEventBus`, `AzureServiceBusHostedService`) — unit tests using mock/emulator, integration tests with a real Azure Service Bus instance
- **Azure Monitor OpenTelemetry exporter** — integration test to verify traces/metrics are exported correctly
- **Existing service tests** — all existing unit and integration tests must continue to pass with both RabbitMQ and Azure Service Bus configurations
- **Bicep templates** — validate using `az deployment group what-if` before actual deployment
- **Pipeline smoke tests** — after deployment, verify health check endpoints respond correctly

### Prior Art
- Existing test patterns in the codebase: `WebApplicationFactory<Program>` for integration tests, `Given_When_Then` naming convention
- Azure Service Bus tests should follow the same patterns as existing RabbitMQ tests in each service

---

## Out of Scope

1. **Cosmos DB migration** — Azure SQL Database is used for now. Migration of specific services to Cosmos DB will be a separate PRD
2. **Custom domain / TLS certificates** — DNS configuration and HTTPS certificates for production Ingress
3. **Blue/Green or Canary deployment strategies** — standard rolling updates are used initially
4. **Multi-region deployment** — single-region deployment initially
5. **Disaster recovery** — backup/restore strategies for Azure SQL, Redis
6. **Cost optimization** — reserved instances, spot VMs for AKS nodes
7. **Security hardening** — network policies, pod security policies, RBAC within AKS (beyond basic deployment)
8. **Self-hosted Azure DevOps agents** — start with Microsoft-hosted; self-hosted agents (VMs in AKS VNet) can be added later if private cluster access is needed
9. **Automated scaling of self-hosted agents** — KEDA-based agent scaling
10. **GitOps (ArgoCD/Flux)** — pipeline-based deployment is used instead
11. **Migrating from GitHub to Azure Repos** — code stays on GitHub

---

## Further Notes

### Learning Path

This PRD is designed as a **learning exercise** as well as a practical deployment. The recommended order of implementation:

| Phase | Focus | Description |
|---|---|---|
| **Phase 1** | Dockerfiles | Create production-ready multi-stage Dockerfiles for all services |
| **Phase 2** | Bicep IaC | Provision Azure resources (AKS, ACR, Azure SQL, Redis, Monitor) |
| **Phase 3** | Azure Pipelines | Create per-service CI/CD pipelines with Build stage |
| **Phase 4** | AKS K8s Manifests | Create per-environment deployment manifests |
| **Phase 5** | Pipeline Deployment Stages | Add Dev/Staging/Prod deployment stages using Microsoft-hosted agents |
| **Phase 7** | Azure Service Bus Adapter | Implement the optional messaging provider |
| **Phase 8** | Application Config Changes | Azure Monitor exporters, connection string updates |
| **Phase 9** | Documentation | Write all architecture and setup docs |
| **Phase 10** | Local K8s Guide | Document local Kubernetes practice workflow |

### Reference Patterns

The `Infrastructure - Deployment/` directory contains reference patterns for:
- Azure Pipeline structure (`azure-pipelines.yml`)
- AKS K8s manifest patterns (`kube/` directory)
- Dockerfile patterns (`Dockerfile.build`, `Dockerfile.run`)
- Documentation structure (`OVERVIEW.md`, `ARCHITECTURE.md`, `SYSTEM_DESIGN.md`, `TECH_STACK.md`)

### Proposed Directory Structure

```
Infrastructure - Deployment/
├── bicep/                          # Infrastructure-as-Code
│   ├── main.bicep                  # Orchestration
│   ├── parameters/
│   │   ├── dev.bicepparam
│   │   ├── staging.bicepparam
│   │   └── prod.bicepparam
│   └── modules/
│       ├── aks.bicep
│       ├── acr.bicep
│       ├── sql.bicep
│       ├── redis.bicep
│       ├── keyvault.bicep
│       ├── vnet.bicep
│       ├── monitor.bicep
│       ├── appinsights.bicep
│       └── servicebus.bicep
├── pipelines/                      # Shared pipeline templates only
│   └── templates/
│       ├── build-stage.yml         # Shared build template
│       └── deploy-stage.yml        # Shared deploy template
├── kube/                           # AKS Kubernetes Manifests
│   ├── aks-dev-namespace.yml
│   ├── aks-dev-product.yml
│   ├── aks-dev-order.yml
│   ├── aks-dev-basket.yml
│   ├── aks-dev-inventory.yml
│   ├── aks-dev-shipping.yml
│   ├── aks-dev-payment.yml
│   ├── aks-dev-auth.yml
│   ├── aks-dev-api-gateway.yml
│   ├── aks-staging-namespace.yml
│   ├── aks-staging-*.yml           # (same pattern for all services)
│   ├── aks-prod-namespace.yml
│   ├── aks-prod-*.yml              # (same pattern for all services)
│   └── aks-prod-ingress.yml
├── docs/
│   ├── OVERVIEW.md
│   ├── ARCHITECTURE.md
│   ├── SYSTEM_DESIGN.md
│   ├── TECH_STACK.md
│   └── Devops Agent Setup.md
└── README.md
```

---

## Proposed Issue Breakdown (Vertical Slices)

When ready to implement, the PRD should be broken into these vertical slices:

| # | Title | Type | Blocked By | User Stories |
|---|---|---|---|---|
| 1 | Production Dockerfiles for all microservices | AFK | None | 12, 13, 14 |
| 2 | Bicep IaC: VNet, AKS & ACR provisioning | AFK | None | 1, 2, 6, 10, 11 |
| 3 | Bicep IaC: Azure SQL, Redis, Key Vault, Monitor, Service Bus | AFK | #2 | 3, 4, 5, 7, 8, 9 |
| 4 | Azure Pipeline build stage template (shared) | AFK | #1 | 16, 17, 18, 19 |
| 5 | Per-service Azure Pipeline definitions | AFK | #4 | 16 |
| 6 | AKS K8s manifests: Dev environment (all services) | AFK | #2 | 27, 28, 29, 30, 33 |
| 7 | AKS K8s manifests: Staging environment (all services) | AFK | #6 | 27, 28, 29, 30, 33 |
| 8 | AKS K8s manifests: Production environment + Ingress | AFK | #7 | 27, 28, 29, 30, 31, 33 |
| 9 | Pipeline deployment stages (Dev/Staging/Prod) | AFK | #5, #6 | 20–26, 45 |
| 10 | Infrastructure K8s manifests (RabbitMQ for non-Service-Bus envs) | AFK | #6 | 32 |
| 11 | Azure Service Bus adapter for `IEventBus` | AFK | None | 34, 35, 36, 37, 38 |
| 12 | Application config: Azure Monitor OpenTelemetry exporter | AFK | #3 | 41, 42 |
| 13 | Application config: Azure SQL, Redis, YARP cluster addresses | AFK | #3 | 39, 40, 43, 44 |
| 14 | Documentation: OVERVIEW, ARCHITECTURE, SYSTEM_DESIGN, TECH_STACK | AFK | #9 | 51–54, 56 |
| 15 | Documentation: Local Kubernetes practice guide | AFK | None | 48, 49, 50 |
| 16 | Docker push to ACR in pipeline | AFK | #4, #2 | 15 |
