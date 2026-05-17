using ECommerce.Shared.Authentication;
using ECommerce.Shared.HealthChecks;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Messaging;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Observability;
using ECommerce.Shared.OpenApi;
using ECommerce.Shared.Qa;
using OpenTelemetry.Metrics;
using Saga.Service.Endpoints;
using Saga.Service.Infrastructure.Data.EntityFramework;
using Saga.Service.IntegrationEvents;
using Saga.Service.IntegrationEvents.EventHandlers;
using Saga.Service.Models;

var builder = WebApplication.CreateBuilder(args);

const string serviceName = "Saga";

builder.Services.AddSqlServerDatastore(builder.Configuration);

builder.Services.AddOutbox(builder.Configuration);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<SagaOrchestratorOptions>(
    builder.Configuration.GetSection("Saga:Orchestrator"));
builder.Services.AddScoped<OrderSagaReplyProcessor>();

builder.AddPlatformOpenApi("saga");

builder.Services.AddPlatformEventBus(builder.Configuration)
    .AddPlatformEventPublisher(builder.Configuration)
    .AddPlatformSubscriberService(builder.Configuration)
    .AddEventHandler<OrderCreatedEvent, OrderCreatedEventHandler>()
    .AddEventHandler<StockReservedEvent, StockReservedEventHandler>()
    .AddEventHandler<StockReservationFailedEvent, StockReservationFailedEventHandler>()
    .AddEventHandler<PaymentAuthorizedEvent, PaymentAuthorizedEventHandler>()
    .AddEventHandler<OrderConfirmedEvent, OrderConfirmedEventHandler>()
    .AddEventHandler<StockCommittedEvent, StockCommittedEventHandler>()
    .AddEventHandler<ShipmentCreatedEvent, ShipmentCreatedEventHandler>();

builder.AddPlatformObservability(serviceName,
    customTracing: t => t.WithSqlInstrumentation());

builder.Services.AddPlatformHealthChecks()
    .AddSqlServerProbe(builder.Configuration.GetConnectionString("Default") ?? "")
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

app.UsePlatformOpenApi();

app.RegisterInternalOutboxEndpoints();

app.UseHttpsRedirection();

app.UseJwtAuthentication();

app.Run();

public partial class Program { }
