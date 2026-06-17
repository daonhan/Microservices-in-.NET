# sbx2 network: VNet (10.50.0.0/16) with the same three-subnet layout as the
# Bicep lane's vnet.bicep — AKS nodes, private endpoints, and self-hosted agents.
# Subnets are standalone resources (the azurerm-recommended shape) rather than
# inline blocks so later phases can attach private endpoints without churn.
resource "azurerm_virtual_network" "this" {
  name                = var.name
  location            = var.location
  resource_group_name = var.resource_group_name
  address_space       = var.address_space
  tags                = var.tags
}

resource "azurerm_subnet" "aks" {
  name                 = "aks-subnet"
  resource_group_name  = var.resource_group_name
  virtual_network_name = azurerm_virtual_network.this.name
  address_prefixes     = [var.aks_subnet_prefix]

  # Mirror the Bicep aks-subnet service endpoints so the data plane (Phase 3) can
  # reach SQL / Storage / Key Vault over the Azure backbone.
  service_endpoints = [
    "Microsoft.Sql",
    "Microsoft.Storage",
    "Microsoft.KeyVault",
  ]
  private_endpoint_network_policies = "Disabled"
}

resource "azurerm_subnet" "private_endpoints" {
  name                              = "private-endpoints-subnet"
  resource_group_name               = var.resource_group_name
  virtual_network_name              = azurerm_virtual_network.this.name
  address_prefixes                  = [var.private_endpoints_subnet_prefix]
  private_endpoint_network_policies = "Disabled"
}

resource "azurerm_subnet" "agents" {
  name                              = "agents-subnet"
  resource_group_name               = var.resource_group_name
  virtual_network_name              = azurerm_virtual_network.this.name
  address_prefixes                  = [var.agents_subnet_prefix]
  private_endpoint_network_policies = "Enabled"
}
