@description('Name of the Azure Container Registry. Must be globally unique, alphanumeric, 5-50 chars.')
@minLength(5)
@maxLength(50)
param acrName string

@description('Azure region for the ACR.')
param location string

@description('SKU for the ACR. Use Basic/Standard for non-prod, Premium for prod (geo-replication, private endpoints).')
@allowed([
  'Basic'
  'Standard'
  'Premium'
])
param sku string = 'Standard'

@description('Whether the admin user is enabled. Disabled by default; AKS uses managed identity to pull images.')
param adminUserEnabled bool = false

@description('Tags applied to the ACR.')
param tags object = {}

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: acrName
  location: location
  tags: tags
  sku: {
    name: sku
  }
  properties: {
    adminUserEnabled: adminUserEnabled
    publicNetworkAccess: 'Enabled'
    zoneRedundancy: sku == 'Premium' ? 'Enabled' : 'Disabled'
  }
}

@description('Resource ID of the ACR.')
output acrId string = acr.id

@description('Login server for the ACR (e.g., myacr.azurecr.io).')
output acrLoginServer string = acr.properties.loginServer

@description('Name of the ACR.')
output acrName string = acr.name
