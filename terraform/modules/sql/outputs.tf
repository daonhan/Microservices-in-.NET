output "sql_server_name" {
  description = "Name of the logical SQL Server."
  value       = azurerm_mssql_server.this.name
}

output "sql_server_fqdn" {
  description = "Fully qualified domain name of the SQL Server."
  value       = azurerm_mssql_server.this.fully_qualified_domain_name
}

output "admin_login" {
  description = "SQL Server administrator login name."
  value       = var.admin_login
}

output "admin_password" {
  description = "Generated SQL Server administrator password (present only in state)."
  value       = random_password.admin.result
  sensitive   = true
}

# Full per-database connection strings (password embedded), keyed by service name.
# Iterating the database resources (not the var) makes this depend on them.
output "connection_strings" {
  description = "Per-database connection strings keyed by service name."
  value = {
    for name, db in azurerm_mssql_database.this :
    name => "Server=tcp:${azurerm_mssql_server.this.fully_qualified_domain_name},1433;Database=${db.name};User Id=${var.admin_login};Password=${random_password.admin.result};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30"
  }
  sensitive = true
}
