namespace ECommerce.Shared.Observability;

public class OpenTelemetryOptions
{
    public const string OpenTelemetrySectionName = "OpenTelemetry";
    public const string OtlpExporter = "Otlp";
    public const string AzureMonitorExporter = "AzureMonitor";

    public string OtlpExporterEndpoint { get; set; } = string.Empty;
    public string LogsOtlpExporterEndpoint { get; set; } = string.Empty;
    public double SamplingRatio { get; set; } = 1.0;
    public string Environment { get; set; } = "Development";
    public string ServiceVersion { get; set; } = "1.0.0";
    public bool EnableConsoleExporters { get; set; }

    /// <summary>
    /// Selects the OpenTelemetry exporter. <see cref="OtlpExporter"/> (default) sends to a local
    /// OTLP endpoint (Jaeger/OTel Collector). <see cref="AzureMonitorExporter"/> sends traces,
    /// metrics, and logs to Azure Application Insights.
    /// </summary>
    public string Exporter { get; set; } = OtlpExporter;

    /// <summary>
    /// Azure Application Insights connection string. When unset, falls back to the
    /// APPLICATIONINSIGHTS_CONNECTION_STRING environment variable.
    /// </summary>
    public string AzureMonitorConnectionString { get; set; } = string.Empty;

    public bool UseAzureMonitor =>
        string.Equals(Exporter, AzureMonitorExporter, StringComparison.OrdinalIgnoreCase);

    public string ResolveAzureMonitorConnectionString() =>
        !string.IsNullOrWhiteSpace(AzureMonitorConnectionString)
            ? AzureMonitorConnectionString
            : System.Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING") ?? string.Empty;
}
