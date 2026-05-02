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

## Which manifests do I apply?

The `kubernetes/` folder holds **two distinct sets** of manifests, distinguished by
filename prefix:

| Filename pattern | Purpose | Apply to |
|---|---|---|
| `<service>.yaml`, `sql.yaml`, `rabbitmq.yaml`, `redis.yaml`, observability YAMLs (`jaeger.yaml`, `prometheus.yaml`, `grafana.yaml`, `loki.yaml`, …) | **Local stack.** Single replica, no namespace, `LoadBalancer` services for easy access, `<USERNAME>/<service>:latest` image placeholders. | Docker Desktop, Minikube |
| `aks-dev-*.yml`, `aks-staging-*.yml`, `aks-prod-*.yml`, `aks-prod-ingress.yml` | **AKS reference.** Per-environment namespaces (`ecommerce-dev`, `ecommerce-staging`, `ecommerce-prod`), HPAs, ACR image pulls, secret refs for Application Insights, Azure SQL, Redis, Service Bus. | AKS only |

**Apply only the local-stack manifests against your laptop cluster.** The
`aks-*.yml` files reference Azure-only secrets (ACR pull, App Insights connection
strings) and will fail to start without them.

> The `Infrastructure - Deployment/kube/` folder contains a few legacy adapter
> manifests unrelated to this guide. Ignore it for local practice.

---

## Prerequisites

- Docker Desktop **or** Minikube installed.
- `kubectl` on `PATH`. (`kubectl version --client`)
- Local images for each microservice. The `kubernetes/*.yaml` manifests reference
  `image: <USERNAME>/<service>:latest` — you will replace `<USERNAME>` with a tag
  that points at images present in your local Docker daemon (or push to a registry).
- ~6 GB RAM free for the cluster (SQL Server is the heavy one).

Build the service images from the repo root:

### One-shot scripts (recommended)

Wrappers that build all 8 images with the repo root as build context:

```bash
# Bash / WSL / macOS
./scripts/build-local-images.sh                  # tag = latest
TAG=dev ./scripts/build-local-images.sh          # custom tag
PARALLEL=1 ./scripts/build-local-images.sh       # parallel (xargs -P 4)
```

```powershell
# PowerShell (Windows / Docker Desktop)
./scripts/build-local-images.ps1
./scripts/build-local-images.ps1 -Tag dev
./scripts/build-local-images.ps1 -Parallel       # PowerShell 7+
```

> For Minikube, run `eval "$(minikube docker-env)"` (bash) or
> `minikube docker-env --shell powershell | Invoke-Expression` (pwsh) **before**
> running the script so images land in the cluster's image store.

### Manual one-by-one

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

PowerShell equivalent (Windows / Docker Desktop):

```powershell
$services = @{
    product   = 'product-microservice/Product.Service'
    order     = 'order-microservice/Order.Service'
    basket    = 'basket-microservice/Basket.Service'
    auth      = 'auth-microservice/Auth.Service'
    inventory = 'inventory-microservice/Inventory.Service'
    shipping  = 'shipping-microservice/Shipping.Service'
    payment   = 'payment-microservice/Payment.Service'
    apigateway = 'api-gateway/ApiGateway'
}

foreach ($svc in $services.Keys) {
    $tag = if ($svc -eq 'apigateway') { 'local/apigateway:latest' } else { "local/${svc}service:latest" }
    docker build -f "$($services[$svc])/Dockerfile" -t $tag .
    if ($LASTEXITCODE -ne 0) { throw "build failed for $svc" }
}
```

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

PowerShell equivalent:

```powershell
Get-ChildItem kubernetes -Filter *.yaml | ForEach-Object {
    (Get-Content $_.FullName) -replace '<USERNAME>/', 'local/' |
        Set-Content $_.FullName
}

# Revert later with `git checkout -- kubernetes/`
```

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

### One-shot apply with readiness gates

The same flow, scripted with `kubectl wait` between stages so microservices do not
crash-loop while infra is still coming up. Use the wrappers in `scripts/`:

```bash
# Bash / WSL / macOS
./scripts/apply-local-k8s.sh                       # full stack
SKIP_OBSERVABILITY=1 ./scripts/apply-local-k8s.sh  # infra + services only
TIMEOUT=600s ./scripts/apply-local-k8s.sh          # bump readiness timeout
```

```powershell
# PowerShell
./scripts/apply-local-k8s.ps1
./scripts/apply-local-k8s.ps1 -SkipObservability
./scripts/apply-local-k8s.ps1 -Timeout 600s
```

Both scripts:

1. Apply `sql.yaml`, `rabbitmq.yaml`, `redis.yaml` and wait for each to be `Ready`.
2. Apply the observability stack (unless skipped).
3. Apply every `*-microservice.yaml` plus `api-gateway.yaml`, then wait for each
   `app=<name>` selector to be `Ready`.
4. Print a final `kubectl get pods` snapshot and a port-forward hint.

If a readiness gate fails, the script prints the failing pod's events and exits
non-zero — handy for CI smoke jobs.

> The microservice glob deliberately matches only `*-microservice.yaml`, which
> excludes `aks-dev-*.yml`, `aks-staging-*.yml`, and `aks-prod-*.yml` (those are
> AKS-only and reference Azure secrets that do not exist locally).

<details>
<summary>Inline equivalent (no scripts)</summary>

```bash
#!/usr/bin/env bash
set -euo pipefail

# 1. Infra
kubectl apply -f kubernetes/sql.yaml -f kubernetes/rabbitmq.yaml -f kubernetes/redis.yaml
kubectl wait --for=condition=ready pod -l app=mssql     --timeout=300s
kubectl wait --for=condition=ready pod -l app=rabbitmq  --timeout=180s
kubectl wait --for=condition=ready pod -l app=redis     --timeout=120s

# 2. Observability (optional)
kubectl apply \
  -f kubernetes/otel-collector.yaml \
  -f kubernetes/jaeger.yaml \
  -f kubernetes/prometheus.yaml \
  -f kubernetes/alertmanager.yaml \
  -f kubernetes/loki.yaml \
  -f kubernetes/grafana.yaml \
  -f kubernetes/exporters.yaml

# 3. Microservices (excludes aks-* manifests)
for f in kubernetes/*-microservice.yaml kubernetes/api-gateway.yaml; do
  kubectl apply -f "$f"
done

kubectl get pods
```

</details>

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

In another terminal, run the wrapper:

```powershell
./scripts/local-smoke-test.ps1
# override the seeded admin credentials if you have changed them:
./scripts/local-smoke-test.ps1 -Username 'me@example.com' -Password 'secret'
```

It logs in as the seeded administrator (`microservices@daonhan.com` —
[Auth.Service/.../UserConfiguration.cs](../auth-microservice/Auth.Service/Infrastructure/Data/EntityFramework/Configurations/UserConfiguration.cs)),
creates a product, places an order against it, and prints log-tail commands
to verify the saga.

The same flow inline (matches the actual gateway routes in
[api-gateway/ApiGateway/appsettings.json](../api-gateway/ApiGateway/appsettings.json)
— singular paths, no `/auth/register`, no `/products` listing):

```powershell
$base = 'http://localhost:8004'

# 1. Login as the seeded administrator
$loginBody = @{ Username = 'microservices@daonhan.com'; Password = 'oKNrqkO7iC#G' } | ConvertTo-Json
$token = (Invoke-RestMethod "$base/login" -Method Post -ContentType 'application/json' -Body $loginBody).token
$h = @{ Authorization = "Bearer $token" }

# 2. Create a product (Admin route)
$body = @{ Name = "smoke-$(Get-Random)"; Price = 9.99; ProductTypeId = 1 } | ConvertTo-Json
$resp = Invoke-WebRequest "$base/product/" -Method Post -Headers $h -ContentType 'application/json' -Body $body
$productId = [int]$resp.Content

# 3. Read it back
Invoke-RestMethod "$base/product/$productId" -Headers $h

# 4. Place an order (triggers the saga)
$orderBody = @{ OrderProducts = @(@{ ProductId = "$productId"; Quantity = 1 }) } | ConvertTo-Json
Invoke-WebRequest "$base/order/cust-$(Get-Random)" -Method Post -Headers $h -ContentType 'application/json' -Body $orderBody
```

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

### `MountVolume.SetUp failed` / `pod has unbound immediate PersistentVolumeClaims` / `PreBind plugin "VolumeBinding": binding volumes: context deadline exceeded`

**Verified fix on Docker Desktop.** [kubernetes/sql.yaml](../kubernetes/sql.yaml)
requests a `PersistentVolumeClaim` with `ReadWriteMany`. Docker Desktop's only
storage class is `rancher.io/local-path` (also Minikube's default), which **only
supports `ReadWriteOnce`** — the PVC never binds and the `mssql` pod stays
`Pending` forever. The scheduler logs:

```text
Warning  FailedScheduling  default-scheduler
  running PreBind plugin "VolumeBinding": binding volumes: context deadline exceeded
```

#### Easy fix: let the apply scripts handle it

Both [scripts/apply-local-k8s.ps1](../scripts/apply-local-k8s.ps1) and
[scripts/apply-local-k8s.sh](../scripts/apply-local-k8s.sh) detect this
automatically. They check the cluster's storage classes; if the only provisioners
are `rancher.io/local-path`, `docker.io/hostpath`, or `k8s.io/minikube-hostpath`,
they stream `kubernetes/sql.yaml` through an in-memory
`ReadWriteMany` → `ReadWriteOnce` rewrite before piping it to `kubectl apply -f -`.
**The file on disk is not modified**, so nothing to revert and nothing to forget.

Override the auto-detection if needed:

```powershell
./scripts/apply-local-k8s.ps1 -LocalStorageFix on    # force rewrite
./scripts/apply-local-k8s.ps1 -LocalStorageFix off   # never rewrite
./scripts/apply-local-k8s.ps1 -LocalStorageFix auto  # default
```

```bash
LOCAL_STORAGE_FIX=on   ./scripts/apply-local-k8s.sh
LOCAL_STORAGE_FIX=off  ./scripts/apply-local-k8s.sh
LOCAL_STORAGE_FIX=auto ./scripts/apply-local-k8s.sh   # default
```

#### Manual fix (when applying by hand)

Confirm the symptom first:

```powershell
kubectl get storageclass
# NAME                 PROVISIONER             ...
# hostpath             rancher.io/local-path   ...
# standard (default)   rancher.io/local-path   ...

kubectl describe pvc mssql-claim   # STATUS will be Pending
```

Then edit [kubernetes/sql.yaml](../kubernetes/sql.yaml) to use `ReadWriteOnce`:

```yaml
spec:
  accessModes:
    - ReadWriteOnce   # was: ReadWriteMany
```

`accessModes` is immutable on an existing PVC, so delete and re-apply:

```powershell
kubectl delete -f kubernetes/sql.yaml --ignore-not-found
kubectl apply  -f kubernetes/sql.yaml
kubectl get pvc -w   # wait until mssql-claim shows STATUS=Bound, then Ctrl+C
```

```bash
kubectl delete -f kubernetes/sql.yaml --ignore-not-found
kubectl apply  -f kubernetes/sql.yaml
kubectl get pvc -w   # wait until mssql-claim shows STATUS=Bound, then Ctrl+C
```

> **Do not commit this change.** The `ReadWriteMany` mode is intentional for AKS
> (Azure Files supports RWX). Revert before pushing:
>
> ```bash
> git checkout -- kubernetes/sql.yaml
> ```

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
# Delete only local-stack manifests; ignore the aks-* ones
for f in kubernetes/*.yaml; do kubectl delete -f "$f" --ignore-not-found; done
```

PowerShell:

```powershell
Get-ChildItem kubernetes -Filter *.yaml |
    ForEach-Object { kubectl delete -f $_.FullName --ignore-not-found }
```

For Minikube, you can also wipe the whole VM:

```bash
minikube delete
```

For Docker Desktop, untick **Enable Kubernetes** in Settings to stop the cluster
without losing Docker Desktop itself.

---

## Next steps

- Once you are comfortable here, the AKS Dev/Staging/Prod manifests live alongside
  the local ones in [kubernetes/](../kubernetes/) under the `aks-{env}-*.yml`
  prefix. The local cluster is a faithful proxy for the dev environment — if it
  works here, it should work there.
- AKS-specific deltas (HPA, Application Insights, ACR image pulls, namespaces,
  ingress) are visible by diffing a local manifest (e.g.
  [kubernetes/product-microservice.yaml](../kubernetes/product-microservice.yaml))
  against its AKS counterpart
  ([kubernetes/aks-dev-product.yml](../kubernetes/aks-dev-product.yml)).
- For the deployment pipeline that promotes images through these environments,
  see [Infrastructure - Deployment/SYSTEM_DESIGN.md](../Infrastructure%20-%20Deployment/SYSTEM_DESIGN.md).
