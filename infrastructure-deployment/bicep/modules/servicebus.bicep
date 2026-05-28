// Azure Service Bus namespace + topics for the e-commerce platform.
//
// Topics mirror the integration events flowing between microservices:
//   OrderCreated, OrderConfirmed, OrderCancelled, ProductCreated, ProductPriceUpdated,
//   StockReserved, StockReservationFailed, StockCommitted,
//   PaymentAuthorized, PaymentFailed, ShipmentDispatched.
//
// Each topic gets one default subscription ("default") so consumers can begin
// receiving messages immediately. Additional subscriptions can be added per-service.

@description('Name of the Service Bus namespace. Must be globally unique.')
param namespaceName string

@description('Azure region.')
param location string

@description('Service Bus SKU.')
@allowed([
  'Basic'
  'Standard'
  'Premium'
])
param skuName string = 'Standard'

@description('Topic names to create. Defaults cover all current integration events.')
param topicNames array = [
  'order-created'
  'order-confirmed'
  'order-cancelled'
  'product-created'
  'product-price-updated'
  'stock-reserved'
  'stock-reservation-failed'
  'stock-committed'
  'payment-authorized'
  'payment-failed'
  'shipment-dispatched'
]

@description('Resource tags.')
param tags object = {}

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: namespaceName
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuName
  }
  properties: {
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: false
  }
}

resource topics 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = [
  for topicName in topicNames: {
    parent: serviceBusNamespace
    name: topicName
    properties: {
      defaultMessageTimeToLive: 'P14D' // 14 days
      enablePartitioning: false
      requiresDuplicateDetection: false
      supportOrdering: false
      maxSizeInMegabytes: 1024
    }
  }
]

// Create a default subscription per topic
resource subscriptions 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = [
  for (topicName, i) in topicNames: {
    parent: topics[i]
    name: 'default'
    properties: {
      defaultMessageTimeToLive: 'P14D'
      lockDuration: 'PT1M'
      maxDeliveryCount: 10
      deadLetteringOnMessageExpiration: true
    }
  }
]

// Root manage shared access key — connection string for publishing services
resource rootKey 'Microsoft.ServiceBus/namespaces/authorizationRules@2022-10-01-preview' existing = {
  parent: serviceBusNamespace
  name: 'RootManageSharedAccessKey'
}

@description('Primary connection string for the Service Bus namespace.')
output primaryConnectionString string = rootKey.listKeys().primaryConnectionString

@description('Namespace endpoint (e.g., for SDK connection).')
output namespaceFqdn string = '${serviceBusNamespace.name}.servicebus.windows.net'

@description('Name of the Service Bus namespace.')
output namespaceName string = serviceBusNamespace.name
