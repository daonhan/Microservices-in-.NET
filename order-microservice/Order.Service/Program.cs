using ECommerce.Shared.Authentication;
using ECommerce.Shared.HealthChecks;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Messaging;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Observability;
using ECommerce.Shared.OpenApi;
using ECommerce.Shared.Qa;
using OpenTelemetry.Metrics;
using Order.Service.Features.CancelOrder;
using Order.Service.Features.ConfirmOrder;
using Order.Service.Features.CreateOrder;
using Order.Service.Features.GetOrder;
using Order.Service.Features.ListOrders;
using Order.Service.Features.ProductCreated;
using Order.Service.Infrastructure.Data.EntityFramework;
using Order.Service.Infrastructure.Outbox;
using Order.Service.Infrastructure.Providers;

var builder = WebApplication.CreateBuilder(args);

const string serviceName = "Order";

builder.Services.AddSqlServerDatastore(builder.Configuration);

builder.Services.AddOutbox(builder.Configuration);

builder.Services.AddDomainEventOutbox();
builder.Services.AddInfrastructureProviders(builder.Configuration);

builder.Services.AddCreateOrderSlice();
builder.Services.AddGetOrderSlice();
builder.Services.AddListOrdersSlice();
builder.Services.AddConfirmOrderSlice();
builder.Services.AddCancelOrderSlice();
builder.Services.AddProductCreatedSlice();

builder.AddPlatformOpenApi("order");

builder.Services.AddPlatformEventBus(builder.Configuration)
    .AddPlatformEventPublisher(builder.Configuration)
    .AddPlatformSubscriberService(builder.Configuration);

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

app.MapCreateOrder();
app.MapGetOrder();
app.MapListOrders();
app.MapCancelOrder();
app.RegisterInternalOutboxEndpoints();

app.UseHttpsRedirection();

app.UseJwtAuthentication();

app.Run();

public partial class Program { }
