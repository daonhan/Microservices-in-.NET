using System.Text;
using Basket.Service.Domain;
using Basket.Service.Domain.Abstractions;
using Basket.Service.Features.AddBasketProduct;
using ECommerce.Shared.Observability.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Caching.Distributed;
using NSubstitute;

namespace Basket.Tests.Features.AddBasketProduct;

public class AddBasketProductHandlerTests : IDisposable
{
    private readonly IBasketStore _basketStore = Substitute.For<IBasketStore>();
    private readonly IDistributedCache _cache = Substitute.For<IDistributedCache>();
    private readonly MetricFactory _metricFactory = new("Basket.Tests.AddBasketProduct");

    public void Dispose()
    {
        _metricFactory.Dispose();
        GC.SuppressFinalize(this);
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
        var result = await new AddBasketProductHandler(_basketStore, _cache, _metricFactory)
            .HandleAsync(customerId, addProductRequest);

        // Assert
        Assert.NotNull(result);
        var noContentResult = (NoContent)result;
        Assert.NotNull(noContentResult);
    }
}
