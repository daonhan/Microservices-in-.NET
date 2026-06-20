#!/usr/bin/env bash
#
# One-time bootstrap for the Terraform sbx2 remote-state backend.
#
# Resolves the init chicken-and-egg: Terraform needs a Storage Account to hold
# state, but we do not want Terraform to manage its own backend. An admin runs
# this once to create a hardened state account, then `terraform init` can point
# at it via environments/sbx2.backend.hcl.
#
# Idempotent: re-running it leaves existing resources in place.
#
# Recommended target: the same subscription as sbx2, region southeastasia.
#
# Usage:
#   az login
#   az account set --subscription "<sbx2-subscription-id>"
#   ./bootstrap-tfstate.sh [-g rg-tfstate] [-l southeastasia] [-s <storage-account-name>] [-c tfstate]
set -euo pipefail

RESOURCE_GROUP="rg-tfstate"
LOCATION="southeastasia"
STORAGE_ACCOUNT="sttfstateecomsbx2"
CONTAINER="tfstate"

while getopts "g:l:s:c:h" opt; do
  case "${opt}" in
    g) RESOURCE_GROUP="${OPTARG}" ;;
    l) LOCATION="${OPTARG}" ;;
    s) STORAGE_ACCOUNT="${OPTARG}" ;;
    c) CONTAINER="${OPTARG}" ;;
    h) grep '^#' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option: -${OPTARG}" >&2; exit 2 ;;
  esac
done

echo "Bootstrapping Terraform state backend:"
echo "  resource group : ${RESOURCE_GROUP}"
echo "  location       : ${LOCATION}"
echo "  storage account: ${STORAGE_ACCOUNT}"
echo "  container      : ${CONTAINER}"

az group create \
  --name "${RESOURCE_GROUP}" \
  --location "${LOCATION}" \
  --tags managedBy=bootstrap purpose=tfstate env=sbx2 \
  --output none

# Hardened state account: TLS1.2 floor, no public blob access, shared-key access
# disabled (backend init/plan/apply and container ops all use AAD/OIDC, not the
# account key); blob versioning + soft-delete protect state history.
# NOTE: the bootstrap admin and the pipeline WIF identity each need the
# `Storage Blob Data Contributor` role on this account for data-plane access.
az storage account create \
  --name "${STORAGE_ACCOUNT}" \
  --resource-group "${RESOURCE_GROUP}" \
  --location "${LOCATION}" \
  --sku Standard_LRS \
  --kind StorageV2 \
  --min-tls-version TLS1_2 \
  --allow-blob-public-access false \
  --allow-shared-key-access false \
  --tags managedBy=bootstrap purpose=tfstate env=sbx2 \
  --output none

az storage account blob-service-properties update \
  --account-name "${STORAGE_ACCOUNT}" \
  --resource-group "${RESOURCE_GROUP}" \
  --enable-versioning true \
  --enable-delete-retention true \
  --delete-retention-days 7 \
  --enable-container-delete-retention true \
  --container-delete-retention-days 7 \
  --output none

# Container creation uses the caller's AAD identity (no account key needed).
az storage container create \
  --name "${CONTAINER}" \
  --account-name "${STORAGE_ACCOUNT}" \
  --auth-mode login \
  --output none

echo
echo "Done. Set environments/sbx2.backend.hcl -> storage_account_name = \"${STORAGE_ACCOUNT}\""
echo "Then: terraform init -backend-config=environments/sbx2.backend.hcl"
