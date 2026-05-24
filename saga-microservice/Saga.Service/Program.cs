using ECommerce.Shared.Authentication;
using ECommerce.Shared.HealthChecks;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Messaging;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Observability;
using ECommerce.Shared.OpenApi;
using ECommerce.Shared.Qa;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Saga.Service.Domain;
using Saga.Service.Domain.Abstractions;
using Saga.Service.Domain.OrderSaga;
using Saga.Service.Domain.RefundSaga;
using Saga.Service.Endpoints;
using Saga.Service.Features.OrderSaga.OrderCancelled;
using Saga.Service.Features.OrderSaga.OrderConfirmed;
using Saga.Service.Features.OrderSaga.OrderCreated;
using Saga.Service.Features.OrderSaga.PaymentAuthorized;
using Saga.Service.Features.OrderSaga.PaymentFailed;
using Saga.Service.Features.OrderSaga.PaymentRefunded;
using Saga.Service.Features.OrderSaga.PaymentVoided;
using Saga.Service.Features.OrderSaga.ShipmentCancelled;
using Saga.Service.Features.OrderSaga.ShipmentCreated;
using Saga.Service.Features.OrderSaga.ShipmentFailed;
using Saga.Service.Features.OrderSaga.StockCommitted;
using Saga.Service.Features.OrderSaga.StockReleased;
using Saga.Service.Features.OrderSaga.StockReservationFailed;
using Saga.Service.Features.OrderSaga.StockReserved;
using Saga.Service.Features.RefundSaga.PaymentRefunded;
using Saga.Service.Features.RefundSaga.RefundRequested;
using Saga.Service.Infrastructure.Data.EntityFramework;
using Saga.Service.Infrastructure.Observability;
using Saga.Service.Infrastructure.Outbox;
using Saga.Service.Infrastructure.Reaper;

var builder = WebApplication.CreateBuilder(args);

const string serviceName = "Saga";

builder.Services.AddSqlServerDatastore(builder.Configuration);

builder.Services.AddOutbox(builder.Configuration);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<SagaReaperOptions>(
    builder.Configuration.GetSection("Saga:Reaper"));
builder.Services.Configure<OrderSagaTimeoutOptions>(
    builder.Configuration.GetSection("Saga:OrderSaga"));
builder.Services.AddSingleton<OrderSagaTimeoutScheduler>();
builder.Services.AddScoped<EfOrderSagaTransitionRunner>();
builder.Services.AddScoped<ISagaTransitionRunner<OrderSagaStateSnapshot, Event>>(
    sp => sp.GetRequiredService<EfOrderSagaTransitionRunner>());
builder.Services.AddScoped<EfRefundSagaTransitionRunner>();
builder.Services.AddScoped<ISagaTransitionRunner<RefundSagaStateSnapshot, Event>>(
    sp => sp.GetRequiredService<EfRefundSagaTransitionRunner>());
builder.Services.AddHostedService<SagaReaperService>();

builder.Services
    .AddOrderCreatedSlice()
    .AddStockReservedSlice()
    .AddStockReservationFailedSlice()
    .AddPaymentAuthorizedSlice()
    .AddPaymentFailedSlice()
    .AddOrderConfirmedSlice()
    .AddStockCommittedSlice()
    .AddShipmentCreatedSlice()
    .AddShipmentFailedSlice()
    .AddStockReleasedSlice()
    .AddPaymentVoidedSlice()
    .AddPaymentRefundedSlice()
    .AddOrderCancelledSlice()
    .AddShipmentCancelledSlice()
    .AddRefundRequestedSlice()
    .AddRefundSagaPaymentRefundedSlice();

builder.AddPlatformOpenApi("saga");

builder.Services.AddPlatformEventBus(builder.Configuration)
    .AddPlatformEventPublisher(builder.Configuration)
    .AddPlatformSubscriberService(builder.Configuration);

builder.AddPlatformObservability(serviceName,
    customTracing: t => t.WithSqlInstrumentation().AddSource(SagaTelemetry.ActivitySourceName),
    customMetrics: m => m.AddMeter(SagaTelemetry.MeterName));

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
app.RegisterOperatorSagaEndpoints();

app.UseHttpsRedirection();

app.UseJwtAuthentication();

app.Run();

public partial class Program { }
