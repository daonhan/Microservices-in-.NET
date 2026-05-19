using System.Diagnostics;
using System.Linq;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace ECommerce.Shared.Tests;

public sealed class AspireServiceDefaultsTests
{
    private static HostApplicationBuilder NewBuilder()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenTelemetry:OtlpExporterEndpoint"] = "http://localhost:4317",
            ["OpenTelemetry:SamplingRatio"] = "1.0",
        });
        return builder;
    }

    [Fact]
    public void Given_AspireDefaults_then_PlatformObservability_When_built_Then_both_register()
    {
        var builder = NewBuilder();

        builder.AddAspireServiceDefaults();
        builder.AddPlatformObservability("test");

        using var sp = builder.Services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<TracerProvider>());
        Assert.NotNull(sp.GetService<MeterProvider>());
        Assert.Contains(
            builder.Services,
            d => d.ServiceType.FullName?.Contains("ServiceDiscovery", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Given_AspireDefaults_called_twice_When_built_Then_single_tracer_and_meter_provider()
    {
        var builder = NewBuilder();

        builder.AddAspireServiceDefaults();
        builder.AddAspireServiceDefaults();
        builder.AddPlatformObservability("test");

        Assert.Equal(1, builder.Services.Count(d => d.ServiceType == typeof(TracerProvider)));
        Assert.Equal(1, builder.Services.Count(d => d.ServiceType == typeof(MeterProvider)));
    }

    [Fact]
    public void Given_AspireDefaults_When_PlatformObservability_records_activity_Then_pipeline_preserved()
    {
        var builder = NewBuilder();
        var processor = new RecordingProcessor();

        builder.AddAspireServiceDefaults();
        builder.AddPlatformObservability(
            "test",
            customTracing: tracing => tracing.AddProcessor(processor));

        using var sp = builder.Services.BuildServiceProvider();
        var tracerProvider = sp.GetRequiredService<TracerProvider>();

        using (var activity = OutboxTelemetry.ActivitySource.StartActivity("outbox.uow"))
        {
            activity?.Stop();
        }

        tracerProvider.ForceFlush(5000);

        Assert.Contains(processor.Activities, a => a.Source.Name == OutboxTelemetry.ActivitySourceName);
    }

    private sealed class RecordingProcessor : BaseProcessor<Activity>
    {
        private readonly List<Activity> _activities = new();

        public IReadOnlyList<Activity> Activities
        {
            get
            {
                lock (_activities)
                {
                    return _activities.ToArray();
                }
            }
        }

        public override void OnEnd(Activity data)
        {
            lock (_activities)
            {
                _activities.Add(data);
            }
        }
    }
}
