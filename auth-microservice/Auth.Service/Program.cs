using Auth.Service.Endpoints;
using Auth.Service.Infrastructure.Data.EntityFramework;
using Auth.Service.Services;
using ECommerce.Shared.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSqlServerDatastore(builder.Configuration);
builder.Services.RegisterTokenService(builder.Configuration);

builder.Services.AddPlatformObservability("Auth", builder.Configuration,
    customTracing: t => t.WithSqlInstrumentation());

var app = builder.Build();

app.UsePrometheusExporter();

if (app.Environment.IsDevelopment())
{
    app.MigrateDatabase();
}

app.RegisterEndpoints();

app.Run();

public partial class Program { }
