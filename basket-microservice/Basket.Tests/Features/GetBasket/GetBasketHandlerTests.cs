using Basket.Service.Domain;
using Basket.Service.Domain.Abstractions;
using Basket.Service.Features.GetBasket;
using NSubstitute;

namespace Basket.Tests.Features.GetBasket;

public class GetBasketHandlerTests
{
    private readonly IBasketStore _basketStore = Substitute.For<IBasketStore>();

    [Fact]
    public async Task GivenExistingBasket_WhenCallingGetBasket_ThenReturnsBasket()
    {
        // Arrange
        const string customerId = "1";
        var customerBasket = new CustomerBasket { CustomerId = customerId };

        _basketStore.GetBasketByCustomerId(customerId)
            .Returns(customerBasket);

        // Act
        var result = await new GetBasketHandler(_basketStore).HandleAsync(customerId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(customerId, result.CustomerId);
    }
}
