using Basket.Service.Contracts.Integration;
using Basket.Service.Domain.Abstractions;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;

namespace Basket.Service.Features.OrderCreated;

internal sealed class OrderCreatedHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IBasketStore _basketStore;

    public OrderCreatedHandler(IBasketStore basketStore)
    {
        _basketStore = basketStore;
    }

    public async Task Handle(OrderCreatedEvent @event)
    {
        await _basketStore.DeleteCustomerBasket(@event.CustomerId);
    }
}
