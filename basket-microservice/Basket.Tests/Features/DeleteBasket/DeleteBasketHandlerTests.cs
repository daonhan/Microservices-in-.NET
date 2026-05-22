using Basket.Service.Domain.Abstractions;
using Basket.Service.Features.DeleteBasket;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;

namespace Basket.Tests.Features.DeleteBasket;

public class DeleteBasketHandlerTests
{
    private readonly IBasketStore _basketStore = Substitute.For<IBasketStore>();

    [Fact]
    public async Task GivenExistingBasket_WhenCallingDeleteBasket_ThenReturnsNoContentResult()
    {
        // Arrange
        const string customerId = "1";

        // Act
        var result = await new DeleteBasketHandler(_basketStore).HandleAsync(customerId);

        // Assert
        Assert.NotNull(result);
        var noContentResult = (NoContent)result;
        Assert.NotNull(noContentResult);
    }
}
