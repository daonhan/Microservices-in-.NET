// Local-dev Aspire overlay (ADR-0011). Local-only — never runs in Azure;
// docker-compose.yaml stays the AKS-parity path. Phase 2 fan-out:
// SQL Server + Redis + RabbitMQ infra and all 8 services + ApiGateway wired
// as Aspire resources with cross-service references (Order → Auth /jwks,
// Order → Product, Saga → Inventory, Gateway → 7 upstream clusters).
var builder = DistributedApplication.CreateBuilder(args);

var rabbitUser = builder.AddParameter("rabbit-user", "guest");
var rabbitPassword = builder.AddParameter("rabbit-password", "guest", secret: true);

var sql = builder.AddSqlServer("sql");
var authDb = sql.AddDatabase("auth");
var productDb = sql.AddDatabase("product");
var orderDb = sql.AddDatabase("order");
var inventoryDb = sql.AddDatabase("inventory");
var paymentDb = sql.AddDatabase("payment");
var shippingDb = sql.AddDatabase("shipping");
var sagaDb = sql.AddDatabase("saga");

var redis = builder.AddRedis("redis", port: 6379);

var rabbitmq = builder.AddRabbitMQ("rabbitmq", rabbitUser, rabbitPassword, port: 5672);

var auth = builder.AddProject<Projects.Auth_Service>("auth")
    .WithReference(authDb, connectionName: "Default")
    .WaitFor(sql);

var product = builder.AddProject<Projects.Product_Service>("product")
    .WithReference(productDb, connectionName: "Default")
    .WithReference(rabbitmq)
    .WaitFor(sql)
    .WaitFor(rabbitmq)
    .WaitFor(auth)
    .WithEnvironment("RabbitMq__HostName", "localhost")
    .WithEnvironment("Authentication__AuthMicroserviceBaseAddress", auth.GetEndpoint("http"));

var basket = builder.AddProject<Projects.Basket_Service>("basket")
    .WithReference(redis)
    .WithReference(rabbitmq)
    .WaitFor(redis)
    .WaitFor(rabbitmq)
    .WithEnvironment("Redis__Configuration", "localhost:6379")
    .WithEnvironment("RabbitMq__HostName", "localhost");

var inventory = builder.AddProject<Projects.Inventory_Service>("inventory")
    .WithReference(inventoryDb, connectionName: "Default")
    .WithReference(rabbitmq)
    .WaitFor(sql)
    .WaitFor(rabbitmq)
    .WaitFor(auth)
    .WithEnvironment("RabbitMq__HostName", "localhost")
    .WithEnvironment("Authentication__AuthMicroserviceBaseAddress", auth.GetEndpoint("http"));

var payment = builder.AddProject<Projects.Payment_Service>("payment")
    .WithReference(paymentDb, connectionName: "Default")
    .WithReference(rabbitmq)
    .WaitFor(sql)
    .WaitFor(rabbitmq)
    .WaitFor(auth)
    .WithEnvironment("RabbitMq__HostName", "localhost")
    .WithEnvironment("Authentication__AuthMicroserviceBaseAddress", auth.GetEndpoint("http"));

var shipping = builder.AddProject<Projects.Shipping_Service>("shipping")
    .WithReference(shippingDb, connectionName: "Default")
    .WithReference(rabbitmq)
    .WaitFor(sql)
    .WaitFor(rabbitmq)
    .WaitFor(auth)
    .WithEnvironment("RabbitMq__HostName", "localhost")
    .WithEnvironment("Authentication__AuthMicroserviceBaseAddress", auth.GetEndpoint("http"));

var order = builder.AddProject<Projects.Order_Service>("order")
    .WithReference(orderDb, connectionName: "Default")
    .WithReference(redis)
    .WithReference(rabbitmq)
    .WithReference(product)
    .WaitFor(sql)
    .WaitFor(redis)
    .WaitFor(rabbitmq)
    .WaitFor(auth)
    .WaitFor(product)
    .WithEnvironment("Redis__Configuration", "localhost:6379")
    .WithEnvironment("RabbitMq__HostName", "localhost")
    .WithEnvironment("Authentication__AuthMicroserviceBaseAddress", auth.GetEndpoint("http"))
    .WithEnvironment("ProductService__BaseUrl", product.GetEndpoint("http"));

builder.AddProject<Projects.Saga_Service>("saga")
    .WithReference(sagaDb, connectionName: "Default")
    .WithReference(rabbitmq)
    .WithReference(inventory)
    .WaitFor(sql)
    .WaitFor(rabbitmq)
    .WaitFor(auth)
    .WaitFor(inventory)
    .WithEnvironment("RabbitMq__HostName", "localhost")
    .WithEnvironment("Authentication__AuthMicroserviceBaseAddress", auth.GetEndpoint("http"));

builder.AddProject<Projects.ApiGateway>("gateway")
    .WithReference(auth)
    .WithReference(product)
    .WithReference(basket)
    .WithReference(order)
    .WithReference(inventory)
    .WithReference(payment)
    .WithReference(shipping)
    .WaitFor(auth)
    .WithEnvironment("Authentication__AuthMicroserviceBaseAddress", auth.GetEndpoint("http"))
    .WithEnvironment("ReverseProxy__Clusters__auth-cluster__Destinations__default__Address", auth.GetEndpoint("http"))
    .WithEnvironment("ReverseProxy__Clusters__product-cluster__Destinations__default__Address", product.GetEndpoint("http"))
    .WithEnvironment("ReverseProxy__Clusters__basket-cluster__Destinations__default__Address", basket.GetEndpoint("http"))
    .WithEnvironment("ReverseProxy__Clusters__order-cluster__Destinations__default__Address", order.GetEndpoint("http"))
    .WithEnvironment("ReverseProxy__Clusters__inventory-cluster__Destinations__default__Address", inventory.GetEndpoint("http"))
    .WithEnvironment("ReverseProxy__Clusters__payment-cluster__Destinations__default__Address", payment.GetEndpoint("http"))
    .WithEnvironment("ReverseProxy__Clusters__shipping-cluster__Destinations__default__Address", shipping.GetEndpoint("http"));

builder.Build().Run();
