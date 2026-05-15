using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Payment.Service.Infrastructure.Data;
using Payment.Service.IntegrationEvents.Events;
using Payment.Service.Models;
using Payment.Service.Observability;

namespace Payment.Service.IntegrationEvents.EventHandlers;

internal class OrderCancelledEventHandler : IEventHandler<OrderCancelledEvent>
{
    private readonly IPaymentStore _store;
    private readonly PaymentMetrics _metrics;

    public OrderCancelledEventHandler(
        IPaymentStore store,
        PaymentMetrics metrics)
    {
        _store = store;
        _metrics = metrics;
    }

    public async Task Handle(OrderCancelledEvent @event)
    {
        var payment = await _store.GetByOrder(@event.OrderId);
        if (payment is null)
        {
            return;
        }

        if (payment.Status != PaymentStatus.Pending && payment.Status != PaymentStatus.Authorized)
        {
            return;
        }

        await _store.ExecuteAsync(() =>
        {
            payment.Void("Order cancelled", DateTime.UtcNow);
            return Task.CompletedTask;
        });

        _metrics.RecordStatusChange(PaymentStatus.Failed);
    }
}
