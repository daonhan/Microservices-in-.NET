output "namespace_name" {
  description = "Name of the Service Bus namespace."
  value       = azurerm_servicebus_namespace.this.name
}

output "namespace_fqdn" {
  description = "Service Bus namespace endpoint (for SDK connection)."
  value       = "${azurerm_servicebus_namespace.this.name}.servicebus.windows.net"
}

output "connection_string" {
  description = "Primary connection string (RootManageSharedAccessKey) for the namespace."
  value       = azurerm_servicebus_namespace.this.default_primary_connection_string
  sensitive   = true
}
