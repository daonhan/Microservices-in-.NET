using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ECommerce.Shared.Infrastructure.Outbox;

internal static class OutboxTelemetry
{
    public const string ActivitySourceName = "ECommerce.Shared.Outbox";
    public const string MeterName = "ECommerce.Shared.Outbox";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Transactions = Meter.CreateCounter<long>(
        "outbox_uow_transactions_total",
        "transactions",
        "Number of Outbox unit-of-work transactions, tagged by operation and outcome.");
}
