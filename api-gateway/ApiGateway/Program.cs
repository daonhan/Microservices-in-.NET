using ApiGateway.Gateway;
using ECommerce.Shared.Authentication;
using ECommerce.Shared.HealthChecks;
using ECommerce.Shared.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.AddConfiguredGateway();
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.AddPlatformObservability("ApiGateway");
builder.Services.AddPlatformHealthChecks();

var app = builder.Build();

app.UsePrometheusExporter();
app.MapPlatformHealthChecks();
app.UseJwtAuthentication();
await app.UseConfiguredGatewayAsync();

app.Run();

public partial class Program { }
