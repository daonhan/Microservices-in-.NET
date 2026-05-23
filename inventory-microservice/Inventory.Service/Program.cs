using ECommerce.Shared.Authentication;
using ECommerce.Shared.HealthChecks;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Messaging;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using ECommerce.Shared.Observability;
using ECommerce.Shared.OpenApi;
using ECommerce.Shared.Qa;
using Inventory.Service.Contracts.Integration;
using Inventory.Service.Endpoints;
using Inventory.Service.Features.CreateBackorder;
using Inventory.Service.Features.GetStockItem;
using Inventory.Service.Features.GetStockMovements;
using Inventory.Service.Features.ListStockItems;
using Inventory.Service.Features.Restock;
using Inventory.Service.Features.SetThreshold;
using Inventory.Service.Infrastructure.Data.EntityFramework;
using Inventory.Service.IntegrationEvents.EventHandlers;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSqlServerDatastore(builder.Configuration);

builder.Services.AddOutbox(builder.Configuration);

builder.Services.AddPlatformEventBus(builder.Configuration)
    .AddPlatformEventPublisher(builder.Configuration)
    .AddPlatformSubscriberService(builder.Configuration)
    .AddEventHandler<ProductCreatedEvent, ProductCreatedEventHandler>()
    .AddEventHandler<ReserveStockCommand, ReserveStockCommandHandler>()
    .AddEventHandler<CommitStockCommand, CommitStockCommandHandler>()
    .AddEventHandler<ReleaseStockCommand, ReleaseStockCommandHandler>();

builder.Services.AddListStockItemsSlice();
builder.Services.AddGetStockItemSlice();
builder.Services.AddGetStockMovementsSlice();
builder.Services.AddRestockSlice();
builder.Services.AddSetThresholdSlice();
builder.Services.AddCreateBackorderSlice();

builder.AddPlatformObservability("Inventory",
    customTracing: t => t.WithSqlInstrumentation(),
    customMetrics: m => m.AddView("reservation-latency-ms",
        new ExplicitBucketHistogramConfiguration { Boundaries = [5, 25, 100, 500, 2000] }));

builder.Services.AddPlatformHealthChecks()
    .AddSqlServerProbe(builder.Configuration.GetConnectionString("Default") ?? "")
    .AddRabbitMqProbe(builder.Configuration["RabbitMq:HostName"] ?? "localhost");

builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddRequireServicePolicy();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Administrator", policy =>
        policy.RequireClaim("user_role", "Administrator"));
});

builder.AddPlatformOpenApi("inventory");

var app = builder.Build();

app.UsePrometheusExporter();
app.MapPlatformHealthChecks();
app.UsePlatformOpenApi();

if (QaSeedingExtensions.IsQaSeedingEnabled(app.Environment, app.Configuration))
{
    app.MigrateDatabase();
    app.ApplyOutboxMigrations();
}

app.SeedQaData();

app.MapListStockItems();
app.MapGetStockItem();
app.MapGetStockMovements();
app.MapRestock();
app.MapSetThreshold();
app.MapCreateBackorder();

app.RegisterEndpoints();
app.RegisterInternalOutboxEndpoints();

app.UseHttpsRedirection();

app.UseJwtAuthentication();

app.Run();

public partial class Program { }
