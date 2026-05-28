// Azure SQL Server + per-service databases for the e-commerce platform.
//
// Creates one logical SQL Server and a database for each service that requires SQL:
//   auth, order, product, inventory, shipping, payment, saga.
//
// Connection strings are exposed as outputs so callers can feed them to K8s secrets.

@description('Name of the logical SQL Server.')
param sqlServerName string

@description('Azure region for all resources.')
param location string

@description('SQL Server administrator login.')
param adminLogin string

@description('SQL Server administrator password.')
@secure()
param adminPassword string

@description('Databases to create. Each entry drives one Azure SQL Database resource.')
param databaseNames array = [
  'auth'
  'order'
  'product'
  'inventory'
  'shipping'
  'payment'
  'saga'
]

@description('Database SKU name. Use "Basic" for dev, "S1"/"S2" for staging/prod.')
param dbSkuName string = 'Basic'

@description('Database SKU tier. Use "Basic" for dev, "Standard" for staging/prod.')
param dbSkuTier string = 'Basic'

@description('Cost-tier selector. When "minimal", databases are deployed on the SQL Serverless tier (GP_S_Gen5) with auto-pause; otherwise the provisioned SKU above is used.')
@allowed([
  'minimal'
  'standard'
])
param costProfile string = 'standard'

@description('SQL Serverless minimum vCore capacity, passed as a string for json() conversion (e.g., "0.5"). Used only when costProfile == "minimal".')
param minCapacity string = '0.5'

@description('SQL Serverless auto-pause delay in minutes. Used only when costProfile == "minimal".')
@minValue(60)
@maxValue(10080)
param autoPauseDelay int = 60

@description('Resource tags.')
param tags object = {}

var isMinimal = costProfile == 'minimal'
var serverlessTier = 'GP_S_Gen5'
var serverlessSkuName = '${serverlessTier}_1'

resource sqlServer 'Microsoft.Sql/servers@2023-05-01-preview' = {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    administratorLogin: adminLogin
    administratorLoginPassword: adminPassword
    version: '12.0'
    publicNetworkAccess: 'Enabled' // switch to Disabled + private endpoint for prod hardening
    minimalTlsVersion: '1.2'
  }
}

// Allow Azure services to reach the SQL Server (required for AKS pods via public endpoint)
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-05-01-preview' = {
  parent: sqlServer
  name: 'AllowAllAzureIPs'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource databases 'Microsoft.Sql/servers/databases@2023-05-01-preview' = [
  for dbName in databaseNames: {
    parent: sqlServer
    name: dbName
    location: location
    tags: tags
    sku: isMinimal ? {
      name: serverlessSkuName
      tier: 'GeneralPurpose'
      family: 'Gen5'
      capacity: 1
    } : {
      name: dbSkuName
      tier: dbSkuTier
    }
    properties: union({
      collation: 'SQL_Latin1_General_CP1_CI_AS'
      maxSizeBytes: 2147483648 // 2 GB
    }, isMinimal ? {
      autoPauseDelay: autoPauseDelay
      minCapacity: json(minCapacity)
    } : {})
  }
]

@description('Fully qualified domain name of the SQL Server.')
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName

@description('Name of the logical SQL Server.')
output sqlServerName string = sqlServer.name

// Build connection string stubs; callers append the password from a secret store.
// Format: Server=tcp:{fqdn},1433;Database={db};User Id={login};Password=<inject>;Encrypt=True;TrustServerCertificate=False
@description('Connection string prefix (without password) for each database, keyed by db name.')
output connectionStringPrefix string = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;User Id=${adminLogin};Password='
