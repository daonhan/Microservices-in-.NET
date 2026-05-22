using System.Diagnostics.Metrics;
using System.Text;
using Basket.Service.Domain.Abstractions;
using Basket.Service.Features.CreateBasket;
using ECommerce.Shared.Observability.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Caching.Distributed;
using NSubstitute;

namespace Basket.Tests.Features.CreateBasket;

public class CreateBasketHandlerTests : IDisposable
{
    private readonly IBasketStore _basketStore = Substitute.For<IBasketStore>();
    private readonly IDistributedCache _cache = Substitute.For<IDistributedCache>();
    private readonly MetricFactory _metricFactory = new("Basket.Tests.CreateBasket");

    public void Dispose()
    {
        _metricFactory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GivenNewBasketRequest_WhenCallingCreateBasket_ThenReturnsCreatedResult()
    {
        // Arrange
        const string customerId = "1";
        const string productId = "1";
        var createBasketRequest = new CreateBasketRequest(productId, "Test Name");

        _cache.GetAsync(productId)
            .Returns(Encoding.UTF8.GetBytes("1.00"));

        // Act
        var result = await new CreateBasketHandler(_basketStore, _cache, _metricFactory)
            .HandleAsync(customerId, createBasketRequest);

        // Assert
        Assert.NotNull(result);
        var createdResult = (Created)result;
        Assert.NotNull(createdResult);
    }

    [Fact]
    public async Task WhenCreatingBasket_ThenEmitsBasketUpdatesAndProductsAddedAndSizeMetrics()
    {
        // Arrange
        const string customerId = "1";
        const string productId = "1";
        var createBasketRequest = new CreateBasketRequest(productId, "Test Name");

        _cache.GetAsync(productId)
            .Returns(Encoding.UTF8.GetBytes("1.00"));

        var observed = new List<(string instrument, int value)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "Basket.Tests.CreateBasket")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<int>((instrument, measurement, _, _) =>
            observed.Add((instrument.Name, measurement)));
        listener.Start();

        // Act
        await new CreateBasketHandler(_basketStore, _cache, _metricFactory)
            .HandleAsync(customerId, createBasketRequest);

        // Assert
        Assert.Contains(observed, o => o.instrument == "basket-updates" && o.value == 1);
        Assert.Contains(observed, o => o.instrument == "basket-products-added" && o.value == 1);
        Assert.Contains(observed, o => o.instrument == "basket-size");
    }
}
