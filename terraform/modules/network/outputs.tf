output "vnet_id" {
  description = "Resource ID of the VNet."
  value       = azurerm_virtual_network.this.id
}

output "aks_subnet_id" {
  description = "Resource ID of the AKS node subnet."
  value       = azurerm_subnet.aks.id
}

output "private_endpoints_subnet_id" {
  description = "Resource ID of the private-endpoints subnet."
  value       = azurerm_subnet.private_endpoints.id
}

output "agents_subnet_id" {
  description = "Resource ID of the agents subnet."
  value       = azurerm_subnet.agents.id
}
