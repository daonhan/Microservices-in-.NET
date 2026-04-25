using System.Diagnostics.Metrics;
using ECommerce.Shared.Observability.Metrics;
using Payment.Service.Models;

namespace Payment.Service.Observability;

internal sealed class PaymentMetrics
{
    private readonly Counter<int> _paymentsTotal;
    private readonly Histogram<int> _paymentAuthorizeLatencyMs;

    public PaymentMetrics(MetricFactory factory)
    {
        _paymentsTotal = factory.Counter("payments_total", "payments");
        _paymentAuthorizeLatencyMs = factory.Histogram("payment_authorize_latency_ms", "ms");
    }

    public void RecordStatusChange(PaymentStatus toStatus)
    {
        _paymentsTotal.Add(1, new KeyValuePair<string, object?>("status", toStatus.ToString()));
    }

    public void RecordAuthorizeLatency(TimeSpan elapsed)
    {
        var ms = (int)Math.Max(0, Math.Round(elapsed.TotalMilliseconds));
        _paymentAuthorizeLatencyMs.Record(ms);
    }
}
