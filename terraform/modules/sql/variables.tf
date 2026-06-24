variable "name" {
  description = "Name of the logical SQL Server. Globally unique, lowercase."
  type        = string
}

variable "location" {
  description = "Azure region for the SQL Server and its databases."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group that holds the server."
  type        = string
}

variable "admin_login" {
  description = "SQL Server administrator login name."
  type        = string
  default     = "sqladmin"
}

variable "database_names" {
  description = "Databases to create — one per SQL-backed service."
  type        = list(string)
  default = [
    "auth",
    "order",
    "product",
    "inventory",
    "shipping",
    "payment",
    "saga",
  ]
}

variable "sku_name" {
  description = "Database SKU. GP_S_Gen5_1 is SQL Serverless (1 vCore) with auto-pause — the cost-min sandbox profile."
  type        = string
  default     = "GP_S_Gen5_1"
}

variable "min_capacity" {
  description = "SQL Serverless minimum vCore capacity."
  type        = number
  default     = 0.5
}

variable "auto_pause_delay_in_minutes" {
  description = "SQL Serverless auto-pause delay in minutes."
  type        = number
  default     = 60
}

variable "max_size_gb" {
  description = "Maximum database size in GB."
  type        = number
  default     = 2
}

variable "tags" {
  description = "Tags applied to the server and databases."
  type        = map(string)
  default     = {}
}
