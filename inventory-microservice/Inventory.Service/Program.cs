using ECommerce.Shared.Authentication;
using ECommerce.Shared.HealthChecks;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Infrastructure.RabbitMq;
using ECommerce.Shared.Observability;
using Inventory.Service.Endpoints;
using Inventory.Service.Infrastructure.Data.EntityFramework;
using Inventory.Service.IntegrationEvents;
using Inventory.Service.IntegrationEvents.EventHandlers;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSqlServerDatastore(builder.Configuration);

builder.Services.AddOutbox(builder.Configuration);

builder.Services.AddRabbitMqEventBus(builder.Configuration)
    .AddRabbitMqEventPublisher(builder.Configuration)
    .AddRabbitMqSubscriberService(builder.Configuration)
    .AddEventHandler<ProductCreatedEvent, ProductCreatedEventHandler>()
    .AddEventHandler<OrderCreatedEvent, OrderCreatedEventHandler>()
    .AddEventHandler<OrderConfirmedEvent, OrderConfirmedEventHandler>()
    .AddEventHandler<OrderCancelledEvent, OrderCancelledEventHandler>();

builder.AddPlatformObservability("Inventory",
    customTracing: t => t.WithSqlInstrumentation(),
    customMetrics: m => m.AddView("reservation-latency-ms",
        new ExplicitBucketHistogramConfiguration { Boundaries = [5, 25, 100, 500, 2000] }));

builder.Services.AddPlatformHealthChecks()
    .AddSqlServerProbe(builder.Configuration.GetConnectionString("Default") ?? "")
    .AddRabbitMqProbe(builder.Configuration["RabbitMq:HostName"] ?? "localhost");

builder.Services.AddJwtAuthentication(builder.Configuration);

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Administrator", policy =>
        policy.RequireClaim("user_role", "Administrator"));
});

var app = builder.Build();

app.UsePrometheusExporter();
app.MapPlatformHealthChecks();

if (app.Environment.IsDevelopment())
{
    app.MigrateDatabase();
    app.ApplyOutboxMigrations();
}

app.RegisterEndpoints();

app.UseHttpsRedirection();

app.UseJwtAuthentication();

app.Run();

public partial class Program { }
