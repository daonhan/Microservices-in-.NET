using ECommerce.Shared.Authentication;
using ECommerce.Shared.HealthChecks;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Messaging;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Observability;
using ECommerce.Shared.OpenApi;
using ECommerce.Shared.Qa;
using Payment.Service.Domain.Abstractions;
using Payment.Service.Features.AuthorizePaymentCommand;
using Payment.Service.Features.CapturePayment;
using Payment.Service.Features.CapturePaymentCommand;
using Payment.Service.Features.GetPaymentById;
using Payment.Service.Features.GetPaymentByOrder;
using Payment.Service.Features.OrderCreated;
using Payment.Service.Features.RefundPayment;
using Payment.Service.Features.RefundPaymentCommand;
using Payment.Service.Features.VoidPaymentCommand;
using Payment.Service.Infrastructure.Data.EntityFramework;
using Payment.Service.Infrastructure.Gateways;
using Payment.Service.Infrastructure.Observability;
using Payment.Service.Infrastructure.Outbox;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSqlServerDatastore(builder.Configuration);

builder.Services.AddOutbox(builder.Configuration);

builder.Services.AddSingleton<IPaymentGateway, InMemoryPaymentGateway>();

builder.Services.AddGetPaymentByIdSlice();
builder.Services.AddGetPaymentByOrderSlice();
builder.Services.AddCapturePaymentSlice();
builder.Services.AddRefundPaymentSlice();
builder.Services.AddAuthorizePaymentCommandSlice();
builder.Services.AddCapturePaymentCommandSlice();
builder.Services.AddVoidPaymentCommandSlice();
builder.Services.AddRefundPaymentCommandSlice();
builder.Services.AddOrderCreatedSlice();

builder.Services.AddScoped<MessageCorrelationContext>();
builder.Services.AddScoped<DomainEventOutboxInterceptor>();

builder.Services.AddPlatformEventBus(builder.Configuration)
    .AddPlatformEventPublisher(builder.Configuration)
    .AddPlatformSubscriberService(builder.Configuration);

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

app.MapGetPaymentById();
app.MapGetPaymentByOrder();
app.MapCapturePayment();
app.MapRefundPayment();
app.RegisterInternalOutboxEndpoints();

app.UseHttpsRedirection();

app.UseJwtAuthentication();

app.Run();

public partial class Program { }
