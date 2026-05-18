using System.Diagnostics.Metrics;

namespace Saga.Service.Observability;

internal static class SagaTelemetry
{
    public const string MeterName = "saga-orchestrator";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Overdue = Meter.CreateCounter<long>(
        "saga_overdue_total",
        description: "Saga instances picked up after their current step timeout elapsed.");
}
