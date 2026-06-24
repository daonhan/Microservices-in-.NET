output "acr_id" {
  description = "Resource ID of the ACR."
  value       = azurerm_container_registry.this.id
}

output "acr_login_server" {
  description = "Login server for the ACR (e.g. ecomsbx2acr.azurecr.io)."
  value       = azurerm_container_registry.this.login_server
}

output "acr_name" {
  description = "Name of the ACR."
  value       = azurerm_container_registry.this.name
}
