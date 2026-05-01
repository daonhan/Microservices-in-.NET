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
same Kubernetes manifests, parameterized per environment:

| Environment | Namespace          | Branch trigger     | Replicas (min/max) | Notes                                  |
|-------------|--------------------|--------------------|--------------------|----------------------------------------|
| Dev         | `ecommerce-dev`    | `dev`, `deploy/*`  | 1 / 3              | Auto-deploy. RabbitMQ in-cluster.      |
| Staging     | `ecommerce-staging`| `staging`          | 1 / 5              | Auto-deploy. Optional approval gate.   |
| Production  | `ecommerce-prod`   | `prod`             | 2 / 10             | Approval gate. Nginx Ingress for `/api`.|

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
   deployed by per-service Azure Pipelines using the manifests under
   [`Infrastructure - Deployment/kube/`](../kube/).

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
          ├─ kubectl apply -f kube/aks-<env>-<service>.yml
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
| AKS K8s manifests             | [`Infrastructure - Deployment/kube/`](../kube/)                      |
| Local K8s manifests           | [`kubernetes/`](../../kubernetes/)                                   |
| Dockerfiles                   | `<service>-microservice/<Service>.Service/Dockerfile`                |
| PRD                           | [`docs/prd/azure-infrastructure-deployment.md`](../../docs/prd/azure-infrastructure-deployment.md) |
| Implementation plan           | [`docs/plans/azure-infrastructure-deployment-plan.md`](../../docs/plans/azure-infrastructure-deployment-plan.md) |

## Companion documents

- [ARCHITECTURE.md](ARCHITECTURE.md) — cloud architecture, network topology, service mesh
- [SYSTEM_DESIGN.md](SYSTEM_DESIGN.md) — end-to-end CI/CD flow with stage details
- [TECH_STACK.md](TECH_STACK.md) — every Azure service, its purpose, and integration points
- [Devops Agent Setup.md](Devops%20Agent%20Setup.md) — migrating from Microsoft-hosted to self-hosted agents
- [LOCAL_K8S_GUIDE.md](../../docs/LOCAL_K8S_GUIDE.md) — running the platform on Docker Desktop Kubernetes / Minikube
