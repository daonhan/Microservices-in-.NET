using Azure.Monitor.OpenTelemetry.AspNetCore;
using ECommerce.Shared.Kernel.TelemetryConventions;
using ECommerce.Shared.Observability.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ECommerce.Shared.Observability;

public static class OpenTelemetryStartupExtensions
{
    public static IHostApplicationBuilder AddPlatformObservability(
        this IHostApplicationBuilder hostBuilder,
        string serviceName,
        Action<TracerProviderBuilder>? customTracing = null,
        Action<MeterProviderBuilder>? customMetrics = null)
    {
        hostBuilder.Services.AddPlatformObservability(
            serviceName, hostBuilder.Configuration, customTracing, customMetrics);

        var opts = new OpenTelemetryOptions();
        hostBuilder.Configuration
            .GetSection(OpenTelemetryOptions.OpenTelemetrySectionName)
            .Bind(opts);

        if (opts.UseAzureMonitor)
        {
            // The Azure Monitor distro (UseAzureMonitor) captures ILogger and ships logs to
            // Application Insights itself; skip the manual pipeline to avoid double log export.
            return hostBuilder;
        }

        var instanceId = System.Environment.GetEnvironmentVariable("HOSTNAME")
            ?? System.Net.Dns.GetHostName();

        hostBuilder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            logging.ParseStateValues = true;

            logging.SetResourceBuilder(ResourceBuilder.CreateDefault()
                .AddService(serviceName, serviceVersion: opts.ServiceVersion, serviceInstanceId: instanceId)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = opts.Environment
                }));

            if (!string.IsNullOrWhiteSpace(opts.LogsOtlpExporterEndpoint))
            {
                logging.AddOtlpExporter(o => o.Endpoint = new Uri(opts.LogsOtlpExporterEndpoint));
            }

            if (opts.EnableConsoleExporters)
            {
                logging.AddConsoleExporter();
            }
        });

        return hostBuilder;
    }

    public static OpenTelemetryBuilder AddPlatformObservability(
        this IServiceCollection services,
        string serviceName,
        IConfigurationManager configuration,
        Action<TracerProviderBuilder>? customTracing = null,
        Action<MeterProviderBuilder>? customMetrics = null)
    {
        var opts = new OpenTelemetryOptions();
        configuration.GetSection(OpenTelemetryOptions.OpenTelemetrySectionName).Bind(opts);

        var instanceId = System.Environment.GetEnvironmentVariable("HOSTNAME")
            ?? System.Net.Dns.GetHostName();

        services.AddSingleton(new MetricFactory(serviceName));

        var openTelemetry = services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(serviceName, serviceVersion: opts.ServiceVersion, serviceInstanceId: instanceId)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = opts.Environment
                }));

        if (opts.UseAzureMonitor)
        {
            return openTelemetry.AddAzureMonitorDistro(serviceName, opts, customTracing, customMetrics);
        }

        return openTelemetry
            .WithTracing(builder =>
            {
                builder
                    .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(opts.SamplingRatio)))
                    .AddAspNetCoreInstrumentation()
                    .AddSource(RabbitMqTelemetryNames.ActivitySourceName)
                    .AddSource(DeadLetterTelemetryNames.ActivitySourceName)
                    .AddSource(OutboxTelemetryNames.ActivitySourceName)
                    .AddOtlpExporter(o => o.Endpoint = new Uri(opts.OtlpExporterEndpoint));

                if (opts.EnableConsoleExporters)
                {
                    builder.AddConsoleExporter();
                }

                customTracing?.Invoke(builder);
            })
            .WithMetrics(builder =>
            {
                builder
                    .AddAspNetCoreInstrumentation()
                    .AddMeter(serviceName)
                    .AddMeter(DeadLetterTelemetryNames.MeterName)
                    .AddMeter(OutboxTelemetryNames.MeterName)
                    .AddPrometheusExporter();

                if (opts.EnableConsoleExporters)
                {
                    builder.AddConsoleExporter();
                }

                customMetrics?.Invoke(builder);
            });
    }

    /// <summary>
    /// Composes the cloud (<c>Exporter=AzureMonitor</c>) branch on top of the Azure Monitor
    /// OpenTelemetry Distro. <see cref="AzureMonitorOptionsExtensions.UseAzureMonitor(OpenTelemetryBuilder)"/>
    /// owns the trace/metric/log providers, the Azure Monitor exporters, Live Metrics, and
    /// auto-instrumentation for AspNetCore + HttpClient + SqlClient. This layers only the platform's
    /// custom Activity sources, custom Meters, and the per-service tracing/metrics lambdas on top —
    /// no AspNetCore instrumentation, OTLP/Prometheus exporter, or sampler (the distro owns those).
    /// </summary>
    private static OpenTelemetryBuilder AddAzureMonitorDistro(
        this OpenTelemetryBuilder openTelemetry,
        string serviceName,
        OpenTelemetryOptions opts,
        Action<TracerProviderBuilder>? customTracing,
        Action<MeterProviderBuilder>? customMetrics)
    {
        openTelemetry.UseAzureMonitor(azureMonitor =>
        {
            var connectionString = opts.ResolveAzureMonitorConnectionString();
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                azureMonitor.ConnectionString = connectionString;
            }

            azureMonitor.SamplingRatio = (float)opts.SamplingRatio;
        });

        return openTelemetry
            .WithTracing(builder =>
            {
                builder
                    .AddSource(RabbitMqTelemetryNames.ActivitySourceName)
                    .AddSource(DeadLetterTelemetryNames.ActivitySourceName)
                    .AddSource(OutboxTelemetryNames.ActivitySourceName);

                // SQL is auto-instrumented by the distro; suppress the per-service
                // WithSqlInstrumentation() so SQL spans are not registered twice.
                using (SqlInstrumentationSuppressor.Suppress())
                {
                    customTracing?.Invoke(builder);
                }
            })
            .WithMetrics(builder =>
            {
                builder
                    .AddMeter(serviceName)
                    .AddMeter(DeadLetterTelemetryNames.MeterName)
                    .AddMeter(OutboxTelemetryNames.MeterName);

                customMetrics?.Invoke(builder);
            });
    }

    public static OpenTelemetryBuilder AddOpenTelemetryTracing(this IServiceCollection services,
        string serviceName, IConfigurationManager configuration,
        Action<TracerProviderBuilder>? customTracing = null)
    {
        var openTelemetryOptions = new OpenTelemetryOptions();
        configuration.GetSection(OpenTelemetryOptions.OpenTelemetrySectionName)
            .Bind(openTelemetryOptions);

        return services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(serviceName))
            .WithTracing(builder =>
            {
                builder
                    .AddConsoleExporter()
                    .AddAspNetCoreInstrumentation()
                    .AddSource(RabbitMqTelemetryNames.ActivitySourceName)
                    .AddSource(OutboxTelemetryNames.ActivitySourceName)
                    .AddOtlpExporter(options => options.Endpoint =
                        new Uri(openTelemetryOptions.OtlpExporterEndpoint));

                customTracing?.Invoke(builder);
            });
    }

    public static TracerProviderBuilder WithSqlInstrumentation(this TracerProviderBuilder builder) =>
        SqlInstrumentationSuppressor.IsSuppressed ? builder : builder.AddSqlClientInstrumentation();

    public static OpenTelemetryBuilder AddOpenTelemetryMetrics(
        this OpenTelemetryBuilder openTelemetryBuilder,
        string serviceName, IServiceCollection services,
        Action<MeterProviderBuilder>? customMetrics = null)
    {
        services.AddSingleton(new MetricFactory(serviceName));

        return openTelemetryBuilder
            .WithMetrics(builder =>
            {
                builder
                    .AddConsoleExporter()
                    .AddAspNetCoreInstrumentation()
                    .AddMeter(serviceName)
                    .AddMeter(OutboxTelemetryNames.MeterName)
                    .AddPrometheusExporter();

                customMetrics?.Invoke(builder);
            });
    }

    public static void UsePrometheusExporter(this WebApplication webApplication)
    {
        var opts = new OpenTelemetryOptions();
        webApplication.Configuration
            .GetSection(OpenTelemetryOptions.OpenTelemetrySectionName)
            .Bind(opts);

        // On the distro branch metrics go to App Insights only; the Prometheus scrape
        // endpoint is not registered, so do not map a dead /metrics route.
        if (opts.UseAzureMonitor)
        {
            return;
        }

        webApplication.UseOpenTelemetryPrometheusScrapingEndpoint();
    }
}

/// <summary>
/// Thread-scoped ambient flag used by the Azure Monitor distro branch to suppress the per-service
/// <see cref="OpenTelemetryStartupExtensions.WithSqlInstrumentation"/> call while the distro's own
/// SqlClient instrumentation is active, so SQL spans are registered exactly once. On the OTLP branch
/// the flag is never set, so <c>WithSqlInstrumentation()</c> behaves as before.
/// </summary>
internal static class SqlInstrumentationSuppressor
{
    [ThreadStatic]
    private static bool _suppressed;

    public static bool IsSuppressed => _suppressed;

    public static IDisposable Suppress() => new Scope();

    private sealed class Scope : IDisposable
    {
        public Scope() => _suppressed = true;

        public void Dispose() => _suppressed = false;
    }
}
