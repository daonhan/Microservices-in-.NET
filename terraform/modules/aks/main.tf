# Log Analytics workspace backing the cluster's Container Insights (oms_agent).
# It lives in the aks module because AKS is its only consumer in this lane — the
# Terraform lane has no separate monitoring module (unlike the Bicep monitor.bicep
# which App Insights also shares). Capped daily ingestion keeps the sandbox cheap.
resource "azurerm_log_analytics_workspace" "this" {
  name                = var.log_analytics_workspace_name
  location            = var.location
  resource_group_name = var.resource_group_name
  sku                 = "PerGB2018"
  retention_in_days   = var.log_analytics_retention_days
  daily_quota_gb      = var.log_analytics_daily_quota_gb
  tags                = var.tags
}

# Single burstable-node cluster on the Free control-plane tier, with a
# system-assigned managed identity (the kubelet identity it derives gets AcrPull
# in the registry module). Azure CNI on the network module's AKS subnet mirrors
# the Bicep sandbox profile.
resource "azurerm_kubernetes_cluster" "this" {
  name                = var.name
  location            = var.location
  resource_group_name = var.resource_group_name
  dns_prefix          = var.dns_prefix
  kubernetes_version  = var.kubernetes_version
  sku_tier            = "Free"

  default_node_pool {
    name           = "systempool"
    vm_size        = var.node_vm_size
    node_count     = var.node_count
    max_pods       = var.max_pods
    vnet_subnet_id = var.aks_subnet_id
  }

  identity {
    type = "SystemAssigned"
  }

  # network_policy must be set at create time — adding it later forces a cluster
  # replace. "azure" is free on the Free tier and enables NetworkPolicy enforcement
  # so a compromised pod cannot reach all services unrestricted.
  network_profile {
    network_plugin    = "azure"
    network_policy    = "azure"
    load_balancer_sku = "standard"
    service_cidr      = var.service_cidr
    dns_service_ip    = var.dns_service_ip
  }

  oms_agent {
    log_analytics_workspace_id = azurerm_log_analytics_workspace.this.id
  }

  tags = var.tags
}
