output "hostname" {
  description = "Redis hostname."
  value       = azurerm_redis_cache.this.hostname
}

output "ssl_port" {
  description = "Redis SSL port."
  value       = azurerm_redis_cache.this.ssl_port
}

output "primary_key" {
  description = "Redis primary access key."
  value       = azurerm_redis_cache.this.primary_access_key
  sensitive   = true
}

# StackExchange.Redis-format connection string (matches the Bicep redis.bicep output).
output "connection_string" {
  description = "Redis connection string for StackExchange.Redis."
  value       = "${azurerm_redis_cache.this.hostname}:${azurerm_redis_cache.this.ssl_port},password=${azurerm_redis_cache.this.primary_access_key},ssl=True,abortConnect=False"
  sensitive   = true
}
