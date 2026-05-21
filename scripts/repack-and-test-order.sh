#!/usr/bin/env bash
# Re-pack ECommerce.Shared 2.24.0 (lazy RabbitMQ registration) and run Order tests.
# Prereqs: docker compose up sql rabbitmq redis -d
# Run from the repo root.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"

echo "=== Packing ECommerce.Shared 2.24.0 ==="
cd "$REPO_ROOT/shared-libs/ECommerce.Shared"
dotnet pack -c Release
dotnet nuget push bin/Release/ECommerce.Shared.2.24.0.nupkg -s "$REPO_ROOT/local-nuget-packages"

echo ""
echo "=== Cleaning Order bin/obj ==="
find "$REPO_ROOT/order-microservice" -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +

echo ""
echo "=== Building Order service ==="
cd "$REPO_ROOT/order-microservice"
dotnet restore
dotnet build --no-restore

echo ""
echo "=== Running Order tests ==="
dotnet test --no-build --logger "trx;LogFileName=test-results.trx"

echo ""
echo "=== Done — check test-results.trx for details ==="
