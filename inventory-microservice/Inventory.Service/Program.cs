using ECommerce.Shared.Authentication;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Infrastructure.RabbitMq;
using ECommerce.Shared.Observability;
using Inventory.Service.Endpoints;
using Inventory.Service.Infrastructure.Data.EntityFramework;
using Inventory.Service.IntegrationEvents;
using Inventory.Service.IntegrationEvents.EventHandlers;

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

builder.Services.AddOpenTelemetryTracing("Inventory", builder.Configuration,
    traceBuilder => traceBuilder.WithSqlInstrumentation())
    .AddOpenTelemetryMetrics("Inventory", builder.Services);

builder.Services.AddJwtAuthentication(builder.Configuration);

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Administrator", policy =>
        policy.RequireClaim("user_role", "Administrator"));
});

var app = builder.Build();

app.UsePrometheusExporter();

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
