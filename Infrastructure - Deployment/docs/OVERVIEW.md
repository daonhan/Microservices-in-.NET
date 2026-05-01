# Azure Cloud Deployment — Overview

> **Audience:** developers, operators, and architects who want to understand
> how the e-commerce microservices platform runs on Microsoft Azure.

## What is this?

This document describes the **Azure deployment topology** for the
microservices platform in this repository. It is the entry point into the
documentation under [`Infrastructure - Deployment/docs/`](.).

The platform consists of 8 services (Auth, Product, Basket, Order,
Inventory, Shipping, Payment, API Gateway) plus shared messaging and
observability infrastructure. Locally it runs on Docker Compose; in Azure
it runs on **Azure Kubernetes Service (AKS)** with a full CI/CD pipeline
in **Azure Pipelines**.

## Environments

Three environments are provisioned with the same Bicep templates and the
same Kubernetes manifests, parameterized per environment. The manifests
live in the root [`kubernetes/`](../../kubernetes/) folder:

| Environment | Namespace           | Branch trigger    | Agent pool           | Replicas (min/max) | Notes                                   |
|-------------|---------------------|-------------------|----------------------|--------------------|-----------------------------------------|
| Dev         | `ecommerce-dev`     | `dev`, `deploy/*` | Microsoft-hosted     | 1 / 3              | Auto-deploy. RabbitMQ in-cluster.       |
| Staging     | `ecommerce-staging` | `staging`         | Self-hosted          | 1 / 5              | Private/VNet deploy path, approval gate.|
| Production  | `ecommerce-prod`    | `prod`            | Self-hosted          | 2 / 10             | Private/VNet deploy path, Ingress.      |

Each environment has its own Azure resource group, AKS cluster (or node
pool), and managed services (Azure SQL, Azure Cache for Redis, Azure
Service Bus, Application Insights). Secrets and connection strings are
kept in Azure DevOps pipeline variables and pushed into Kubernetes
secrets at deploy time.

## Layered learning path

The platform is designed to be learned and operated in three layers:

1. **Local app dev** — `docker compose up`. Inner-loop development with
   SQL Server, Redis, RabbitMQ, Jaeger/Prometheus/Grafana running as
   containers.
2. **Local Kubernetes** — Docker Desktop Kubernetes or Minikube using the
   manifests under [`kubernetes/`](../../kubernetes/). See
   [LOCAL_K8S_GUIDE.md](../../docs/LOCAL_K8S_GUIDE.md) for the step-by-step.
3. **Azure cloud** — Bicep-provisioned AKS + ACR + managed Azure services,
   deployed by per-service Azure Pipelines using the same environment-specific
   manifests under [`kubernetes/`](../../kubernetes/), for example
   `kubernetes/aks-dev-order.yml`.

## Deployment model

```
Code push (GitHub)
    │
    ▼
Azure Pipelines (per-service azure-pipelines.yml)
    │
    ├─ Build stage (shared template)
    │     ├─ dotnet restore / format / build / test (+ Cobertura coverage)
    │     ├─ dotnet publish
    │     ├─ docker build -f <service>/Dockerfile .
    │     └─ docker push  → Azure Container Registry
    │
    └─ Deploy stage (per env, shared template)
          ├─ Create K8s secrets from pipeline variables
          ├─ kubectl apply -f kubernetes/aks-<env>-<service>.yml
          └─ Image tag substituted via KubernetesManifest@0
                  │
                  ▼
        AKS namespace ecommerce-<env>
          ├─ Deployment + Service + HPA (per service)
          ├─ Pulls image from ACR (acr-pull-secret)
          └─ Reads connection strings & keys from K8s secrets
```

## Where things live

| Concern                       | Path                                                                 |
|-------------------------------|----------------------------------------------------------------------|
| IaC (Bicep)                   | [`Infrastructure - Deployment/bicep/`](../bicep/)                    |
| Pipeline templates            | [`Infrastructure - Deployment/pipelines/templates/`](../pipelines/templates/) |
| Per-service pipelines         | `<service>-microservice/azure-pipelines.yml`                         |
| Kubernetes manifests (local + AKS) | [`kubernetes/`](../../kubernetes/)                             |
| Dockerfiles                   | `<service>-microservice/<Service>.Service/Dockerfile`                |
| PRD                           | [`docs/prd/azure-infrastructure-deployment.md`](../../docs/prd/azure-infrastructure-deployment.md) |
| Implementation plan           | [`docs/plans/azure-infrastructure-deployment-plan.md`](../../docs/plans/azure-infrastructure-deployment-plan.md) |

## Companion documents

- [ARCHITECTURE.md](ARCHITECTURE.md) — cloud architecture, network topology, service mesh
- [SYSTEM_DESIGN.md](SYSTEM_DESIGN.md) — end-to-end CI/CD flow with stage details
- [TECH_STACK.md](TECH_STACK.md) — every Azure service, its purpose, and integration points
- [Devops Agent Setup.md](Devops%20Agent%20Setup.md) — Dev on Microsoft-hosted agents; Staging/Prod on self-hosted agents
- [LOCAL_K8S_GUIDE.md](../../docs/LOCAL_K8S_GUIDE.md) — running the platform on Docker Desktop Kubernetes / Minikube
