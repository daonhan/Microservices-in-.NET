using ECommerce.Shared.Authentication;
using ECommerce.Shared.HealthChecks;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Infrastructure.RabbitMq;
using ECommerce.Shared.Observability;
using Shipping.Service.Carriers;
using Shipping.Service.Endpoints;
using Shipping.Service.Infrastructure.Data.EntityFramework;
using Shipping.Service.IntegrationEvents;
using Shipping.Service.IntegrationEvents.EventHandlers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSqlServerDatastore(builder.Configuration);

builder.Services.AddOutbox(builder.Configuration);

builder.Services.AddRabbitMqEventBus(builder.Configuration)
    .AddRabbitMqEventPublisher(builder.Configuration)
    .AddRabbitMqSubscriberService(builder.Configuration)
    .AddEventHandler<OrderConfirmedEvent, OrderConfirmedEventHandler>()
    .AddEventHandler<OrderCancelledEvent, OrderCancelledEventHandler>()
    .AddEventHandler<StockCommittedEvent, StockCommittedEventHandler>();

builder.AddPlatformObservability("Shipping",
    customTracing: t => t.WithSqlInstrumentation());

builder.Services.AddPlatformHealthChecks()
    .AddSqlServerProbe(builder.Configuration.GetConnectionString("Default") ?? "")
    .AddRabbitMqProbe(builder.Configuration["RabbitMq:HostName"] ?? "localhost");

builder.Services.AddJwtAuthentication(builder.Configuration);

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
