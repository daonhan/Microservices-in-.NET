plugin "terraform" {
  enabled = true
  preset  = "recommended"
}

# Provider-aware rules for the all-azurerm codebase: invalid/deprecated args,
# bad SKU strings, invalid regions. `tflint --init` fetches it.
plugin "azurerm" {
  enabled = true
  version = "0.27.0"
  source  = "github.com/terraform-linters/tflint-ruleset-azurerm"
}
