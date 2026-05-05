using System.Diagnostics;
using ECommerce.Shared.Infrastructure.DeadLetter;
using ECommerce.Shared.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace ECommerce.Shared.Tests;

public sealed class DeadLetterPlatformObservabilityTests
{
    [Fact]
    public void Given_AddPlatformObservability_When_DeadLetter_activity_is_started_Then_pipeline_records_it()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenTelemetry:OtlpExporterEndpoint"] = "http://localhost:4317",
            ["OpenTelemetry:SamplingRatio"] = "1.0",
        });

        var processor = new RecordingProcessor();

        var services = new ServiceCollection();
        services.AddPlatformObservability(
            "test",
            configuration,
            customTracing: tracing => tracing.AddProcessor(processor));

        using var sp = services.BuildServiceProvider();
        var tracerProvider = sp.GetRequiredService<TracerProvider>();

        using (var activity = DeadLetterMetrics.ActivitySource.StartActivity("test-replay"))
        {
            activity?.Stop();
        }

        tracerProvider.ForceFlush(5000);

        Assert.Contains(processor.Activities, a => a.Source.Name == DeadLetterMetrics.ActivitySourceName);
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
