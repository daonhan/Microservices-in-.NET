using ECommerce.Shared.HealthChecks;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Infrastructure.RabbitMq;
using ECommerce.Shared.Observability;
using ECommerce.Shared.OpenApi;
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

builder.AddPlatformOpenApi("order");

builder.Services.AddRabbitMqEventBus(builder.Configuration)
    .AddRabbitMqEventPublisher(builder.Configuration)
    .AddRabbitMqSubscriberService(builder.Configuration)
    .AddEventHandler<PaymentAuthorizedEvent, PaymentAuthorizedEventHandler>()
    .AddEventHandler<PaymentFailedEvent, PaymentFailedEventHandler>()
    .AddEventHandler<StockReservationFailedEvent, StockReservationFailedEventHandler>();

builder.AddPlatformObservability(serviceName,
    customTracing: t => t.WithSqlInstrumentation(),
    customMetrics: m => m.AddView("products-per-order",
        new ExplicitBucketHistogramConfiguration { Boundaries = [1, 2, 5, 10] }));

builder.Services.AddPlatformHealthChecks()
    .AddSqlServerProbe(builder.Configuration.GetConnectionString("Default") ?? "")
    .AddRabbitMqProbe(builder.Configuration["RabbitMq:HostName"] ?? "localhost");

var app = builder.Build();

app.UsePrometheusExporter();
app.MapPlatformHealthChecks();

if (app.Environment.IsDevelopment())
{
    app.MigrateDatabase();
    app.ApplyOutboxMigrations();
}

app.UsePlatformOpenApi();

app.RegisterEndpoints();

app.UseHttpsRedirection();

app.Run();

public partial class Program { }
