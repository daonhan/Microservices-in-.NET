using '../main.bicep'

param environment = 'staging'
param workload = 'ecom'
param location = 'eastus'

param acrName = 'ecomstagingacr'
param acrSku = 'Standard'

param vnetAddressPrefix = '10.20.0.0/16'
param aksSubnetPrefix = '10.20.0.0/20'
param privateEndpointsSubnetPrefix = '10.20.16.0/24'
param agentsSubnetPrefix = '10.20.17.0/24'

param aksSystemNodeCount = 2
param aksSystemNodeVmSize = 'Standard_DS2_v2'
param kubernetesVersion = ''

param serviceCidr = '10.1.0.0/16'
param dnsServiceIP = '10.1.0.10'

// ── SQL ──────────────────────────────────────────────────────────────────────
param sqlAdminLogin = 'sqladmin'
param dbSkuName = 'S1'
param dbSkuTier = 'Standard'

// ── Redis ─────────────────────────────────────────────────────────────────────
param redisSkuFamily = 'C'
param redisSkuName = 'Standard'
param redisSkuCapacity = 1

// ── Key Vault ─────────────────────────────────────────────────────────────────
param keyVaultSku = 'standard'

// ── Log Analytics ─────────────────────────────────────────────────────────────
param logRetentionDays = 60

// ── Service Bus ───────────────────────────────────────────────────────────────
param serviceBusSku = 'Standard'
