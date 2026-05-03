# Kubernetes Deployment

Manifests live under [`kubernetes/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/kubernetes). Each file is self-contained (`Deployment` + `Service`) so you can apply them individually.

> **Local vs Azure.** This page covers running the platform on local Kubernetes (Docker Desktop / Minikube) using the un-prefixed manifests. The same folder also holds per-environment AKS manifests (`aks-dev-*.yml`, `aks-staging-*.yml`, `aks-prod-*.yml`) consumed by Azure Pipelines — see [Azure-Deployment](Azure-Deployment) for the cloud flow.
>
> For a step-by-step walkthrough (image build, Docker Desktop / Minikube setup, troubleshooting `ImagePullBackOff` and PVC `ReadWriteMany` issues), see [Local-Kubernetes-Guide](Local-Kubernetes-Guide).

## Apply order

Infrastructure first, observability next, microservices last.

```bash
# 1. Infrastructure
kubectl apply -f kubernetes/sql.yaml
kubectl apply -f kubernetes/rabbitmq.yaml
kubectl apply -f kubernetes/redis.yaml

# 2. Observability
kubectl apply -f kubernetes/otel-collector.yaml
kubectl apply -f kubernetes/jaeger.yaml
kubectl apply -f kubernetes/prometheus.yaml
kubectl apply -f kubernetes/alertmanager.yaml
kubectl apply -f kubernetes/loki.yaml
kubectl apply -f kubernetes/grafana.yaml
kubectl apply -f kubernetes/exporters.yaml

# 3. Microservices
kubectl apply -f kubernetes/product-microservice.yaml
kubectl apply -f kubernetes/order-microservice.yaml
kubectl apply -f kubernetes/basket-microservice.yaml
kubectl apply -f kubernetes/auth-microservice.yaml
kubectl apply -f kubernetes/inventory-microservice.yaml
kubectl apply -f kubernetes/api-gateway.yaml
```

## Service discovery

Services discover each other through Kubernetes DNS. The ClusterIP service names used in config are:

| Logical dependency | K8s service name |
|---|---|
| SQL Server | `mssql-clusterip-service` |
| RabbitMQ | `rabbitmq-clusterip-service` |
| Redis | `redis-clusterip-service` |

If you fork and rename, update `appsettings.json` accordingly.

## Health

Every microservice manifest wires readiness and liveness probes against `/health/ready` and `/health/live`, powered by the shared health-check helpers in [Shared-Library](Shared-Library).

## Rolling back the Gateway provider

The Gateway ships YARP and Ocelot in the same image. To swap without a rebuild:

```bash
kubectl set env deploy/api-gateway Gateway__Provider=Ocelot
kubectl rollout restart deploy/api-gateway
```

Confirm from the pod logs: `ApiGateway starting with provider=Ocelot`. See [Service-API-Gateway](Service-API-Gateway).

## Verifying

```bash
kubectl get pods
kubectl get services
kubectl logs -l app=api-gateway --tail=50
```

Port-forward the Gateway for smoke testing:

```bash
kubectl port-forward svc/api-gateway-clusterip-service 8004:8004
```

Then follow the smoke test in [Getting-Started](Getting-Started#first-request--end-to-end-smoke-test).

## Deploying to Azure (AKS)

The AKS manifests (`aks-{env}-{service}.yml`) are deployed by per-service Azure Pipelines after a `docker push` to ACR — image tag substitution happens at deploy time via `KubernetesManifest@0`. Bicep provisions the cluster, ACR, SQL, Redis, Service Bus, Key Vault, and Application Insights.

See [Azure-Deployment](Azure-Deployment) for the full topology, environment matrix, pipeline templates, and provider switches (`Messaging__Provider`, `OpenTelemetry__Exporter`).
