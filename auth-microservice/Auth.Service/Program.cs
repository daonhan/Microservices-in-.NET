using Auth.Service.Domain;
using Auth.Service.Features.GetJwks;
using Auth.Service.Features.GetOpenIdConfiguration;
using Auth.Service.Features.IssueServiceToken;
using Auth.Service.Features.Login;
using Auth.Service.Infrastructure.Data.EntityFramework;
using Auth.Service.Infrastructure.Signing;
using ECommerce.Shared.HealthChecks;
using ECommerce.Shared.Observability;
using ECommerce.Shared.OpenApi;
using ECommerce.Shared.Qa;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddAuthDatastore(builder.Configuration)
                .AddSigningInfrastructure(builder.Configuration)
                .AddLoginSlice()
                .AddIssueServiceTokenSlice(builder.Configuration)
                .AddGetJwksSlice()
                .AddGetOpenIdConfigurationSlice();

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

app.MapLogin();
app.MapIssueServiceToken();
app.MapGetJwks();
app.MapGetOpenIdConfiguration();

app.Run();

public partial class Program { }
