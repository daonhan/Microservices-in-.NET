locals {
  # Mirrors the Bicep "${workload}-${environment}-<kind>" naming (e.g. ecom-sbx2-rg,
  # the Terraform-owned parallel of Bicep's ecom-sandbox-rg).
  resource_group_name = "${var.workload}-${var.environment}-rg"

  # azurerm has no provider-level default tags, so Terraform-owned resources carry
  # this common set (env=sbx2 / managedBy=terraform makes the lane obvious in the
  # portal). Merge into each resource's `tags`.
  common_tags = {
    env       = var.environment
    workload  = var.workload
    managedBy = "terraform"
  }
}
