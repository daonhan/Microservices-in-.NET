using '../main.bicep'

param environment = 'dev'
param workload = 'ecom'
param location = 'eastus'

// ACR name must be globally unique. Override per-tenant in the deploy command if needed.
param acrName = 'ecomdevacr'
param acrSku = 'Basic'

param vnetAddressPrefix = '10.10.0.0/16'
param aksSubnetPrefix = '10.10.0.0/20'
param privateEndpointsSubnetPrefix = '10.10.16.0/24'
param agentsSubnetPrefix = '10.10.17.0/24'

param aksSystemNodeCount = 1
param aksSystemNodeVmSize = 'Standard_B2s'
param kubernetesVersion = ''

param serviceCidr = '10.0.0.0/16'
param dnsServiceIP = '10.0.0.10'

// ── SQL ──────────────────────────────────────────────────────────────────────
// sqlAdminPassword must be supplied via --parameters sqlAdminPassword=<secret> or Key Vault ref at deploy time.
param sqlAdminLogin = 'sqladmin'
param dbSkuName = 'Basic'
param dbSkuTier = 'Basic'

// ── Redis ─────────────────────────────────────────────────────────────────────
param redisSkuFamily = 'C'
param redisSkuName = 'Basic'
param redisSkuCapacity = 0

// ── Key Vault ─────────────────────────────────────────────────────────────────
param keyVaultSku = 'standard'

// ── Log Analytics ─────────────────────────────────────────────────────────────
param logRetentionDays = 30

// ── Service Bus ───────────────────────────────────────────────────────────────
param serviceBusSku = 'Standard'
