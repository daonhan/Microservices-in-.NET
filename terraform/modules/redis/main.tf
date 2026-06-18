# Azure Cache for Redis, shared by Basket and Order. Basic C0 (no replication, no
# SLA) is the cost-minimizing sandbox profile; SSL-only with the same allkeys-lru
# eviction policy as the Bicep redis.bicep.
resource "azurerm_redis_cache" "this" {
  name                 = var.name
  location             = var.location
  resource_group_name  = var.resource_group_name
  capacity             = var.capacity
  family               = var.family
  sku_name             = var.sku_name
  minimum_tls_version  = "1.2"
  non_ssl_port_enabled = false
  redis_version        = var.redis_version

  redis_configuration {
    maxmemory_policy = "allkeys-lru"
  }

  tags = var.tags
}
