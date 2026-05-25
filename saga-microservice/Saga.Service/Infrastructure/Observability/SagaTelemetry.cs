using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Saga.Service.Infrastructure.Observability;

internal static class SagaTelemetry
{
    public const string MeterName = "saga-orchestrator";
    public const string ActivitySourceName = "saga-orchestrator";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Started = Meter.CreateCounter<long>(
        "saga_started_total",
        description: "Saga instances opened, tagged by saga type.");

    public static readonly Counter<long> Completed = Meter.CreateCounter<long>(
        "saga_completed_total",
        description: "Saga instances that reached a successful terminal state, tagged by type.");

    public static readonly Counter<long> Failed = Meter.CreateCounter<long>(
        "saga_failed_total",
        description: "Saga instances that parked in Failed, tagged by type and reason.");

    public static readonly Histogram<double> StepDuration = Meter.CreateHistogram<double>(
        "saga_step_duration_seconds",
        unit: "s",
        description: "Wall-clock seconds a saga spent in a step before the next transition, tagged by type and step.");

    public static readonly Counter<long> Overdue = Meter.CreateCounter<long>(
        "saga_overdue_total",
        description: "Saga instances picked up after their current step timeout elapsed, tagged by type and step.");

    public static readonly Counter<long> Compensation = Meter.CreateCounter<long>(
        "saga_compensation_total",
        description: "Saga instances that entered compensation, tagged by type.");

    // Opens a transition span parented to the incoming event's trace context
    // (Activity.Current, propagated by the messaging instrumentation). The
    // caller disposes the activity once the transition is persisted.
    public static Activity? StartTransition(Guid sagaId, string sagaType, string fromStep, string toStep)
    {
        var activity = ActivitySource.StartActivity("saga.transition", ActivityKind.Internal);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("saga.id", sagaId);
        activity.SetTag("saga.type", sagaType);
        activity.SetTag("saga.from_step", fromStep);
        activity.SetTag("saga.to_step", toStep);
        return activity;
    }
}
