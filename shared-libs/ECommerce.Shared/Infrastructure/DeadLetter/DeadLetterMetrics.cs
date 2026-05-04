using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ECommerce.Shared.Infrastructure.DeadLetter;

internal static class DeadLetterMetrics
{
    public const string MeterName = "ECommerce.Shared.DeadLetter";
    public const string ActivitySourceName = "ECommerce.Shared.DeadLetter";

    public static readonly Meter Meter = new(MeterName);

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static readonly Counter<long> Replays = Meter.CreateCounter<long>(
        "dlq_replays_total",
        description: "Number of replay attempts from the dead-letter store, tagged with outcome (success|failure).");
}
