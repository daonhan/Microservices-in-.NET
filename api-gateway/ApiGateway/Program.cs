using ApiGateway.Infrastructure.Proxy;
using ApiGateway.Operator;
using ECommerce.Shared.Authentication;
using ECommerce.Shared.HealthChecks;
using ECommerce.Shared.Infrastructure.DeadLetter;
using ECommerce.Shared.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.AddConfiguredGateway();
OperatorModule.AddServices(builder);
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.AddPlatformObservability(
    "ApiGateway",
    customTracing: tracing => tracing.AddSource("Yarp.ReverseProxy"));
builder.Services.AddPlatformHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.ApplyDeadLetterMigrations();
}

app.UsePrometheusExporter();
app.MapPlatformHealthChecks();
app.UseJwtAuthentication();
OperatorModule.MapEndpoints(app);
await app.UseConfiguredGatewayAsync();

app.Run();

public partial class Program { }
