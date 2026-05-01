// Azure Key Vault for the e-commerce platform.
//
// Provisioned for future secret management use.
// Azure RBAC authorization is enabled (preferred over access policies for AKS/managed-identity use).

@description('Name of the Key Vault.')
param keyVaultName string

@description('Azure region.')
param location string

@description('SKU tier for the Key Vault.')
@allowed([
  'standard'
  'premium'
])
param skuName string = 'standard'

@description('When true the vault will reject all network traffic except from allowed sources.')
param enableNetworkRestriction bool = false

@description('Resource tags.')
param tags object = {}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: skuName
    }
    tenantId: tenant().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: true
    publicNetworkAccess: enableNetworkRestriction ? 'Disabled' : 'Enabled'
    networkAcls: {
      defaultAction: enableNetworkRestriction ? 'Deny' : 'Allow'
      bypass: 'AzureServices'
    }
  }
}

@description('Resource ID of the Key Vault.')
output keyVaultId string = keyVault.id

@description('URI of the Key Vault.')
output keyVaultUri string = keyVault.properties.vaultUri

@description('Name of the Key Vault.')
output keyVaultName string = keyVault.name
