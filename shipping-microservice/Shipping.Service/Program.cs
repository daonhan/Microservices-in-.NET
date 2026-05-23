using ECommerce.Shared.Authentication;
using ECommerce.Shared.HealthChecks;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Messaging;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Observability;
using ECommerce.Shared.OpenApi;
using ECommerce.Shared.Qa;
using Shipping.Service.Domain.Abstractions;
using Shipping.Service.Features.CancelShipment;
using Shipping.Service.Features.CancelShipmentCommand;
using Shipping.Service.Features.CreateShipmentCommand;
using Shipping.Service.Features.DeliverShipment;
using Shipping.Service.Features.DispatchShipment;
using Shipping.Service.Features.FailShipment;
using Shipping.Service.Features.GetCarrierQuotes;
using Shipping.Service.Features.GetShipmentById;
using Shipping.Service.Features.GetShipmentsByOrder;
using Shipping.Service.Features.ListShipments;
using Shipping.Service.Features.OrderConfirmed;
using Shipping.Service.Features.PackShipment;
using Shipping.Service.Features.PickShipment;
using Shipping.Service.Features.ProcessCarrierWebhook;
using Shipping.Service.Features.ReturnShipment;
using Shipping.Service.Infrastructure.Carriers;
using Shipping.Service.Infrastructure.Data.EntityFramework;
using Shipping.Service.Infrastructure.Observability;
using Shipping.Service.Infrastructure.Outbox;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSqlServerDatastore(builder.Configuration);

builder.Services.AddOutbox(builder.Configuration);

builder.Services.AddPlatformEventBus(builder.Configuration)
    .AddPlatformEventPublisher(builder.Configuration)
    .AddPlatformSubscriberService(builder.Configuration);

builder.AddPlatformObservability("Shipping",
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

builder.Services.AddSingleton<FakeCarrierDispatchRegistry>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ICarrierGateway, FakeExpressCarrierGateway>();
builder.Services.AddSingleton<ICarrierGateway, FakeGroundCarrierGateway>();
builder.Services.AddScoped<RateShoppingService>();
builder.Services.AddSingleton<ShippingMetrics>();

builder.Services.Configure<CarrierWebhookOptions>(options =>
{
    var section = builder.Configuration.GetSection(CarrierWebhookOptions.SectionName);
    var intervalValue = section["PollingIntervalSeconds"];
    if (!string.IsNullOrWhiteSpace(intervalValue)
        && int.TryParse(intervalValue, System.Globalization.CultureInfo.InvariantCulture, out var interval))
    {
        options.PollingIntervalSeconds = interval;
    }

    foreach (var secret in section.GetSection("SharedSecrets").GetChildren())
    {
        if (!string.IsNullOrWhiteSpace(secret.Value))
        {
            options.SharedSecrets[secret.Key] = secret.Value;
        }
    }
});

builder.Services.AddSingleton<CarrierPollingService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CarrierPollingService>());

builder.Services
    .AddGetShipmentsByOrderSlice()
    .AddGetShipmentByIdSlice()
    .AddListShipmentsSlice()
    .AddGetCarrierQuotesSlice()
    .AddPickShipmentSlice()
    .AddPackShipmentSlice()
    .AddDispatchShipmentSlice()
    .AddDeliverShipmentSlice()
    .AddFailShipmentSlice()
    .AddReturnShipmentSlice()
    .AddCancelShipmentSlice()
    .AddProcessCarrierWebhookSlice()
    .AddOrderConfirmedSlice()
    .AddCreateShipmentCommandSlice()
    .AddCancelShipmentCommandSlice();

builder.AddPlatformOpenApi("shipping");

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

app.MapGetShipmentsByOrder();
app.MapGetShipmentById();
app.MapListShipments();
app.MapGetCarrierQuotes();
app.MapPickShipment();
app.MapPackShipment();
app.MapDispatchShipment();
app.MapDeliverShipment();
app.MapFailShipment();
app.MapReturnShipment();
app.MapCancelShipment();
app.MapProcessCarrierWebhook();

app.RegisterInternalOutboxEndpoints();

app.UseHttpsRedirection();

app.UseJwtAuthentication();

app.Run();

public partial class Program { }
