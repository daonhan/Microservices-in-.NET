variable "name" {
  description = "Name of the Service Bus namespace. Globally unique."
  type        = string
}

variable "location" {
  description = "Azure region for the namespace."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group that holds the namespace."
  type        = string
}

variable "sku" {
  description = "Service Bus SKU. Standard is the cheapest tier supporting topics."
  type        = string
  default     = "Standard"
}

variable "topic_names" {
  description = "Topics to create — one per integration event."
  type        = list(string)
  default = [
    "order-created",
    "order-confirmed",
    "order-cancelled",
    "product-created",
    "product-price-updated",
    "stock-reserved",
    "stock-reservation-failed",
    "stock-committed",
    "payment-authorized",
    "payment-failed",
    "shipment-dispatched",
  ]
}

variable "tags" {
  description = "Tags applied to the namespace."
  type        = map(string)
  default     = {}
}
