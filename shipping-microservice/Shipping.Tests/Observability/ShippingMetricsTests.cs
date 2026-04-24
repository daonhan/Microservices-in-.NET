using System.Diagnostics.Metrics;
using ECommerce.Shared.Observability.Metrics;
using Shipping.Service.Models;
using Shipping.Service.Observability;

namespace Shipping.Tests.Observability;

public class ShippingMetricsTests : IDisposable
{
    private readonly MetricFactory _factory = new("Shipping.Tests.Metrics");
    private readonly ShippingMetrics _metrics;
    private readonly MeterListener _listener;
    private readonly List<RecordedMeasurement> _measurements = [];

    public ShippingMetricsTests()
    {
        _metrics = new ShippingMetrics(_factory);
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "Shipping.Tests.Metrics")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        _listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
        {
            var tagList = new List<KeyValuePair<string, object?>>();
            foreach (var tag in tags)
            {
                tagList.Add(tag);
            }

            _measurements.Add(new RecordedMeasurement(instrument.Name, value, tagList));
        });
        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void RecordStatusChange_EmitsShipmentsTotalWithStatusTag()
    {
        _metrics.RecordStatusChange(ShipmentStatus.Shipped);

        var measurement = Assert.Single(_measurements, m => m.Name == "shipments_total");
        Assert.Equal(1, measurement.Value);
        Assert.Contains(measurement.Tags,
            t => t.Key == "status" && (string?)t.Value == "Shipped");
    }

    [Fact]
    public void RecordTimeToDispatch_EmitsSeconds()
    {
        var created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var dispatched = created.AddSeconds(42);

        _metrics.RecordTimeToDispatch(created, dispatched);

        var measurement = Assert.Single(_measurements, m => m.Name == "time_to_dispatch_seconds");
        Assert.Equal(42, measurement.Value);
    }

    [Fact]
    public void RecordTimeToDelivery_EmitsSeconds()
    {
        var created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var delivered = created.AddMinutes(2);

        _metrics.RecordTimeToDelivery(created, delivered);

        var measurement = Assert.Single(_measurements, m => m.Name == "time_to_delivery_seconds");
        Assert.Equal(120, measurement.Value);
    }

    [Fact]
    public void RecordTimeToDelivery_WithNegativeElapsed_ClampsToZero()
    {
        var created = new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc);
        var delivered = created.AddSeconds(-30);

        _metrics.RecordTimeToDelivery(created, delivered);

        var measurement = Assert.Single(_measurements, m => m.Name == "time_to_delivery_seconds");
        Assert.Equal(0, measurement.Value);
    }

    [Fact]
    public void RecordRateShoppingSpread_EmitsSpreadInCents()
    {
        _metrics.RecordRateShoppingSpread(minPrice: 9.50m, maxPrice: 12.00m);

        var measurement = Assert.Single(_measurements, m => m.Name == "rate_shopping_quote_spread");
        Assert.Equal(250, measurement.Value);
    }

    private sealed record RecordedMeasurement(
        string Name,
        int Value,
        IReadOnlyList<KeyValuePair<string, object?>> Tags);
}
