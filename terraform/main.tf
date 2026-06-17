# Phase 1 tracer: the entire CI/CD lane proven against the smallest resource —
# just the sbx2 resource group. Later phases add the network/aks/registry/sql/
# redis/servicebus/keyvault child modules under this same root.
resource "azurerm_resource_group" "sbx2" {
  name     = local.resource_group_name
  location = var.location
  tags     = local.common_tags
}
