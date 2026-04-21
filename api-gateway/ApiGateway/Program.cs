using ECommerce.Shared.Authentication;
using ECommerce.Shared.HealthChecks;
using ECommerce.Shared.Observability;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("ocelot.json", false, false);
builder.Services.AddOcelot(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.AddPlatformObservability("ApiGateway");
builder.Services.AddPlatformHealthChecks();

var app = builder.Build();

app.UsePrometheusExporter();
app.MapPlatformHealthChecks();
app.UseJwtAuthentication();
await app.UseOcelot();

app.Run();
