# ACR for the sbx2 lane. Basic SKU, admin user disabled — image pulls go through
# the AKS kubelet identity's AcrPull grant below, not registry credentials.
resource "azurerm_container_registry" "this" {
  name                = var.name
  location            = var.location
  resource_group_name = var.resource_group_name
  sku                 = var.sku
  admin_enabled       = false
  tags                = var.tags
}

# Grant the AKS kubelet identity AcrPull on this registry. Unlike Bicep's
# acr-pull-role.bicep, azurerm assigns a random role-assignment name itself, so
# no deterministic guid() seed is needed. skip_service_principal_aad_check avoids
# a replication-lag failure when the freshly-created kubelet identity is used.
resource "azurerm_role_assignment" "kubelet_acr_pull" {
  scope                            = azurerm_container_registry.this.id
  role_definition_name             = "AcrPull"
  principal_id                     = var.kubelet_identity_object_id
  skip_service_principal_aad_check = true
}
