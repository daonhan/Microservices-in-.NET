using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Infrastructure.RabbitMq;
using ECommerce.Shared.Observability;
using OpenTelemetry.Metrics;
using Order.Service.Endpoints;
using Order.Service.Infrastructure.Data.EntityFramework;
using Order.Service.IntegrationEvents.EventHandlers;
using Order.Service.IntegrationEvents.Events;

var builder = WebApplication.CreateBuilder(args);

const string serviceName = "Order";

builder.Services.AddSqlServerDatastore(builder.Configuration);

builder.Services.AddOutbox(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddRabbitMqEventBus(builder.Configuration)
    .AddRabbitMqEventPublisher(builder.Configuration)
    .AddRabbitMqSubscriberService(builder.Configuration)
    .AddEventHandler<StockReservedEvent, StockReservedEventHandler>();

builder.Services.AddOpenTelemetryTracing(serviceName, builder.Configuration,
    traceBuilder => traceBuilder.WithSqlInstrumentation())
    .AddOpenTelemetryMetrics(serviceName, builder.Services,
        metricBuilder => metricBuilder.AddView("products-per-order",
            new ExplicitBucketHistogramConfiguration
            {
                Boundaries = [1, 2, 5, 10]
            }));

var app = builder.Build();

app.UsePrometheusExporter();

if (app.Environment.IsDevelopment())
{
    app.MigrateDatabase();
    app.ApplyOutboxMigrations();
}

app.UseSwagger();
app.UseSwaggerUI();

app.RegisterEndpoints();

app.UseHttpsRedirection();

app.Run();

public partial class Program { }
