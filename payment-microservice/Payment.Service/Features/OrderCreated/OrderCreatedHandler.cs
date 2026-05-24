using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Payment.Service.Contracts.Integration;
using Payment.Service.Domain.Abstractions;

namespace Payment.Service.Features.OrderCreated;

internal sealed class OrderCreatedHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IPaymentStore _paymentStore;

    public OrderCreatedHandler(IPaymentStore paymentStore)
    {
        _paymentStore = paymentStore;
    }

    public async Task Handle(OrderCreatedEvent @event)
    {
        await _paymentStore.RecordOrderCustomer(@event.OrderId, @event.CustomerId);
    }
}
