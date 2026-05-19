using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;

namespace ECommerce.Shared.Observability;

/// <summary>
/// Local-dev Aspire overlay defaults. Wraps — does not replace — the existing
/// <see cref="OpenTelemetryStartupExtensions.AddPlatformObservability(IHostApplicationBuilder, string, System.Action{OpenTelemetry.Trace.TracerProviderBuilder}?, System.Action{OpenTelemetry.Metrics.MeterProviderBuilder}?)"/>
/// path. Adds service discovery, resilient HTTP client defaults, and the
/// Aspire-injected OTLP exporter, guarded so it never double-registers the
/// OTEL meter/tracer providers when called alongside the platform helpers.
/// </summary>
public static class AspireServiceDefaultsExtensions
{
    private const string MarkerKey = "ECommerce.Shared.AspireServiceDefaults.Registered";

    /// <summary>
    /// Registers Aspire service defaults (OTEL OTLP wiring, service discovery,
    /// resilient HTTP defaults) before the platform <c>AddPlatform*</c> helpers.
    /// Idempotent: calling it more than once on the same builder is a no-op.
    /// </summary>
    public static TBuilder AddAspireServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        if (builder.Properties.ContainsKey(MarkerKey))
        {
            return builder;
        }

        builder.Properties[MarkerKey] = true;

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(_ => { })
            .WithTracing(_ => { });

        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }
}
