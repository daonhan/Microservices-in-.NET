# Remote state for the sbx2 Terraform lane (azurerm backend, blob-lease locking).
#
# Partial backend: the storage coordinates and state key live in
# environments/sbx2.backend.hcl and are supplied at init time with
#   terraform init -backend-config=environments/sbx2.backend.hcl
#
# The state Storage Account is created out-of-band by bootstrap/bootstrap-tfstate.sh
# (the init chicken-and-egg). Keeping the globally-unique account name out of the
# root lets additional environments add their own backend config + state key later.
terraform {
  backend "azurerm" {}
}
