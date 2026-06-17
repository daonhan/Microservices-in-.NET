output "resource_group_name" {
  description = "Name of the sbx2 resource group."
  value       = azurerm_resource_group.sbx2.name
}

output "resource_group_id" {
  description = "Resource ID of the sbx2 resource group."
  value       = azurerm_resource_group.sbx2.id
}

output "location" {
  description = "Region the sbx2 environment is provisioned in."
  value       = azurerm_resource_group.sbx2.location
}

# ── Phase 2 compute slice ─────────────────────────────────────────────────────

output "vnet_id" {
  description = "Resource ID of the sbx2 VNet."
  value       = module.network.vnet_id
}

output "aks_name" {
  description = "Name of the AKS cluster."
  value       = module.aks.aks_name
}

output "aks_fqdn" {
  description = "FQDN of the AKS API server."
  value       = module.aks.aks_fqdn
}

output "acr_login_server" {
  description = "Login server for the ACR (push/pull target for the app-deploy phase)."
  value       = module.registry.acr_login_server
}

output "acr_name" {
  description = "Name of the ACR."
  value       = module.registry.acr_name
}
