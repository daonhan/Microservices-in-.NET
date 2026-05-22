using Basket.Service.Domain.Abstractions;

namespace Basket.Service.Features.DeleteBasket;

internal sealed class DeleteBasketHandler
{
    private readonly IBasketStore _basketStore;

    public DeleteBasketHandler(IBasketStore basketStore)
    {
        _basketStore = basketStore;
    }

    public async Task<IResult> HandleAsync(string customerId)
    {
        await _basketStore.DeleteCustomerBasket(customerId);

        return TypedResults.NoContent();
    }
}
