// Main orchestration template for the e-commerce microservices Azure platform.
//
// Deploys:
//   - VNet (AKS, private-endpoints, agents subnets)
//   - ACR + AcrPull role assignment
//   - AKS cluster
//   - Azure SQL Server + 6 service databases
//   - Azure Cache for Redis
//   - Azure Key Vault
//   - Log Analytics Workspace
//   - Application Insights
//   - Azure Service Bus namespace + topics
//
// Per-environment values come from parameters/{dev,staging,prod}.bicepparam.

targetScope = 'resourceGroup'

@description('Environment name. Drives naming and tag values.')
@allowed([
  'dev'
  'staging'
  'prod'
])
param environment string

@description('Short workload identifier used in resource names.')
@minLength(2)
@maxLength(12)
param workload string = 'ecom'

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Globally unique ACR name (alphanumeric, 5-50 chars).')
@minLength(5)
@maxLength(50)
param acrName string

@description('SKU for the ACR.')
@allowed([
  'Basic'
  'Standard'
  'Premium'
])
param acrSku string = 'Standard'

@description('VNet address space.')
param vnetAddressPrefix string = '10.10.0.0/16'

@description('AKS subnet prefix.')
param aksSubnetPrefix string = '10.10.0.0/20'

@description('Private endpoints subnet prefix.')
param privateEndpointsSubnetPrefix string = '10.10.16.0/24'

@description('Agents subnet prefix.')
param agentsSubnetPrefix string = '10.10.17.0/24'

@description('Number of nodes in the AKS system node pool.')
@minValue(1)
@maxValue(100)
param aksSystemNodeCount int = 2

@description('VM size for the AKS system node pool.')
param aksSystemNodeVmSize string = 'Standard_DS2_v2'

@description('Kubernetes version (empty = AKS-managed default).')
param kubernetesVersion string = ''

@description('Service CIDR for AKS in-cluster Services.')
param serviceCidr string = '10.0.0.0/16'

@description('AKS DNS service IP (must be inside serviceCidr).')
param dnsServiceIP string = '10.0.0.10'

// ── SQL ──────────────────────────────────────────────────────────────────────

@description('SQL Server administrator login name.')
param sqlAdminLogin string = 'sqladmin'

@description('SQL Server administrator password.')
@secure()
param sqlAdminPassword string

@description('Database SKU name (Basic for dev, S1/S2 for staging/prod).')
param dbSkuName string = 'Basic'

@description('Database SKU tier (Basic for dev, Standard for staging/prod).')
param dbSkuTier string = 'Basic'

// ── Redis ────────────────────────────────────────────────────────────────────

@description('Redis SKU family (C = Classic, P = Premium).')
@allowed(['C', 'P'])
param redisSkuFamily string = 'C'

@description('Redis SKU name.')
@allowed(['Basic', 'Standard', 'Premium'])
param redisSkuName string = 'Basic'

@description('Redis SKU capacity (0-6).')
@minValue(0)
@maxValue(6)
param redisSkuCapacity int = 0

// ── Key Vault ─────────────────────────────────────────────────────────────────

@description('Key Vault SKU tier.')
@allowed(['standard', 'premium'])
param keyVaultSku string = 'standard'

// ── Log Analytics ─────────────────────────────────────────────────────────────

@description('Log Analytics retention in days.')
@minValue(30)
@maxValue(730)
param logRetentionDays int = 30

// ── Service Bus ───────────────────────────────────────────────────────────────

@description('Service Bus SKU.')
@allowed(['Basic', 'Standard', 'Premium'])
param serviceBusSku string = 'Standard'

var commonTags = {
  workload: workload
  environment: environment
  managedBy: 'bicep'
}

var vnetName = '${workload}-${environment}-vnet'
var aksName = '${workload}-${environment}-aks'
var aksDnsPrefix = '${workload}-${environment}'
var sqlServerName = '${workload}-${environment}-sql'
var redisCacheName = '${workload}-${environment}-redis'
var keyVaultName = '${workload}-${environment}-kv'
var logWorkspaceName = '${workload}-${environment}-logs'
var appInsightsName = '${workload}-${environment}-ai'
var serviceBusName = '${workload}-${environment}-sb'

module vnet 'modules/vnet.bicep' = {
  name: 'vnet-deploy'
  params: {
    vnetName: vnetName
    location: location
    addressPrefix: vnetAddressPrefix
    aksSubnetPrefix: aksSubnetPrefix
    privateEndpointsSubnetPrefix: privateEndpointsSubnetPrefix
    agentsSubnetPrefix: agentsSubnetPrefix
    tags: commonTags
  }
}

module acr 'modules/acr.bicep' = {
  name: 'acr-deploy'
  params: {
    acrName: acrName
    location: location
    sku: acrSku
    adminUserEnabled: false
    tags: commonTags
  }
}

module aks 'modules/aks.bicep' = {
  name: 'aks-deploy'
  params: {
    aksName: aksName
    location: location
    dnsPrefix: aksDnsPrefix
    kubernetesVersion: kubernetesVersion
    aksSubnetId: vnet.outputs.aksSubnetId
    systemNodeCount: aksSystemNodeCount
    systemNodeVmSize: aksSystemNodeVmSize
    serviceCidr: serviceCidr
    dnsServiceIP: dnsServiceIP
    tags: commonTags
  }
}

module acrPull 'modules/acr-pull-role.bicep' = {
  name: 'aks-acr-pull-role'
  params: {
    acrName: acr.outputs.acrName
    principalId: aks.outputs.kubeletIdentityObjectId
    assignmentSeed: aks.outputs.aksId
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql-deploy'
  params: {
    sqlServerName: sqlServerName
    location: location
    adminLogin: sqlAdminLogin
    adminPassword: sqlAdminPassword
    dbSkuName: dbSkuName
    dbSkuTier: dbSkuTier
    tags: commonTags
  }
}

module redis 'modules/redis.bicep' = {
  name: 'redis-deploy'
  params: {
    redisCacheName: redisCacheName
    location: location
    skuFamily: redisSkuFamily
    skuName: redisSkuName
    skuCapacity: redisSkuCapacity
    tags: commonTags
  }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault-deploy'
  params: {
    keyVaultName: keyVaultName
    location: location
    skuName: keyVaultSku
    tags: commonTags
  }
}

module monitor 'modules/monitor.bicep' = {
  name: 'monitor-deploy'
  params: {
    workspaceName: logWorkspaceName
    location: location
    retentionInDays: logRetentionDays
    tags: commonTags
  }
}

module appInsights 'modules/appinsights.bicep' = {
  name: 'appinsights-deploy'
  params: {
    appInsightsName: appInsightsName
    location: location
    workspaceId: monitor.outputs.workspaceId
    tags: commonTags
  }
}

module serviceBus 'modules/servicebus.bicep' = {
  name: 'servicebus-deploy'
  params: {
    namespaceName: serviceBusName
    location: location
    skuName: serviceBusSku
    tags: commonTags
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

@description('Resource ID of the deployed VNet.')
output vnetId string = vnet.outputs.vnetId

@description('Resource ID of the AKS subnet (for downstream slices: SQL, Redis private endpoints).')
output aksSubnetId string = vnet.outputs.aksSubnetId

@description('Resource ID of the private-endpoints subnet (for downstream slices).')
output privateEndpointsSubnetId string = vnet.outputs.privateEndpointsSubnetId

@description('Login server for the ACR.')
output acrLoginServer string = acr.outputs.acrLoginServer

@description('Name of the ACR.')
output acrName string = acr.outputs.acrName

@description('Name of the AKS cluster.')
output aksName string = aks.outputs.aksName

@description('FQDN of the AKS API server.')
output aksFqdn string = aks.outputs.aksFqdn

@description('FQDN of the SQL Server.')
output sqlServerFqdn string = sql.outputs.sqlServerFqdn

@description('SQL connection string prefix (append password to complete).')
output sqlConnectionStringPrefix string = sql.outputs.connectionStringPrefix

@description('Redis connection string (host:port,password=...,ssl=True).')
output redisConnectionString string = redis.outputs.connectionString

@description('URI of the Key Vault.')
output keyVaultUri string = keyVault.outputs.keyVaultUri

@description('Application Insights Connection String (APPLICATIONINSIGHTS_CONNECTION_STRING).')
output appInsightsConnectionString string = appInsights.outputs.connectionString

@description('Service Bus primary connection string.')
output serviceBusConnectionString string = serviceBus.outputs.primaryConnectionString
