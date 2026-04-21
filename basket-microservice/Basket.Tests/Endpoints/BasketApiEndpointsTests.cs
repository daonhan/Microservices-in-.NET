using System.Diagnostics.Metrics;
using System.Text;
using Basket.Service.ApiModels;
using Basket.Service.Endpoints;
using Basket.Service.Infrastructure.Data;
using Basket.Service.Models;
using ECommerce.Shared.Observability.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Caching.Distributed;
using NSubstitute;

namespace Basket.Tests.Endpoints;

public class BasketApiEndpointsTests : IDisposable
{
    private readonly IBasketStore _basketStore = Substitute.For<IBasketStore>();
    private readonly IDistributedCache _cache = Substitute.For<IDistributedCache>();
    private readonly MetricFactory _metricFactory = new("Basket.Tests");

    public void Dispose()
    {
        _metricFactory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GivenExistingBasket_WhenCallingGetBasket_ThenReturnsBasket()
    {
        // Arrange
        const string customerId = "1";
        var customerBasket = new CustomerBasket { CustomerId = customerId };

        _basketStore.GetBasketByCustomerId(customerId)
            .Returns(customerBasket);

        // Act
        var result = await BasketApiEndpoints.GetBasket(_basketStore, customerId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(customerId, result.CustomerId);
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
        var result = await BasketApiEndpoints.CreateBasket(_basketStore, _cache, _metricFactory,
            customerId, createBasketRequest);

        // Assert
        Assert.NotNull(result);
        var createdResult = (Created)result;
        Assert.NotNull(createdResult);
    }

    [Fact]
    public async Task GivenExistingBasket_WhenCallingAddBasketProduct_ThenReturnsNoContentResult()
    {
        // Arrange
        const string customerId = "1";
        const string productId = "1";
        var addProductRequest = new AddBasketProductRequest(productId, "Test Name", 2);
        var customerBasket = new CustomerBasket { CustomerId = customerId };

        _basketStore.GetBasketByCustomerId(customerId)
            .Returns(customerBasket);

        _cache.GetAsync(productId)
            .Returns(Encoding.UTF8.GetBytes("9.99"));

        // Act
        var result = await BasketApiEndpoints.AddBasketProduct(_basketStore, _cache, _metricFactory,
            customerId, addProductRequest);

        // Assert
        Assert.NotNull(result);
        var noContentResult = (NoContent)result;
        Assert.NotNull(noContentResult);
    }

    [Fact]
    public async Task GivenExistingBasketWithProduct_WhenCallingDeleteBasketProduct_ThenReturnsNoContentResult()
    {
        // Arrange
        const string customerId = "1";
        const string productId = "1";
        var customerBasket = new CustomerBasket { CustomerId = customerId };
        customerBasket.AddBasketProduct(new BasketProduct(productId, "Test Name", 9.99M));

        _basketStore.GetBasketByCustomerId(customerId)
            .Returns(customerBasket);

        // Act
        var result = await BasketApiEndpoints.DeleteBasketProduct(_basketStore, _metricFactory, customerId, productId);

        // Assert
        Assert.NotNull(result);
        var noContentResult = (NoContent)result;
        Assert.NotNull(noContentResult);
    }

    [Fact]
    public async Task GivenExistingBasket_WhenCallingDeleteBasket_ThenReturnsNoContentResult()
    {
        // Arrange
        const string customerId = "1";

        // Act
        var result = await BasketApiEndpoints.DeleteBasket(_basketStore, customerId);

        // Assert
        Assert.NotNull(result);
        var noContentResult = (NoContent)result;
        Assert.NotNull(noContentResult);
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
                if (instrument.Meter.Name == "Basket.Tests")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<int>((instrument, measurement, _, _) =>
            observed.Add((instrument.Name, measurement)));
        listener.Start();

        // Act
        await BasketApiEndpoints.CreateBasket(_basketStore, _cache, _metricFactory,
            customerId, createBasketRequest);

        // Assert
        Assert.Contains(observed, o => o.instrument == "basket-updates" && o.value == 1);
        Assert.Contains(observed, o => o.instrument == "basket-products-added" && o.value == 1);
        Assert.Contains(observed, o => o.instrument == "basket-size");
    }

    [Fact]
    public async Task WhenDeletingBasketProduct_ThenEmitsBasketProductsRemovedMetric()
    {
        // Arrange
        const string customerId = "1";
        const string productId = "1";
        var customerBasket = new CustomerBasket { CustomerId = customerId };
        customerBasket.AddBasketProduct(new BasketProduct(productId, "Test Name", 9.99M));

        _basketStore.GetBasketByCustomerId(customerId)
            .Returns(customerBasket);

        var observed = new List<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "Basket.Tests")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<int>((instrument, _, _, _) =>
            observed.Add(instrument.Name));
        listener.Start();

        // Act
        await BasketApiEndpoints.DeleteBasketProduct(_basketStore, _metricFactory, customerId, productId);

        // Assert
        Assert.Contains("basket-products-removed", observed);
        Assert.Contains("basket-updates", observed);
    }
}
