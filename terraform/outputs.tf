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

# ── Phase 3 data plane ────────────────────────────────────────────────────────
# Connection strings/keys carry live credentials, so they are marked sensitive and
# feed the app-deploy phase's SBX2_* pod secrets. Key Vault is provisioned but not
# wired to pods, so only its address is surfaced.

output "sql_server_fqdn" {
  description = "FQDN of the sbx2 SQL Server."
  value       = module.sql.sql_server_fqdn
}

output "sql_connection_strings" {
  description = "Per-database SQL connection strings (password embedded), keyed by service name."
  value       = module.sql.connection_strings
  sensitive   = true
}

output "redis_connection_string" {
  description = "StackExchange.Redis connection string for the sbx2 cache."
  value       = module.redis.connection_string
  sensitive   = true
}

output "servicebus_connection_string" {
  description = "Service Bus namespace primary connection string (RootManageSharedAccessKey)."
  value       = module.servicebus.connection_string
  sensitive   = true
}

output "key_vault_uri" {
  description = "URI of the sbx2 Key Vault (provisioned, not yet wired to pods)."
  value       = module.keyvault.key_vault_uri
}

output "key_vault_name" {
  description = "Name of the sbx2 Key Vault."
  value       = module.keyvault.key_vault_name
}
