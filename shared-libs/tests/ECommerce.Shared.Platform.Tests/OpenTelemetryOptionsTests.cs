using ECommerce.Shared.Observability;
using Microsoft.Extensions.Configuration;

namespace ECommerce.Shared.Tests;

public sealed class OpenTelemetryOptionsTests
{
    [Fact]
    public void Given_no_exporter_When_binding_options_Then_Otlp_is_default()
    {
        var configuration = BuildConfig(new Dictionary<string, string?>
        {
            ["OpenTelemetry:OtlpExporterEndpoint"] = "http://localhost:4317",
        });

        var opts = new OpenTelemetryOptions();
        configuration.GetSection(OpenTelemetryOptions.OpenTelemetrySectionName).Bind(opts);

        Assert.Equal(OpenTelemetryOptions.OtlpExporter, opts.Exporter);
        Assert.False(opts.UseAzureMonitor);
    }

    [Fact]
    public void Given_AzureMonitor_exporter_When_binding_options_Then_UseAzureMonitor_is_true()
    {
        var configuration = BuildConfig(new Dictionary<string, string?>
        {
            ["OpenTelemetry:Exporter"] = "AzureMonitor",
            ["OpenTelemetry:AzureMonitorConnectionString"] = "InstrumentationKey=00000000-0000-0000-0000-000000000000",
        });

        var opts = new OpenTelemetryOptions();
        configuration.GetSection(OpenTelemetryOptions.OpenTelemetrySectionName).Bind(opts);

        Assert.True(opts.UseAzureMonitor);
        Assert.Equal(
            "InstrumentationKey=00000000-0000-0000-0000-000000000000",
            opts.ResolveAzureMonitorConnectionString());
    }

    [Fact]
    public void Given_AzureMonitor_without_connection_string_When_resolving_Then_falls_back_to_env_var()
    {
        var previous = System.Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
        try
        {
            System.Environment.SetEnvironmentVariable(
                "APPLICATIONINSIGHTS_CONNECTION_STRING",
                "InstrumentationKey=11111111-1111-1111-1111-111111111111");

            var opts = new OpenTelemetryOptions { Exporter = OpenTelemetryOptions.AzureMonitorExporter };

            Assert.True(opts.UseAzureMonitor);
            Assert.Equal(
                "InstrumentationKey=11111111-1111-1111-1111-111111111111",
                opts.ResolveAzureMonitorConnectionString());
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING", previous);
        }
    }

    private static ConfigurationManager BuildConfig(Dictionary<string, string?> values)
    {
        var manager = new ConfigurationManager();
        manager.AddInMemoryCollection(values);
        return manager;
    }
}
