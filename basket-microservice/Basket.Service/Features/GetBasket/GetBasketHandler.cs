using Basket.Service.Domain;
using Basket.Service.Domain.Abstractions;

namespace Basket.Service.Features.GetBasket;

internal sealed class GetBasketHandler
{
    private readonly IBasketStore _basketStore;

    public GetBasketHandler(IBasketStore basketStore)
    {
        _basketStore = basketStore;
    }

    public Task<CustomerBasket> HandleAsync(string customerId)
        => _basketStore.GetBasketByCustomerId(customerId);
}
