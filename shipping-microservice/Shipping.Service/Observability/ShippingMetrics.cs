using System.Diagnostics.Metrics;
using ECommerce.Shared.Observability.Metrics;
using Shipping.Service.Models;

namespace Shipping.Service.Observability;

/// <summary>
/// Custom Shipping-service metrics emitted through the shared
/// <see cref="MetricFactory"/>. All histograms emit integer samples:
/// times are recorded in seconds, quote spreads in cents so the
/// integer-based MetricFactory API is sufficient.
/// </summary>
internal sealed class ShippingMetrics
{
    private readonly Counter<int> _shipmentsTotal;
    private readonly Histogram<int> _timeToDispatchSeconds;
    private readonly Histogram<int> _timeToDeliverySeconds;
    private readonly Histogram<int> _rateShoppingQuoteSpread;

    public ShippingMetrics(MetricFactory factory)
    {
        _shipmentsTotal = factory.Counter("shipments_total", "shipments");
        _timeToDispatchSeconds = factory.Histogram("time_to_dispatch_seconds", "s");
        _timeToDeliverySeconds = factory.Histogram("time_to_delivery_seconds", "s");
        _rateShoppingQuoteSpread = factory.Histogram("rate_shopping_quote_spread", "cents");
    }

    public void RecordStatusChange(ShipmentStatus toStatus)
    {
        _shipmentsTotal.Add(1, new KeyValuePair<string, object?>("status", toStatus.ToString()));
    }

    public void RecordTimeToDispatch(DateTime createdAt, DateTime dispatchedAt)
    {
        _timeToDispatchSeconds.Record(ToNonNegativeSeconds(dispatchedAt - createdAt));
    }

    public void RecordTimeToDelivery(DateTime createdAt, DateTime deliveredAt)
    {
        _timeToDeliverySeconds.Record(ToNonNegativeSeconds(deliveredAt - createdAt));
    }

    public void RecordRateShoppingSpread(decimal minPrice, decimal maxPrice)
    {
        var spread = Math.Max(0m, maxPrice - minPrice);
        var cents = (int)Math.Round(spread * 100m);
        _rateShoppingQuoteSpread.Record(cents);
    }

    private static int ToNonNegativeSeconds(TimeSpan elapsed)
        => (int)Math.Max(0, Math.Round(elapsed.TotalSeconds));
}
