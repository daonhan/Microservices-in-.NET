# Phase 1 tracer: the entire CI/CD lane proven against the smallest resource —
# just the sbx2 resource group. Phase 2 thickens it into an empty-but-ready
# cluster (network + aks + registry); Phase 3 adds the sql/redis/servicebus/
# keyvault data plane under this same root.
resource "azurerm_resource_group" "sbx2" {
  name     = local.resource_group_name
  location = var.location
  tags     = local.common_tags
}

# Phase 2 compute slice. Dependency flow: network → aks (consumes the AKS subnet)
# → registry (grants AcrPull to the cluster's kubelet identity).
module "network" {
  source = "./modules/network"

  name                = "${local.name_prefix}-vnet"
  location            = var.location
  resource_group_name = azurerm_resource_group.sbx2.name
  tags                = local.common_tags
}

module "aks" {
  source = "./modules/aks"

  name                         = "${local.name_prefix}-aks"
  location                     = var.location
  resource_group_name          = azurerm_resource_group.sbx2.name
  dns_prefix                   = local.name_prefix
  aks_subnet_id                = module.network.aks_subnet_id
  log_analytics_workspace_name = "${local.name_prefix}-logs"
  tags                         = local.common_tags
}

module "registry" {
  source = "./modules/registry"

  name                       = local.acr_name
  location                   = var.location
  resource_group_name        = azurerm_resource_group.sbx2.name
  kubelet_identity_object_id = module.aks.kubelet_identity_object_id
  tags                       = local.common_tags
}

# Phase 3 data plane. Self-contained: SQL (7 service databases), Redis, Service
# Bus (11 topics), and a provisioned-but-unwired Key Vault. Like the Bicep lane's
# sandbox profile these hang off the resource group over public endpoints rather
# than threading private endpoints through the network module.
module "sql" {
  source = "./modules/sql"

  name                = "${local.name_prefix}-sql"
  location            = var.location
  resource_group_name = azurerm_resource_group.sbx2.name
  tags                = local.common_tags
}

module "redis" {
  source = "./modules/redis"

  name                = "${local.name_prefix}-redis"
  location            = var.location
  resource_group_name = azurerm_resource_group.sbx2.name
  tags                = local.common_tags
}

module "servicebus" {
  source = "./modules/servicebus"

  name                = "${local.name_prefix}-sb"
  location            = var.location
  resource_group_name = azurerm_resource_group.sbx2.name
  tags                = local.common_tags
}

module "keyvault" {
  source = "./modules/keyvault"

  name                = "${local.name_prefix}-kv"
  location            = var.location
  resource_group_name = azurerm_resource_group.sbx2.name
  tags                = local.common_tags
}
