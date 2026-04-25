using Basket.Service.Endpoints;
using Basket.Service.Infrastructure.Data;
using Basket.Service.Infrastructure.Data.Redis;
using Basket.Service.IntegrationEvents;
using Basket.Service.IntegrationEvents.EventHandlers;
using ECommerce.Shared.HealthChecks;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.RabbitMq;
using ECommerce.Shared.Observability;
using ECommerce.Shared.OpenApi;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<IBasketStore, RedisBasketStore>();
builder.Services.AddRabbitMqEventBus(builder.Configuration)
    .AddRabbitMqSubscriberService(builder.Configuration)
    .AddEventHandler<OrderCreatedEvent, OrderCreatedEventHandler>()
    .AddEventHandler<ProductPriceUpdatedEvent, ProductPriceUpdatedEventHandler>();

builder.Services.AddRedisCache(builder.Configuration);

builder.AddPlatformObservability("Basket",
    customMetrics: m => m.AddView("basket-size",
        new ExplicitBucketHistogramConfiguration { Boundaries = [0, 1, 3, 5, 10, 25] }));

builder.Services.AddPlatformHealthChecks()
    .AddRedisProbe(builder.Configuration["Redis:Configuration"] ?? "localhost:6379")
    .AddRabbitMqProbe(builder.Configuration["RabbitMq:HostName"] ?? "localhost");

builder.AddPlatformOpenApi("basket");

var app = builder.Build();
app.UsePrometheusExporter();
app.MapPlatformHealthChecks();
app.UsePlatformOpenApi();

app.RegisterEndpoints();

app.UseHttpsRedirection();

app.Run();
