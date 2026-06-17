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
