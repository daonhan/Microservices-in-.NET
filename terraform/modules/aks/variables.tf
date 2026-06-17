variable "name" {
  description = "Name of the AKS cluster."
  type        = string
}

variable "location" {
  description = "Azure region for the AKS cluster and its Log Analytics workspace."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group that holds the cluster."
  type        = string
}

variable "dns_prefix" {
  description = "DNS prefix for the AKS API server."
  type        = string
}

variable "kubernetes_version" {
  description = "Kubernetes version. null leaves the AKS-managed default for the region."
  type        = string
  default     = null
}

variable "aks_subnet_id" {
  description = "Resource ID of the subnet the node pool is attached to (from the network module)."
  type        = string
}

variable "node_count" {
  description = "Number of nodes in the system node pool."
  type        = number
  default     = 1
}

variable "node_vm_size" {
  description = "VM size for the system node pool. Burstable B-series keeps the sandbox cheap."
  type        = string
  default     = "Standard_B2ms"
}

variable "max_pods" {
  description = "Maximum pods per node."
  type        = number
  default     = 30
}

variable "service_cidr" {
  description = "Service CIDR for in-cluster Services. Must not overlap the VNet address space."
  type        = string
  default     = "10.3.0.0/16"
}

variable "dns_service_ip" {
  description = "DNS service IP. Must sit inside service_cidr."
  type        = string
  default     = "10.3.0.10"
}

variable "log_analytics_workspace_name" {
  description = "Name of the Log Analytics workspace backing Container Insights."
  type        = string
}

variable "log_analytics_retention_days" {
  description = "Log Analytics retention in days."
  type        = number
  default     = 30
}

variable "log_analytics_daily_quota_gb" {
  description = "Log Analytics daily ingestion cap in GB (cost control)."
  type        = number
  default     = 1
}

variable "tags" {
  description = "Tags applied to the cluster and workspace."
  type        = map(string)
  default     = {}
}
