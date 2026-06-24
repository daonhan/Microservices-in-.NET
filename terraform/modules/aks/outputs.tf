output "aks_id" {
  description = "Resource ID of the AKS cluster."
  value       = azurerm_kubernetes_cluster.this.id
}

output "aks_name" {
  description = "Name of the AKS cluster."
  value       = azurerm_kubernetes_cluster.this.name
}

output "aks_fqdn" {
  description = "FQDN of the AKS API server."
  value       = azurerm_kubernetes_cluster.this.fqdn
}

output "kubelet_identity_object_id" {
  description = "Object ID of the kubelet identity (granted AcrPull by the registry module)."
  value       = azurerm_kubernetes_cluster.this.kubelet_identity[0].object_id
}

output "log_analytics_workspace_id" {
  description = "Resource ID of the Log Analytics workspace backing Container Insights."
  value       = azurerm_log_analytics_workspace.this.id
}
