# Service Bus namespace + one topic per integration event flowing between the
# microservices. The topic set mirrors the Bicep servicebus.bicep list exactly.
# Standard SKU is the cheapest tier that supports topics (Basic is queues-only).
resource "azurerm_servicebus_namespace" "this" {
  name                = var.name
  location            = var.location
  resource_group_name = var.resource_group_name
  sku                 = var.sku
  minimum_tls_version = "1.2"
  tags                = var.tags
}

resource "azurerm_servicebus_topic" "this" {
  for_each = toset(var.topic_names)

  name                         = each.key
  namespace_id                 = azurerm_servicebus_namespace.this.id
  default_message_ttl          = "P14D"
  partitioning_enabled         = false
  requires_duplicate_detection = false
  support_ordering             = false
  max_size_in_megabytes        = 1024
}
