#!/usr/bin/env bash
# One-shot deploy of the local-stack microservices platform to a local
# Kubernetes cluster (Docker Desktop or Minikube).
#
# Applies infra -> observability -> microservices, with `kubectl wait`
# readiness gates between stages so dependent pods do not crash-loop.
#
# Usage:
#   ./scripts/apply-local-k8s.sh                       # full stack
#   SKIP_OBSERVABILITY=1 ./scripts/apply-local-k8s.sh  # infra + services only
#   TIMEOUT=600s ./scripts/apply-local-k8s.sh          # custom wait timeout
#   LOCAL_STORAGE_FIX=on   ...                         # force RWM->RWO rewrite
#   LOCAL_STORAGE_FIX=off  ...                         # disable detection
#   LOCAL_STORAGE_FIX=auto ...                         # default; rewrite if
#                                                      #   only local-path /
#                                                      #   hostpath provisioners
#                                                      #   are present
#   IMAGE_PREFIX=local     ...                         # default; replaces the
#                                                      #   <USERNAME> placeholder
#                                                      #   in microservice + gateway
#                                                      #   manifests at apply time
#
# Assumes you have already built local images (./scripts/build-local-images.sh).
# The `<USERNAME>/` placeholder in kubernetes/*-microservice.yaml +
# api-gateway.yaml is rewritten in-memory at apply time using IMAGE_PREFIX,
# so the manifests on disk stay registry-agnostic.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR/.."

TIMEOUT="${TIMEOUT:-300s}"
SKIP_OBSERVABILITY="${SKIP_OBSERVABILITY:-0}"
LOCAL_STORAGE_FIX="${LOCAL_STORAGE_FIX:-auto}"
IMAGE_PREFIX="${IMAGE_PREFIX:-local}"

apply_app_manifest() {
  # Rewrite `<USERNAME>/` -> `${IMAGE_PREFIX}/` in-memory; the file on disk is
  # left untouched so manifests stay registry-agnostic. When the prefix is
  # `local`, also inject `imagePullPolicy: IfNotPresent` so kubelet uses the
  # daemon's image cache instead of attempting to pull.
  local f="$1"
  if [[ "$IMAGE_PREFIX" == "local" ]]; then
    sed -e "s|<USERNAME>/|${IMAGE_PREFIX}/|g" \
        -e '/^[[:space:]]*image:[[:space:]]*local\//a\
          imagePullPolicy: IfNotPresent' "$f" | kubectl apply -f -
  else
    sed -e "s|<USERNAME>/|${IMAGE_PREFIX}/|g" "$f" | kubectl apply -f -
  fi
}

rwx_supported() {
  # Returns 0 if any StorageClass uses a provisioner that supports RWX.
  local provisioners
  provisioners="$(kubectl get storageclass -o jsonpath='{.items[*].provisioner}' 2>/dev/null || true)"
  [[ -z "$provisioners" ]] && return 0  # permissive on errors
  for p in $provisioners; do
    case "$p" in
      rancher.io/local-path|docker.io/hostpath|k8s.io/minikube-hostpath) ;;
      *) return 0 ;;
    esac
  done
  return 1
}

case "$LOCAL_STORAGE_FIX" in
  on)   REWRITE_SQL=1 ;;
  off)  REWRITE_SQL=0 ;;
  auto) if rwx_supported; then REWRITE_SQL=0; else REWRITE_SQL=1; fi ;;
  *)    echo "LOCAL_STORAGE_FIX must be on|off|auto" >&2; exit 2 ;;
esac

echo "==> 1/3 Infrastructure (sql, rabbitmq, redis)"
if [[ "$REWRITE_SQL" == "1" ]]; then
  echo "    rewriting sql.yaml: ReadWriteMany -> ReadWriteOnce (in-memory; file unchanged)"
  sed 's/ReadWriteMany/ReadWriteOnce/g' kubernetes/sql.yaml | kubectl apply -f -
else
  kubectl apply -f kubernetes/sql.yaml
fi
kubectl apply -f kubernetes/rabbitmq.yaml -f kubernetes/redis.yaml

echo "    waiting for infra pods to become Ready..."
kubectl wait --for=condition=ready pod -l app=mssql    --timeout="$TIMEOUT"
kubectl wait --for=condition=ready pod -l app=rabbitmq --timeout=180s
kubectl wait --for=condition=ready pod -l app=redis    --timeout=120s

if [[ "$SKIP_OBSERVABILITY" != "1" ]]; then
  echo "==> 2/3 Observability (otel, jaeger, prometheus, alertmanager, loki, grafana, exporters)"
  kubectl apply \
    -f kubernetes/otel-collector.yaml \
    -f kubernetes/jaeger.yaml \
    -f kubernetes/prometheus.yaml \
    -f kubernetes/alertmanager.yaml \
    -f kubernetes/loki.yaml \
    -f kubernetes/grafana.yaml \
    -f kubernetes/exporters.yaml
else
  echo "==> 2/3 Observability skipped (SKIP_OBSERVABILITY=1)"
fi

echo "==> 3/3 Microservices (image prefix: $IMAGE_PREFIX)"
# Glob excludes aks-dev-*.yml / aks-staging-*.yml / aks-prod-*.yml.
for f in kubernetes/*-microservice.yaml; do
  apply_app_manifest "$f"
done
apply_app_manifest kubernetes/api-gateway.yaml

# Auth dev-keys: in Development the auth service signs JWTs from PEM files
# at /app/dev-keys. The base manifest does not commit a Secret/volume for
# them (Development-only material), so we bootstrap it here.
DEV_KEY_DIR="auth-microservice/Auth.Service/dev-keys"
if [[ -f "$DEV_KEY_DIR/dev-private.pem" && -f "$DEV_KEY_DIR/dev-public.pem" ]]; then
  echo "    bootstrapping auth-dev-keys Secret + volume mount"
  kubectl create secret generic auth-dev-keys \
    --from-file="dev-private.pem=$DEV_KEY_DIR/dev-private.pem" \
    --from-file="dev-public.pem=$DEV_KEY_DIR/dev-public.pem" \
    --dry-run=client -o yaml | kubectl apply -f -

  kubectl patch deployment authservice --patch "$(cat <<'YAML'
spec:
  template:
    spec:
      volumes:
        - name: dev-keys
          secret:
            secretName: auth-dev-keys
      containers:
        - name: authservice
          volumeMounts:
            - name: dev-keys
              mountPath: /app/dev-keys
              readOnly: true
YAML
)"
else
  echo "    !! auth dev-keys not found at $DEV_KEY_DIR/. authservice will fail to start in Development." >&2
fi

echo "    waiting for microservice pods to become Ready..."
for app in productservice orderservice basketservice authservice inventoryservice shippingservice paymentservice sagaservice apigateway; do
  if ! kubectl wait --for=condition=ready pod -l "app=$app" --timeout="$TIMEOUT"; then
    echo "    !! $app did not become Ready within $TIMEOUT — recent events:" >&2
    kubectl get pods -l "app=$app"
    kubectl describe pods -l "app=$app" | tail -n 30
    exit 1
  fi
done

echo
echo "All pods Ready. Snapshot:"
kubectl get pods
echo
echo "Next: kubectl port-forward svc/apigateway-loadbalancer 8004:8004"
