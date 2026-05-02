using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Order.Service.Infrastructure.Data;
using Order.Service.IntegrationEvents.Events;
using Order.Service.Models;

namespace Order.Service.IntegrationEvents.EventHandlers;

internal class PaymentAuthorizedEventHandler : IEventHandler<PaymentAuthorizedEvent>
{
    private readonly IOrderStore _orderStore;

    public PaymentAuthorizedEventHandler(IOrderStore orderStore)
    {
        _orderStore = orderStore;
    }

    public async Task Handle(PaymentAuthorizedEvent @event)
    {
        await _orderStore.ExecuteAsync(async () =>
        {
            var order = await _orderStore.GetOrderById(@event.OrderId);

            if (order is null || order.Status != OrderStatus.PendingStock)
            {
                return;
            }

            order.TryConfirm();
        });
    }
}
