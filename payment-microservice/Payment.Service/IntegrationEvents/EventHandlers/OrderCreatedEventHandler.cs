using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Payment.Service.Infrastructure.Data;
using Payment.Service.IntegrationEvents.Events;

namespace Payment.Service.IntegrationEvents.EventHandlers;

internal class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IPaymentStore _paymentStore;

    public OrderCreatedEventHandler(IPaymentStore paymentStore)
    {
        _paymentStore = paymentStore;
    }

    public async Task Handle(OrderCreatedEvent @event)
    {
        await _paymentStore.RecordOrderCustomer(@event.OrderId, @event.CustomerId);
    }
}
