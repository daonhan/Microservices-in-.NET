using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Order.Service.Infrastructure.Data;
using Order.Service.IntegrationEvents.Events;
using Order.Service.Models;

namespace Order.Service.IntegrationEvents.EventHandlers;

internal class PaymentFailedEventHandler : IEventHandler<PaymentFailedEvent>
{
    private readonly IOrderStore _orderStore;

    public PaymentFailedEventHandler(IOrderStore orderStore)
    {
        _orderStore = orderStore;
    }

    public async Task Handle(PaymentFailedEvent @event)
    {
        await _orderStore.ExecuteAsync(async () =>
        {
            var order = await _orderStore.GetOrderById(@event.OrderId);

            if (order is null || order.Status == OrderStatus.Cancelled)
            {
                return;
            }

            order.TryCancel();
        });
    }
}
