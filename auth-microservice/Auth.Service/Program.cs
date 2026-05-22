using Auth.Service.Endpoints;
using Auth.Service.Infrastructure.Data.EntityFramework;
using Auth.Service.Services;
using ECommerce.Shared.HealthChecks;
using ECommerce.Shared.Observability;
using ECommerce.Shared.OpenApi;
using ECommerce.Shared.Qa;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<Microsoft.AspNetCore.Identity.IPasswordHasher<Auth.Service.Domain.User>, Microsoft.AspNetCore.Identity.PasswordHasher<Auth.Service.Domain.User>>();
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

if (QaSeedingExtensions.IsQaSeedingEnabled(app.Environment, app.Configuration))
{
    app.MigrateDatabase();
}

app.SeedQaData();

app.RegisterEndpoints();
app.RegisterServiceTokenEndpoint();
app.RegisterJwksEndpoint();

app.Run();

public partial class Program { }
