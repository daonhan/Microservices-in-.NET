variable "name" {
  description = "Name of the Azure Container Registry. Globally unique, alphanumeric, 5-50 chars."
  type        = string
}

variable "location" {
  description = "Azure region for the ACR."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group that holds the registry."
  type        = string
}

variable "sku" {
  description = "ACR SKU. Basic matches the cost-minimizing sandbox profile."
  type        = string
  default     = "Basic"
}

variable "kubelet_identity_object_id" {
  description = "Object ID of the AKS kubelet identity that is granted AcrPull."
  type        = string
}

variable "tags" {
  description = "Tags applied to the registry."
  type        = map(string)
  default     = {}
}
