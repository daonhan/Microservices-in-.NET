using Azure.Monitor.OpenTelemetry.Exporter;
using ECommerce.Shared.Infrastructure.RabbitMq;
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

            if (opts.UseAzureMonitor)
            {
                var connectionString = opts.ResolveAzureMonitorConnectionString();
                if (!string.IsNullOrWhiteSpace(connectionString))
                {
                    logging.AddAzureMonitorLogExporter(o => o.ConnectionString = connectionString);
                }
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

        return services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(serviceName, serviceVersion: opts.ServiceVersion, serviceInstanceId: instanceId)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = opts.Environment
                }))
            .WithTracing(builder =>
            {
                builder
                    .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(opts.SamplingRatio)))
                    .AddAspNetCoreInstrumentation()
                    .AddSource(RabbitMqTelemetry.ActivitySourceName)
                    .AddOtlpExporter(o => o.Endpoint = new Uri(opts.OtlpExporterEndpoint));

                if (opts.UseAzureMonitor)
                {
                    var connectionString = opts.ResolveAzureMonitorConnectionString();
                    if (!string.IsNullOrWhiteSpace(connectionString))
                    {
                        builder.AddAzureMonitorTraceExporter(o => o.ConnectionString = connectionString);
                    }
                }

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
                    .AddPrometheusExporter();

                if (opts.UseAzureMonitor)
                {
                    var connectionString = opts.ResolveAzureMonitorConnectionString();
                    if (!string.IsNullOrWhiteSpace(connectionString))
                    {
                        builder.AddAzureMonitorMetricExporter(o => o.ConnectionString = connectionString);
                    }
                }

                if (opts.EnableConsoleExporters)
                {
                    builder.AddConsoleExporter();
                }

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
                    .AddSource(RabbitMqTelemetry.ActivitySourceName)
                    .AddOtlpExporter(options => options.Endpoint =
                        new Uri(openTelemetryOptions.OtlpExporterEndpoint));

                customTracing?.Invoke(builder);
            });
    }

    public static TracerProviderBuilder WithSqlInstrumentation(this TracerProviderBuilder builder) =>
        builder.AddSqlClientInstrumentation();

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
                    .AddPrometheusExporter();

                customMetrics?.Invoke(builder);
            });
    }

    public static void UsePrometheusExporter(this WebApplication webApplication) =>
        webApplication.UseOpenTelemetryPrometheusScrapingEndpoint();
}
