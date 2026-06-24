# Backend coordinates for the sbx2 remote state (azurerm).
# The resource group, storage account, and container are created out-of-band by
# bootstrap/bootstrap-tfstate.sh. Supplied at init:
#   terraform init -backend-config=environments/sbx2.backend.hcl
#
# storage_account_name must match the account the bootstrap script created
# (globally unique, <=24 chars). Update it to the real name after bootstrap.
resource_group_name  = "rg-tfstate"
storage_account_name = "sttfstateecomsbx2"
container_name       = "tfstate"
key                  = "sbx2.tfstate"
use_azuread_auth     = true
use_oidc             = true
