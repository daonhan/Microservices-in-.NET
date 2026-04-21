using System.Diagnostics.Metrics;
using ECommerce.Shared.Observability.Metrics;

namespace Order.Tests.Domain;

public class MetricFactoryTests : IDisposable
{
    private readonly MetricFactory _metricFactory = new("MetricFactoryTests");

    public void Dispose()
    {
        _metricFactory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Counter_UsesProvidedNameAndUnit()
    {
        // Arrange
        var observed = new List<(string name, string? unit)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "MetricFactoryTests")
                {
                    observed.Add((instrument.Name, instrument.Unit));
                    l.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.Start();

        // Act
        _metricFactory.Counter("my-counter", "things");

        // Assert
        Assert.Contains(("my-counter", "things"), observed);
    }

    [Fact]
    public void Histogram_UsesProvidedNameAndUnit()
    {
        // Arrange
        var observed = new List<(string name, string? unit)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "MetricFactoryTests")
                {
                    observed.Add((instrument.Name, instrument.Unit));
                    l.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.Start();

        // Act
        _metricFactory.Histogram("my-histogram", "ms");

        // Assert
        Assert.Contains(("my-histogram", "ms"), observed);
    }

    [Fact]
    public void Counter_WhenCalledTwiceWithSameName_ReturnsSameInstance()
    {
        // Act
        var first = _metricFactory.Counter("cached-counter");
        var second = _metricFactory.Counter("cached-counter");

        // Assert
        Assert.Same(first, second);
    }

    [Fact]
    public void Histogram_WhenCalledTwiceWithSameName_ReturnsSameInstance()
    {
        // Act
        var first = _metricFactory.Histogram("cached-histogram");
        var second = _metricFactory.Histogram("cached-histogram");

        // Assert
        Assert.Same(first, second);
    }
}
