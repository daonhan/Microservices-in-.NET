// Azure Cache for Redis for the e-commerce platform.
//
// A single shared Redis instance is used by Basket and Order services.
// Connection string (host:port,password=...,ssl=True,abortConnect=False) is output
// for injection into K8s secrets.

@description('Name of the Redis cache instance.')
param redisCacheName string

@description('Azure region.')
param location string

@description('Redis SKU family: C (Classic) or P (Premium).')
@allowed([
  'C'
  'P'
])
param skuFamily string = 'C'

@description('Redis SKU name.')
@allowed([
  'Basic'
  'Standard'
  'Premium'
])
param skuName string = 'Basic'

@description('Redis cache capacity (0-6 for Basic/Standard, 1-5 for Premium).')
@minValue(0)
@maxValue(6)
param skuCapacity int = 0

@description('Redis version.')
@allowed([
  '6'
  '7'
])
param redisVersion string = '6'

@description('Resource tags.')
param tags object = {}

resource redis 'Microsoft.Cache/redis@2023-08-01' = {
  name: redisCacheName
  location: location
  tags: tags
  properties: {
    sku: {
      family: skuFamily
      name: skuName
      capacity: skuCapacity
    }
    redisVersion: redisVersion
    enableNonSslPort: false
    minimumTlsVersion: '1.2'
    redisConfiguration: {
      'maxmemory-policy': 'allkeys-lru'
    }
  }
}

@description('Redis hostname.')
output redisHostName string = redis.properties.hostName

@description('Redis SSL port.')
output redisSslPort int = redis.properties.sslPort

@description('Redis primary access key (for K8s secret injection).')
output redisPrimaryKey string = redis.listKeys().primaryKey

@description('Connection string for StackExchange.Redis / Azure Cache for Redis.')
output connectionString string = '${redis.properties.hostName}:${redis.properties.sslPort},password=${redis.listKeys().primaryKey},ssl=True,abortConnect=False'
