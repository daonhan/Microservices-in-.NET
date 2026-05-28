// Log Analytics Workspace for the e-commerce platform.
//
// Acts as the central sink for AKS diagnostic logs and is referenced by Application Insights.

@description('Name of the Log Analytics Workspace.')
param workspaceName string

@description('Azure region.')
param location string

@description('Retention period in days (30-730).')
@minValue(30)
@maxValue(730)
param retentionInDays int = 30

@description('SKU for the workspace.')
@allowed([
  'PerGB2018'
  'Free'
  'CapacityReservation'
])
param sku string = 'PerGB2018'

@description('Cost-tier selector. Passthrough; cap behavior is controlled directly by dailyCapGb.')
@allowed([
  'minimal'
  'standard'
])
param costProfile string = 'standard'

@description('Daily ingestion cap in GB. Set to a positive value to cap; -1 (default) leaves the workspace uncapped.')
param dailyCapGb int = -1

@description('Resource tags.')
param tags object = {}

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: workspaceName
  location: location
  tags: tags
  properties: union({
    sku: {
      name: sku
    }
    retentionInDays: retentionInDays
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }, dailyCapGb > 0 ? {
    workspaceCapping: {
      dailyQuotaGb: dailyCapGb
    }
  } : {})
}

@description('Resource ID of the Log Analytics Workspace.')
output workspaceId string = workspace.id

@description('Name of the Log Analytics Workspace.')
output workspaceName string = workspace.name

@description('Customer ID (workspace GUID) — needed by Application Insights.')
output customerId string = workspace.properties.customerId
