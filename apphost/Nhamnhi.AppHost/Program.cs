// Local-dev Aspire overlay (ADR-0011). Local-only — never runs in Azure;
// docker-compose.yaml stays the AKS-parity path. Phase 1 tracer bullet:
// SQL Server + Redis + RabbitMQ infra and Basket as the only wired service.
var builder = DistributedApplication.CreateBuilder(args);

var rabbitUser = builder.AddParameter("rabbit-user", "guest");
var rabbitPassword = builder.AddParameter("rabbit-password", "guest", secret: true);

builder.AddSqlServer("sql");

var redis = builder.AddRedis("redis", port: 6379);

var rabbitmq = builder.AddRabbitMQ("rabbitmq", rabbitUser, rabbitPassword, port: 5672);

builder.AddProject<Projects.Basket_Service>("basket")
    .WithReference(redis)
    .WithReference(rabbitmq)
    .WaitFor(redis)
    .WaitFor(rabbitmq)
    .WithEnvironment("Redis__Configuration", "localhost:6379")
    .WithEnvironment("RabbitMq__HostName", "localhost");

builder.Build().Run();
