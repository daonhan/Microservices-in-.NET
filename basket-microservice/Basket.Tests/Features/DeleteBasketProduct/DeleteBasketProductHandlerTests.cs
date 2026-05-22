using System.Diagnostics.Metrics;
using Basket.Service.Domain;
using Basket.Service.Domain.Abstractions;
using Basket.Service.Features.DeleteBasketProduct;
using ECommerce.Shared.Observability.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;

namespace Basket.Tests.Features.DeleteBasketProduct;

public class DeleteBasketProductHandlerTests : IDisposable
{
    private readonly IBasketStore _basketStore = Substitute.For<IBasketStore>();
    private readonly MetricFactory _metricFactory = new("Basket.Tests.DeleteBasketProduct");

    public void Dispose()
    {
        _metricFactory.Dispose();
        GC.SuppressFinalize(this);
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
        var result = await new DeleteBasketProductHandler(_basketStore, _metricFactory)
            .HandleAsync(customerId, productId);

        // Assert
        Assert.NotNull(result);
        var noContentResult = (NoContent)result;
        Assert.NotNull(noContentResult);
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
                if (instrument.Meter.Name == "Basket.Tests.DeleteBasketProduct")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<int>((instrument, _, _, _) =>
            observed.Add(instrument.Name));
        listener.Start();

        // Act
        await new DeleteBasketProductHandler(_basketStore, _metricFactory)
            .HandleAsync(customerId, productId);

        // Assert
        Assert.Contains("basket-products-removed", observed);
        Assert.Contains("basket-updates", observed);
    }
}
