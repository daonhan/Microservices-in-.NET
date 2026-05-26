using System.Diagnostics;
using System.Diagnostics.Metrics;
using ECommerce.Shared.Kernel.TelemetryConventions;

namespace ECommerce.Shared.Infrastructure.DeadLetter;

internal static class DeadLetterMetrics
{
    public const string MeterName = DeadLetterTelemetryNames.MeterName;
    public const string ActivitySourceName = DeadLetterTelemetryNames.ActivitySourceName;

    public static readonly Meter Meter = new(MeterName);

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static readonly Counter<long> Replays = Meter.CreateCounter<long>(
        "dlq_replays_total",
        description: "Number of replay attempts from the dead-letter store, tagged with provider and outcome (success|failure).");

    public static readonly Counter<long> Discards = Meter.CreateCounter<long>(
        "dlq_discards_total",
        description: "Number of discard attempts from the dead-letter store, tagged with provider and outcome (success|failure).");
}
