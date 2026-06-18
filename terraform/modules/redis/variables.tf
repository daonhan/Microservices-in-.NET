variable "name" {
  description = "Name of the Redis cache instance."
  type        = string
}

variable "location" {
  description = "Azure region for the Redis cache."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group that holds the cache."
  type        = string
}

variable "capacity" {
  description = "Redis cache capacity. 0 = C0 (the smallest Basic size)."
  type        = number
  default     = 0
}

variable "family" {
  description = "Redis SKU family. C = Basic/Standard, P = Premium."
  type        = string
  default     = "C"
}

variable "sku_name" {
  description = "Redis SKU. Basic matches the cost-minimizing sandbox profile."
  type        = string
  default     = "Basic"
}

variable "redis_version" {
  description = "Redis major version."
  type        = string
  default     = "6"
}

variable "tags" {
  description = "Tags applied to the cache."
  type        = map(string)
  default     = {}
}
