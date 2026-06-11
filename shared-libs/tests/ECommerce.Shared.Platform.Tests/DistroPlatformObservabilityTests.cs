using System.Net;
using ECommerce.Shared.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;

namespace ECommerce.Shared.Tests;

/// <summary>
/// Behavioural contracts of the <c>Exporter=AzureMonitor</c> (distro) branch of the
/// <c>AddPlatformObservability</c> composition seam. They guard against a future refactor silently
/// reintroducing double telemetry (SQL spans, log export) or a dead Prometheus endpoint.
/// A syntactically valid fake connection string is used — never a real App Insights resource.
/// </summary>
public sealed class DistroPlatformObservabilityTests
{
    private const string FakeConnectionString =
        "InstrumentationKey=00000000-0000-0000-0000-000000000000";

    [Fact]
    public void Given_AzureMonitor_When_seam_invokes_customTracing_Then_sql_instrumentation_is_suppressed()
    {
        bool? suppressedDuringInvocation = null;

        var services = new ServiceCollection();
        services.AddPlatformObservability(
            "test",
            AzureMonitorConfig(),
            customTracing: tracing =>
            {
                suppressedDuringInvocation = SqlInstrumentationSuppressor.IsSuppressed;
                tracing.WithSqlInstrumentation();
            });

        using var sp = services.BuildServiceProvider();
        // Resolving the provider runs the WithTracing configuration delegate (and our lambda).
        _ = sp.GetRequiredService<TracerProvider>();

        Assert.True(suppressedDuringInvocation);
        Assert.False(SqlInstrumentationSuppressor.IsSuppressed); // scope is restored afterwards
    }

    [Fact]
    public void Given_Otlp_When_seam_invokes_customTracing_Then_sql_instrumentation_is_not_suppressed()
    {
        bool? suppressedDuringInvocation = null;

        var services = new ServiceCollection();
        services.AddPlatformObservability(
            "test",
            OtlpConfig(),
            customTracing: tracing =>
            {
                suppressedDuringInvocation = SqlInstrumentationSuppressor.IsSuppressed;
                tracing.WithSqlInstrumentation();
            });

        using var sp = services.BuildServiceProvider();
        _ = sp.GetRequiredService<TracerProvider>();

        Assert.False(suppressedDuringInvocation);
    }

    [Fact]
    public async Task Given_AzureMonitor_When_UsePrometheusExporter_Then_metrics_endpoint_is_not_mapped()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(AzureMonitorConfigValues());
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.UsePrometheusExporter();

        await app.StartAsync();
        try
        {
            var response = await app.GetTestClient().GetAsync("/metrics");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Given_Otlp_When_UsePrometheusExporter_Then_metrics_endpoint_is_mapped()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(OtlpConfigValues());
        builder.WebHost.UseTestServer();
        builder.Services.AddPlatformObservability("test", builder.Configuration);

        var app = builder.Build();
        app.UsePrometheusExporter();

        await app.StartAsync();
        try
        {
            var response = await app.GetTestClient().GetAsync("/metrics");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public void Given_AzureMonitor_When_AddPlatformObservability_Then_manual_logging_provider_is_not_added()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(AzureMonitorConfigValues());

        builder.AddPlatformObservability("test");

        using var host = builder.Build();
        var openTelemetryLoggerProviders = host.Services
            .GetServices<ILoggerProvider>()
            .Count(p => p is OpenTelemetryLoggerProvider);

        // Only the distro's logging provider — the manual pipeline is skipped (no double export).
        Assert.Equal(1, openTelemetryLoggerProviders);
    }

    [Fact]
    public void Given_Otlp_When_AddPlatformObservability_Then_manual_logging_provider_is_added()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(OtlpConfigValues());

        builder.AddPlatformObservability("test");

        using var host = builder.Build();
        var openTelemetryLoggerProviders = host.Services
            .GetServices<ILoggerProvider>()
            .Count(p => p is OpenTelemetryLoggerProvider);

        Assert.Equal(1, openTelemetryLoggerProviders);
    }

    private static ConfigurationManager AzureMonitorConfig() => BuildConfig(AzureMonitorConfigValues());

    private static ConfigurationManager OtlpConfig() => BuildConfig(OtlpConfigValues());

    private static Dictionary<string, string?> AzureMonitorConfigValues() => new()
    {
        ["OpenTelemetry:Exporter"] = OpenTelemetryOptions.AzureMonitorExporter,
        ["OpenTelemetry:AzureMonitorConnectionString"] = FakeConnectionString,
        ["OpenTelemetry:SamplingRatio"] = "0.1",
    };

    private static Dictionary<string, string?> OtlpConfigValues() => new()
    {
        ["OpenTelemetry:OtlpExporterEndpoint"] = "http://localhost:4317",
        ["OpenTelemetry:SamplingRatio"] = "1.0",
    };

    private static ConfigurationManager BuildConfig(Dictionary<string, string?> values)
    {
        var manager = new ConfigurationManager();
        manager.AddInMemoryCollection(values);
        return manager;
    }
}
