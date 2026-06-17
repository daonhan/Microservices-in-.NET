variable "name" {
  description = "Name of the virtual network."
  type        = string
}

variable "location" {
  description = "Azure region for the VNet and its subnets."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group that holds the network resources."
  type        = string
}

variable "address_space" {
  description = "Address space for the VNet (CIDR)."
  type        = list(string)
  default     = ["10.50.0.0/16"]
}

variable "aks_subnet_prefix" {
  description = "Address prefix for the AKS node subnet."
  type        = string
  default     = "10.50.0.0/20"
}

variable "private_endpoints_subnet_prefix" {
  description = "Address prefix for the private-endpoints subnet (SQL, Redis, etc.)."
  type        = string
  default     = "10.50.16.0/24"
}

variable "agents_subnet_prefix" {
  description = "Address prefix for the self-hosted agents subnet."
  type        = string
  default     = "10.50.17.0/24"
}

variable "tags" {
  description = "Tags applied to the network resources."
  type        = map(string)
  default     = {}
}
