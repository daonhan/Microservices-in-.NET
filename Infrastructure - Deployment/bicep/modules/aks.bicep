@description('Name of the AKS cluster.')
param aksName string

@description('Azure region for the AKS cluster.')
param location string

@description('DNS prefix for the AKS API server.')
param dnsPrefix string

@description('Kubernetes version. Leave empty for the AKS-managed default for the region.')
param kubernetesVersion string = ''

@description('Resource ID of the subnet where AKS nodes will be deployed.')
param aksSubnetId string

@description('Number of nodes in the system node pool.')
@minValue(1)
@maxValue(100)
param systemNodeCount int = 2

@description('VM size for the system node pool.')
param systemNodeVmSize string = 'Standard_DS2_v2'

@description('Maximum pods per node.')
@minValue(30)
@maxValue(250)
param maxPods int = 30

@description('Network plugin. Azure CNI is recommended; kubenet is acceptable for cost-sensitive dev.')
@allowed([
  'azure'
  'kubenet'
])
param networkPlugin string = 'azure'

@description('Service CIDR for in-cluster Services. Must not overlap VNet address space.')
param serviceCidr string = '10.0.0.0/16'

@description('DNS service IP. Must be inside serviceCidr.')
param dnsServiceIP string = '10.0.0.10'

@description('Tags applied to the AKS cluster.')
param tags object = {}

resource aks 'Microsoft.ContainerService/managedClusters@2024-05-01' = {
  name: aksName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  sku: {
    name: 'Base'
    tier: 'Free'
  }
  properties: {
    dnsPrefix: dnsPrefix
    kubernetesVersion: empty(kubernetesVersion) ? null : kubernetesVersion
    enableRBAC: true
    agentPoolProfiles: [
      {
        name: 'systempool'
        mode: 'System'
        count: systemNodeCount
        vmSize: systemNodeVmSize
        osType: 'Linux'
        osSKU: 'Ubuntu'
        type: 'VirtualMachineScaleSets'
        vnetSubnetID: aksSubnetId
        maxPods: maxPods
        enableAutoScaling: false
      }
    ]
    networkProfile: {
      networkPlugin: networkPlugin
      loadBalancerSku: 'standard'
      serviceCidr: serviceCidr
      dnsServiceIP: dnsServiceIP
    }
    apiServerAccessProfile: {
      enablePrivateCluster: false
    }
  }
}

@description('Resource ID of the AKS cluster.')
output aksId string = aks.id

@description('Name of the AKS cluster.')
output aksName string = aks.name

@description('FQDN of the AKS API server.')
output aksFqdn string = aks.properties.fqdn

@description('Object ID of the AKS kubelet identity (for ACR pull and other role assignments).')
output kubeletIdentityObjectId string = aks.properties.identityProfile.kubeletidentity.objectId
