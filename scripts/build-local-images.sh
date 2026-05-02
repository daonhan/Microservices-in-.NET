#!/usr/bin/env bash
# Build all microservice Docker images for local Kubernetes practice.
#
# Tags every image as `local/<service>:${TAG:-latest}`, with the repo root
# as build context so shared-libs/, Directory.Build.props, and
# local-nuget-packages/ are visible during build.
#
# For Minikube: run `eval "$(minikube docker-env)"` BEFORE this script so
# images land in the cluster's image store.
#
# Usage:
#   ./scripts/build-local-images.sh                # tag = latest
#   TAG=dev ./scripts/build-local-images.sh        # custom tag
#   PARALLEL=1 ./scripts/build-local-images.sh     # parallel builds (xargs -P)

set -euo pipefail

# Run from repo root regardless of caller's CWD
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR/.."

TAG="${TAG:-latest}"
PARALLEL="${PARALLEL:-0}"

# name : context : image
SERVICES=(
  "product:product-microservice/Product.Service:productservice"
  "order:order-microservice/Order.Service:orderservice"
  "basket:basket-microservice/Basket.Service:basketservice"
  "auth:auth-microservice/Auth.Service:authservice"
  "inventory:inventory-microservice/Inventory.Service:inventoryservice"
  "shipping:shipping-microservice/Shipping.Service:shippingservice"
  "payment:payment-microservice/Payment.Service:paymentservice"
  "apigateway:api-gateway/ApiGateway:apigateway"
)

build_one() {
  local entry="$1"
  IFS=':' read -r name ctx image <<<"$entry"
  local ref="local/${image}:${TAG}"
  echo "==> Building ${ref} from ${ctx}/Dockerfile"
  docker build -f "${ctx}/Dockerfile" -t "${ref}" .
}

export -f build_one
export TAG

if [[ "$PARALLEL" == "1" ]]; then
  printf '%s\n' "${SERVICES[@]}" | xargs -I{} -P 4 bash -c 'build_one "$@"' _ {}
else
  for entry in "${SERVICES[@]}"; do build_one "$entry"; done
fi

echo
echo "All images built. Verify with: docker images | grep '^local/'"
