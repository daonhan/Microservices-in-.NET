using Basket.Service.Contracts.Integration;
using Basket.Service.Domain.Abstractions;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;

namespace Basket.Service.IntegrationEvents.EventHandlers;

internal class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IBasketStore _basketStore;

    public OrderCreatedEventHandler(IBasketStore basketStore)
    {
        _basketStore = basketStore;
    }

    public async Task Handle(OrderCreatedEvent @event)
    {
        await _basketStore.DeleteCustomerBasket(@event.CustomerId);
    }
}
