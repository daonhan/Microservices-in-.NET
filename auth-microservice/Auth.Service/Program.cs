using Auth.Service.Endpoints;
using Auth.Service.Infrastructure.Data.EntityFramework;
using Auth.Service.Services;
using ECommerce.Shared.HealthChecks;
using ECommerce.Shared.Observability;
using ECommerce.Shared.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSqlServerDatastore(builder.Configuration);
builder.Services.RegisterTokenService(builder.Configuration);

builder.AddPlatformObservability("Auth",
    customTracing: t => t.WithSqlInstrumentation());

builder.Services.AddPlatformHealthChecks()
    .AddSqlServerProbe(builder.Configuration.GetConnectionString("Default") ?? "");

builder.AddPlatformOpenApi("auth");

var app = builder.Build();

app.UsePrometheusExporter();
app.MapPlatformHealthChecks();
app.UsePlatformOpenApi();

if (app.Environment.IsDevelopment())
{
    app.MigrateDatabase();
}

app.RegisterEndpoints();

app.Run();

public partial class Program { }
