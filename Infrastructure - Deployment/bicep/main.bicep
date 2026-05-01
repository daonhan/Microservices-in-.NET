// Main orchestration template for the e-commerce microservices Azure foundation.
//
// Deploys: VNet (with AKS, private-endpoints, and agents subnets), ACR, and an AKS
// cluster wired to ACR via an AcrPull role assignment on the kubelet identity.
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

var commonTags = {
  workload: workload
  environment: environment
  managedBy: 'bicep'
}

var vnetName = '${workload}-${environment}-vnet'
var aksName = '${workload}-${environment}-aks'
var aksDnsPrefix = '${workload}-${environment}'

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
