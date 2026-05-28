using '../main.bicep'

param environment = 'sandbox'
param costProfile = 'minimal'
param workload = 'ecom'
param location = 'southeastasia'

// Sandbox reuses an existing shared ACR (typically in ecom-shared-rg); main.bicep
// skips ACR provisioning when environment == 'sandbox'. Override at deploy time
// with --parameters acrName=<your-shared-acr-name>.
param acrName = 'ecomsharedacr0000'

param vnetAddressPrefix = '10.40.0.0/16'
param aksSubnetPrefix = '10.40.0.0/20'
param privateEndpointsSubnetPrefix = '10.40.16.0/24'
param agentsSubnetPrefix = '10.40.17.0/24'

param aksSystemNodeCount = 1
param aksSystemNodeVmSize = 'Standard_B2ms'
param kubernetesVersion = ''

param serviceCidr = '10.3.0.0/16'
param dnsServiceIP = '10.3.0.10'

// ── SQL ──────────────────────────────────────────────────────────────────────
// sqlAdminPassword must be supplied via --parameters sqlAdminPassword=<secret> or Key Vault ref at deploy time.
// Serverless SKU + auto-pause are selected by sql.bicep when costProfile == 'minimal'.
param sqlAdminLogin = 'sqladmin'

// ── Redis ─────────────────────────────────────────────────────────────────────
param redisSkuFamily = 'C'
param redisSkuName = 'Basic'
param redisSkuCapacity = 0

// ── Key Vault ─────────────────────────────────────────────────────────────────
param keyVaultSku = 'standard'

// ── Log Analytics ─────────────────────────────────────────────────────────────
// dailyCapGb is an integer; 1 is the smallest practical cap (~$2/month at PerGB2018).
param logRetentionDays = 30
param logDailyCapGb = 1

// ── App Insights ──────────────────────────────────────────────────────────────
param appInsightsSamplingPercentage = 10

// ── Service Bus ───────────────────────────────────────────────────────────────
param serviceBusSku = 'Standard'

// ── Budget ────────────────────────────────────────────────────────────────────
param budgetAmount = 100
param budgetContactEmails = ['your-email@example.com']
param budgetFirstThresholdPercent = 80
