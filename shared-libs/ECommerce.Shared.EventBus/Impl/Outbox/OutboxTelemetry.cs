using System.Diagnostics;
using System.Diagnostics.Metrics;
using ECommerce.Shared.Kernel.TelemetryConventions;

namespace ECommerce.Shared.Infrastructure.Outbox;

internal static class OutboxTelemetry
{
    public const string ActivitySourceName = OutboxTelemetryNames.ActivitySourceName;
    public const string MeterName = OutboxTelemetryNames.MeterName;

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Transactions = Meter.CreateCounter<long>(
        "outbox_uow_transactions_total",
        "transactions",
        "Number of Outbox unit-of-work transactions, tagged by operation and outcome.");
}
