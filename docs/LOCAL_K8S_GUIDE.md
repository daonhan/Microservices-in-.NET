# Local Kubernetes Practice Guide

This guide walks you through running the full microservices platform on a local Kubernetes
cluster (Docker Desktop or Minikube) using the manifests in `kubernetes/`. The goal is
to validate manifest changes before they hit AKS — same shapes, same probes, same DNS
naming conventions, but on your laptop.

---

## Why run locally?

- Catch manifest bugs (typos, missing services, bad probes) without an AKS round-trip.
- Reproduce service-discovery behavior (Kubernetes DNS, ClusterIP services) that
  `docker compose` does not model.
- Smoke-test the full topology — saga, outbox, observability — before merging.

If you only need infra (SQL, RabbitMQ, Redis, Jaeger…), `docker compose up` is faster.
Use local K8s when the change touches a `*.yaml` under `kubernetes/`.

---

## `kubernetes/` vs `Infrastructure - Deployment/kube/`

There are two K8s folders in this repo. They are **not interchangeable**:

| Folder | Purpose | Cluster targets |
|---|---|---|
| `kubernetes/` | Active manifests for this project. One file per service (`Deployment` + `Service`), plus infra (`sql.yaml`, `rabbitmq.yaml`, `redis.yaml`) and observability (`jaeger.yaml`, `prometheus.yaml`, `grafana.yaml`, …). | **Local K8s** (Docker Desktop, Minikube) and the early AKS dev environment. |
| `Infrastructure - Deployment/kube/` *(not yet in repo)* | Reference / template AKS manifests (planned). Will include AKS-specific concerns (HPA, namespaces, Azure Application Insights env vars, image pulls from `*.azurecr.io`). | AKS only — not wired into the local stack. |

**Apply only `kubernetes/` against your local cluster.** `Infrastructure - Deployment/kube/`
does not yet exist — it will be added as the AKS Dev/Staging/Prod manifests land
(Slices 6–8).

---

## Prerequisites

- Docker Desktop **or** Minikube installed.
- `kubectl` on `PATH`. (`kubectl version --client`)
- Local images for each microservice. The `kubernetes/*.yaml` manifests reference
  `image: <USERNAME>/<service>:latest` — you will replace `<USERNAME>` with a tag
  that points at images present in your local Docker daemon (or push to a registry).
- ~6 GB RAM free for the cluster (SQL Server is the heavy one).

Build the service images from the repo root:

```bash
# Example: product
docker build -f product-microservice/Product.Service/Dockerfile -t local/productservice:latest .

# Or all of them
for svc in product order basket auth inventory shipping payment; do
  case $svc in
    product) ctx="product-microservice/Product.Service" ;;
    order)   ctx="order-microservice/Order.Service" ;;
    basket)  ctx="basket-microservice/Basket.Service" ;;
    auth)    ctx="auth-microservice/Auth.Service" ;;
    inventory) ctx="inventory-microservice/Inventory.Service" ;;
    shipping)  ctx="shipping-microservice/Shipping.Service" ;;
    payment)   ctx="payment-microservice/Payment.Service" ;;
  esac
  docker build -f "$ctx/Dockerfile" -t "local/${svc}service:latest" .
done

docker build -f api-gateway/ApiGateway/Dockerfile -t local/apigateway:latest .
```

> Build context is the repo root (the trailing `.`) so `shared-libs/`,
> `Directory.Build.props`, and `local-nuget-packages/` are visible to the Dockerfile.

Before applying manifests, replace `<USERNAME>/<service>:latest` with `local/<service>:latest`
(see [Replace image references](#replace-image-references) below).

---

## Option A: Docker Desktop Kubernetes

### 1. Enable Kubernetes

1. Open **Docker Desktop** → ⚙ **Settings** → **Kubernetes**.
2. Tick **Enable Kubernetes**.
3. Click **Apply & Restart**. First start can take 2–5 minutes — the bottom-left status
   in Docker Desktop shows **Kubernetes is starting…** then **Kubernetes is running**.
4. Verify from a terminal:

   ```bash
   kubectl config use-context docker-desktop
   kubectl cluster-info
   kubectl get nodes
   ```

   You should see one node named `docker-desktop` in `Ready` state.

### 2. Make sure local images are visible

Docker Desktop's Kubernetes uses the same image store as Docker Desktop, so any image
you `docker build` is automatically available. **No `docker push` is needed.**

Make the manifests use `imagePullPolicy: IfNotPresent` (default for tagged images other
than `:latest` — and for `:latest` Docker Desktop usually still resolves locally first;
if you hit `ErrImagePull`, switch the manifest to a non-`latest` tag like `:dev`).

### 3. Skip to [Deploy the platform](#deploy-the-platform).

---

## Option B: Minikube

### 1. Start a cluster

```bash
minikube start --cpus=4 --memory=6144 --driver=docker
kubectl config use-context minikube
kubectl get nodes
```

Tune `--cpus` / `--memory` for your machine — SQL Server + Redis + RabbitMQ + 8
services + observability stack is the floor.

### 2. Point Docker at Minikube's daemon

Minikube runs its own Docker daemon. To make `docker build` produce images visible
to the cluster, evaluate Minikube's environment in your shell:

```bash
eval "$(minikube docker-env)"

# Verify your shell now talks to the Minikube daemon
docker info | grep -i name   # should mention minikube
```

Now rebuild the images (see [Prerequisites](#prerequisites)). They will land directly
in the cluster's image store — no registry round-trip needed.

> If you forget `eval $(minikube docker-env)`, builds go into your host daemon and the
> cluster will fail to pull them.

### 3. Skip to [Deploy the platform](#deploy-the-platform).

---

## Replace image references

Manifests under `kubernetes/` use `<USERNAME>/<service>:latest`. Before applying, swap
that placeholder for your local tag. A one-shot in-place edit:

```bash
# Replace <USERNAME> with "local"
find kubernetes -name '*.yaml' -exec sed -i.bak 's|<USERNAME>/|local/|g' {} +

# Optional: also force IfNotPresent so the kubelet does not try to pull from a registry
find kubernetes -name '*.yaml' -exec sed -i.bak '/image: local\//a\          imagePullPolicy: IfNotPresent' {} +
```

The `*.bak` files are throwaway; clean them up with `find kubernetes -name '*.bak' -delete`
when you are done. **Do not commit these substitutions** — they are local-only.

---

## Deploy the platform

Apply order matters: infra → observability → microservices. Otherwise pods crash-loop
waiting on dependencies that have not been scheduled yet.

```bash
# 1. Infrastructure
kubectl apply -f kubernetes/sql.yaml
kubectl apply -f kubernetes/rabbitmq.yaml
kubectl apply -f kubernetes/redis.yaml

# 2. Observability (optional but recommended for tracing)
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
kubectl apply -f kubernetes/shipping-microservice.yaml
kubectl apply -f kubernetes/payment-microservice.yaml
kubectl apply -f kubernetes/api-gateway.yaml
```

Wait for everything to be `Ready`:

```bash
kubectl get pods -w
```

First start is slow: SQL Server alone takes ~60–90 s before its readiness probe passes.
Microservices restart until SQL is reachable — that is expected on the first apply.

---

## Verify

### Pods and services

```bash
kubectl get pods
kubectl get services
```

Every pod should be `Running` with `READY 1/1`. Every microservice should expose a
`*-clusterip-service` and a `*-loadbalancer` Service.

### Health probes

The manifests wire `/health/ready` and `/health/live` (see
[Shared-Library](wiki/Shared-Library.md)). Hit them through `kubectl exec`
to bypass `LoadBalancer` quirks on local clusters:

```bash
kubectl exec deploy/productservice -- curl -sf http://localhost:8080/health/ready
kubectl exec deploy/apigateway   -- curl -sf http://localhost:8080/health/live
```

`curl -sf` exits non-zero on non-2xx, so a successful command means the probe passed.

### End-to-end smoke test

Port-forward the gateway and reach the public surface:

```bash
kubectl port-forward svc/apigateway-loadbalancer 8004:8004
```

In another terminal, follow the smoke test in
[Getting-Started](wiki/Getting-Started.md#first-request--end-to-end-smoke-test):
register, log in, hit `/products`, place an order, watch the saga complete.

You can confirm the saga ran by tailing logs across services:

```bash
kubectl logs -l app=orderservice --tail=50
kubectl logs -l app=inventoryservice --tail=50
```

Look for `OrderCreatedEvent` → `StockReserved` → `OrderConfirmed`.

### Observability

Port-forward the dashboards if you brought up the observability stack:

```bash
kubectl port-forward svc/jaeger 16686:16686       # Jaeger UI
kubectl port-forward svc/grafana 3000:3000        # Grafana UI
kubectl port-forward svc/prometheus 9090:9090     # Prometheus
```

Open `http://localhost:16686` and confirm traces from `apigateway → productservice`
appear within ~10 s of a request.

---

## Troubleshooting

### `ImagePullBackOff` / `ErrImagePull`

The cluster cannot find your image.

- **Docker Desktop**: confirm the image exists with `docker images | grep local/`. Make sure
  the manifest is using `local/<service>:latest` (not `<USERNAME>/`). Add
  `imagePullPolicy: IfNotPresent` to skip registry lookups.
- **Minikube**: you forgot `eval "$(minikube docker-env)"` before building. Re-run it
  and rebuild. Verify with `minikube ssh -- docker images | grep local/`.

### Pods stuck in `CrashLoopBackOff` after applying microservices first

Order matters. SQL / Redis / RabbitMQ must be `Ready` before microservices start.
Delete the failing pods so they restart against the now-healthy infra:

```bash
kubectl delete pods -l app=orderservice
```

Or apply the infra manifests first and wait for readiness before the microservice
manifests:

```bash
kubectl wait --for=condition=ready pod -l app=mssql --timeout=180s
```

### `MountVolume.SetUp failed` / `pod has unbound immediate PersistentVolumeClaims`

`sql.yaml` requests a `PersistentVolumeClaim` with `ReadWriteMany`. On Docker Desktop
and Minikube, the default `StorageClass` only supports `ReadWriteOnce`. The PVC stays
pending forever.

Fix locally by editing `kubernetes/sql.yaml` to use `ReadWriteOnce`:

```yaml
accessModes:
  - ReadWriteOnce
```

(Do not commit this — it is a local-only workaround. The `ReadWriteMany` mode is
intentional for AKS.)

### `connection refused` when port-forwarding

The pod is `Ready` but the Service is not selecting it. Check labels match:

```bash
kubectl describe svc apigateway-loadbalancer
kubectl get pods --show-labels -l app=apigateway
```

The `selector` and pod `app=` label must match exactly.

### SQL Server pod OOM-killed

SQL Server 2022 needs ~1.5 GB. On Docker Desktop, increase the VM memory
(Settings → Resources → Memory). On Minikube, restart the cluster with more memory:

```bash
minikube stop
minikube start --memory=8192
```

### `dial tcp: lookup mssql-clusterip-service` errors in microservice logs

Kubernetes DNS has not propagated yet, or you applied the microservice before the SQL
Service. Re-apply the SQL manifest and restart the microservice pod:

```bash
kubectl apply -f kubernetes/sql.yaml
kubectl rollout restart deploy/productservice
```

### Cleaning up

Tear everything down with one command:

```bash
kubectl delete -f kubernetes/
```

For Minikube, you can also wipe the whole VM:

```bash
minikube delete
```

For Docker Desktop, untick **Enable Kubernetes** in Settings to stop the cluster
without losing Docker Desktop itself.

---

## Next steps

- Once you are comfortable here, the same manifests will be promoted to AKS Dev
  (Slice 6), Staging (Slice 7), and Prod (Slice 8). The local cluster is a faithful
  proxy for the dev environment — if it works here, it should work there.
- For the AKS-specific deltas (HPA, Application Insights, ACR image pulls), see
  the manifests under `Infrastructure - Deployment/kube/` once they are migrated to
  this project's services.
