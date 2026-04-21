namespace ECommerce.Shared.Observability;

public class OpenTelemetryOptions
{
    public const string OpenTelemetrySectionName = "OpenTelemetry";
    public string OtlpExporterEndpoint { get; set; } = string.Empty;
    public string LogsOtlpExporterEndpoint { get; set; } = string.Empty;
    public double SamplingRatio { get; set; } = 1.0;
    public string Environment { get; set; } = "Development";
    public string ServiceVersion { get; set; } = "1.0.0";
    public bool EnableConsoleExporters { get; set; }
}
