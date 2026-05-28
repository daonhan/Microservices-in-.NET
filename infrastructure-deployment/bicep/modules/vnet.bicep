@description('Name of the virtual network.')
param vnetName string

@description('Azure region for the VNet.')
param location string

@description('Address space for the VNet (CIDR).')
param addressPrefix string = '10.10.0.0/16'

@description('Address prefix for the AKS node subnet.')
param aksSubnetPrefix string = '10.10.0.0/20'

@description('Address prefix for the private endpoints subnet (SQL, Redis, etc.).')
param privateEndpointsSubnetPrefix string = '10.10.16.0/24'

@description('Address prefix for an optional self-hosted agents subnet.')
param agentsSubnetPrefix string = '10.10.17.0/24'

@description('Tags applied to the VNet.')
param tags object = {}

resource vnet 'Microsoft.Network/virtualNetworks@2024-01-01' = {
  name: vnetName
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [
        addressPrefix
      ]
    }
    subnets: [
      {
        name: 'aks-subnet'
        properties: {
          addressPrefix: aksSubnetPrefix
          privateEndpointNetworkPolicies: 'Disabled'
          serviceEndpoints: [
            {
              service: 'Microsoft.Sql'
            }
            {
              service: 'Microsoft.Storage'
            }
            {
              service: 'Microsoft.KeyVault'
            }
          ]
        }
      }
      {
        name: 'private-endpoints-subnet'
        properties: {
          addressPrefix: privateEndpointsSubnetPrefix
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
      {
        name: 'agents-subnet'
        properties: {
          addressPrefix: agentsSubnetPrefix
          privateEndpointNetworkPolicies: 'Enabled'
        }
      }
    ]
  }
}

@description('Resource ID of the VNet.')
output vnetId string = vnet.id

@description('Resource ID of the AKS node subnet.')
output aksSubnetId string = '${vnet.id}/subnets/aks-subnet'

@description('Resource ID of the private endpoints subnet.')
output privateEndpointsSubnetId string = '${vnet.id}/subnets/private-endpoints-subnet'

@description('Resource ID of the agents subnet.')
output agentsSubnetId string = '${vnet.id}/subnets/agents-subnet'
