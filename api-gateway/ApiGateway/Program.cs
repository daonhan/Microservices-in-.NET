using ECommerce.Shared.Authentication;
using ECommerce.Shared.Observability;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("ocelot.json", false, false);
builder.Services.AddOcelot(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddPlatformObservability("ApiGateway", builder.Configuration);

var app = builder.Build();

app.UsePrometheusExporter();
app.UseJwtAuthentication();
await app.UseOcelot();

app.Run();
