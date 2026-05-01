using '../main.bicep'

param environment = 'prod'
param workload = 'ecom'
param location = 'eastus'

param acrName = 'ecomprodacr'
param acrSku = 'Premium'

param vnetAddressPrefix = '10.30.0.0/16'
param aksSubnetPrefix = '10.30.0.0/20'
param privateEndpointsSubnetPrefix = '10.30.16.0/24'
param agentsSubnetPrefix = '10.30.17.0/24'

param aksSystemNodeCount = 3
param aksSystemNodeVmSize = 'Standard_DS3_v2'
param kubernetesVersion = ''

param serviceCidr = '10.2.0.0/16'
param dnsServiceIP = '10.2.0.10'
