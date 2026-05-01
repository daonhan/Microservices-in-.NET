// Application Insights for the e-commerce platform.
//
// Workspace-based Application Insights instance linked to the Log Analytics Workspace.
// The connection string is output for injection into K8s secrets / pipeline variables.

@description('Name of the Application Insights component.')
param appInsightsName string

@description('Azure region.')
param location string

@description('Resource ID of the Log Analytics Workspace to link this instance to.')
param workspaceId string

@description('Application type.')
@allowed([
  'web'
  'other'
])
param applicationType string = 'web'

@description('Resource tags.')
param tags object = {}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  tags: tags
  properties: {
    Application_Type: applicationType
    WorkspaceResourceId: workspaceId
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
    RetentionInDays: 90
  }
}

@description('Application Insights Instrumentation Key (legacy; prefer connection string).')
output instrumentationKey string = appInsights.properties.InstrumentationKey

@description('Application Insights Connection String (used by APPLICATIONINSIGHTS_CONNECTION_STRING env var).')
output connectionString string = appInsights.properties.ConnectionString

@description('Resource ID of the Application Insights component.')
output appInsightsId string = appInsights.id

@description('Name of the Application Insights component.')
output appInsightsName string = appInsights.name
