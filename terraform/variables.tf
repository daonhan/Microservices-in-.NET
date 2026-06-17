variable "location" {
  description = "Azure region for all sbx2 resources."
  type        = string
  default     = "southeastasia"
}

variable "workload" {
  description = "Short workload identifier used in resource names."
  type        = string
  default     = "ecom"
}

variable "environment" {
  description = "Environment name. Drives naming and tag values."
  type        = string
  default     = "sbx2"
}
