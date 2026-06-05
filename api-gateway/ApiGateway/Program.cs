using ApiGateway.Features.Operator.BatchReplayFailures;
using ApiGateway.Features.Operator.DiscardFailure;
using ApiGateway.Features.Operator.GetFailureDetail;
using ApiGateway.Features.Operator.ListFailures;
using ApiGateway.Features.Operator.ReplayFailure;
using ApiGateway.Infrastructure.Polling;
using ApiGateway.Infrastructure.Proxy;
using ApiGateway.Infrastructure.Seeding;
using ECommerce.Shared.Authentication;
using ECommerce.Shared.HealthChecks;
using ECommerce.Shared.Infrastructure.DeadLetter;
using ECommerce.Shared.Infrastructure.Messaging;
using ECommerce.Shared.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.AddConfiguredGateway();
builder.Services.AddPlatformEventBus(builder.Configuration);
builder.Services.AddDeadLetter(builder.Configuration);
builder.Services.AddRequireOperatorPolicy();
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.AddPlatformObservability(
    "ApiGateway",
    customTracing: tracing => tracing.AddSource("Yarp.ReverseProxy"));
builder.Services.AddPlatformHealthChecks();

var pollerOptions = new OutboxPollerOptions();
builder.Configuration.GetSection(OutboxPollerOptions.SectionName).Bind(pollerOptions);
builder.Services.AddSingleton(pollerOptions);

if (pollerOptions.Enabled)
{
    builder.Services.AddHttpClient<IOutboxFailureClient, OutboxFailureClient>();
    builder.Services.AddHostedService<OutboxFailurePoller>();
}

builder.Services
    .AddListFailuresSlice()
    .AddGetFailureDetailSlice()
    .AddReplayFailureSlice()
    .AddDiscardFailureSlice()
    .AddBatchReplayFailuresSlice();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.ApplyDeadLetterMigrations();
    app.SeedQaDeadLetterFixture();
}

app.UsePrometheusExporter();
app.MapPlatformHealthChecks();
app.UseJwtAuthentication();

var operatorGroup = app.MapGroup("/operator/api/failures")
    .RequireAuthorization(AuthorizationPolicies.RequireOperatorPolicy);
operatorGroup.MapListFailuresSlice();
operatorGroup.MapGetFailureDetailSlice();
operatorGroup.MapReplayFailureSlice();
operatorGroup.MapDiscardFailureSlice();
operatorGroup.MapBatchReplayFailuresSlice();

await app.UseConfiguredGatewayAsync();

app.Run();

public partial class Program { }
