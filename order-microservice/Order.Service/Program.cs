using ECommerce.Shared.Authentication;
using ECommerce.Shared.HealthChecks;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Messaging;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using ECommerce.Shared.Observability;
using ECommerce.Shared.OpenApi;
using ECommerce.Shared.Qa;
using OpenTelemetry.Metrics;
using Order.Service.Endpoints;
using Order.Service.Infrastructure.Data.EntityFramework;
using Order.Service.IntegrationEvents.EventHandlers;
using Order.Service.IntegrationEvents.Events;

var builder = WebApplication.CreateBuilder(args);

const string serviceName = "Order";

builder.Services.AddSqlServerDatastore(builder.Configuration);

builder.Services.AddOutbox(builder.Configuration);

builder.Services.AddStackExchangeRedisCache(options =>
    options.Configuration = builder.Configuration["Redis:Configuration"] ?? "localhost:6379");

builder.Services.AddHttpClient<Order.Service.Infrastructure.Providers.IProductCatalogClient,
    Order.Service.Infrastructure.Providers.HttpProductCatalogClient>(client =>
{
    var baseUrl = builder.Configuration["ProductService:BaseUrl"]
        ?? "http://product-clusterip-service:8080";
    client.BaseAddress = new Uri(baseUrl);
});

builder.Services.AddScoped<Order.Service.Models.IProductPriceProvider, Order.Service.Infrastructure.Providers.RedisProductPriceProvider>();

builder.AddPlatformOpenApi("order");

builder.Services.AddPlatformEventBus(builder.Configuration)
    .AddPlatformEventPublisher(builder.Configuration)
    .AddPlatformSubscriberService(builder.Configuration)
    .AddEventHandler<ProductCreatedEvent, ProductCreatedEventHandler>()
    .AddEventHandler<ConfirmOrderCommand, ConfirmOrderCommandHandler>()
    .AddEventHandler<CancelOrderCommand, CancelOrderCommandHandler>();

builder.AddPlatformObservability(serviceName,
    customTracing: t => t.WithSqlInstrumentation(),
    customMetrics: m => m.AddView("products-per-order",
        new ExplicitBucketHistogramConfiguration { Boundaries = [1, 2, 5, 10] }));

builder.Services.AddPlatformHealthChecks()
    .AddSqlServerProbe(builder.Configuration.GetConnectionString("Default") ?? "")
    .AddRedisProbe(builder.Configuration["Redis:Configuration"] ?? "localhost:6379")
    .AddRabbitMqProbe(builder.Configuration["RabbitMq:HostName"] ?? "localhost");

builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddRequireServicePolicy();

var app = builder.Build();

app.UsePrometheusExporter();
app.MapPlatformHealthChecks();

if (QaSeedingExtensions.IsQaSeedingEnabled(app.Environment, app.Configuration))
{
    app.MigrateDatabase();
    app.ApplyOutboxMigrations();
}

app.SeedQaData();

app.UsePlatformOpenApi();

app.RegisterEndpoints();
app.RegisterInternalOutboxEndpoints();

app.UseHttpsRedirection();

app.UseJwtAuthentication();

app.Run();

public partial class Program { }
