variable "name" {
  description = "Name of the Key Vault. Globally unique, 3-24 chars."
  type        = string
}

variable "location" {
  description = "Azure region for the Key Vault."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group that holds the vault."
  type        = string
}

variable "sku_name" {
  description = "Key Vault SKU."
  type        = string
  default     = "standard"
}

variable "tags" {
  description = "Tags applied to the vault."
  type        = map(string)
  default     = {}
}
