# sbx2 — Terraform-owned greenfield sandbox, parallel to Bicep's `sandbox` (10.40).
# 10.50.0.0/16, southeastasia. Selected at plan/apply time with
#   terraform plan -var-file=environments/sbx2.tfvars
# See docs/plans/terraform-azure-iac-option.md.
location    = "southeastasia"
workload    = "ecom"
environment = "sbx2"
