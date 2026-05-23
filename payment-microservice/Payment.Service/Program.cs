using ECommerce.Shared.Authentication;
using ECommerce.Shared.HealthChecks;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Messaging;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using ECommerce.Shared.Observability;
using ECommerce.Shared.OpenApi;
using ECommerce.Shared.Qa;
using Payment.Service.Contracts.Integration;
using Payment.Service.Domain.Abstractions;
using Payment.Service.Endpoints;
using Payment.Service.Infrastructure.Data.EntityFramework;
using Payment.Service.Infrastructure.Gateways;
using Payment.Service.IntegrationEvents.EventHandlers;
using Payment.Service.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSqlServerDatastore(builder.Configuration);

builder.Services.AddOutbox(builder.Configuration);

builder.Services.AddSingleton<IPaymentGateway, InMemoryPaymentGateway>();

builder.Services.AddPlatformEventBus(builder.Configuration)
    .AddPlatformEventPublisher(builder.Configuration)
    .AddPlatformSubscriberService(builder.Configuration)
    .AddEventHandler<OrderCreatedEvent, OrderCreatedEventHandler>()
    .AddEventHandler<AuthorizePaymentCommand, AuthorizePaymentCommandHandler>()
    .AddEventHandler<CapturePaymentCommand, CapturePaymentCommandHandler>()
    .AddEventHandler<VoidPaymentCommand, VoidPaymentCommandHandler>()
    .AddEventHandler<RefundPaymentCommand, RefundPaymentCommandHandler>();

builder.AddPlatformObservability("Payment",
    customTracing: t => t.WithSqlInstrumentation());

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

builder.Services.AddSingleton<PaymentMetrics>();

builder.AddPlatformOpenApi("payment");

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

// Force PaymentMetrics to be constructed at startup so the
// `payments_total` counter is registered with OpenTelemetry
// before any traffic flows.
app.Services.GetRequiredService<PaymentMetrics>();

app.RegisterEndpoints();
app.RegisterInternalOutboxEndpoints();

app.UseHttpsRedirection();

app.UseJwtAuthentication();

app.Run();

public partial class Program { }
