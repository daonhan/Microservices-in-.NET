# Authentication is supplied entirely through ARM_* environment variables, so the
# same configuration works in both runners without embedding credentials:
#   - locally: `az login` (Azure CLI auth) + `export ARM_SUBSCRIPTION_ID=...`
#   - in Azure Pipelines: the WIF service connection exports ARM_CLIENT_ID /
#     ARM_OIDC_TOKEN / ARM_TENANT_ID / ARM_SUBSCRIPTION_ID with ARM_USE_OIDC=true.
#
# Note: azurerm has no provider-level `default_tags` (unlike the AWS provider), so
# the common tag set lives in locals.tf and is merged into each resource's `tags`.
provider "azurerm" {
  features {}
}
