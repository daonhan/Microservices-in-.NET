using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Payment.Service.Infrastructure.Data;
using Payment.Service.Infrastructure.Gateways;
using Payment.Service.IntegrationEvents.Events;
using Payment.Service.Models;
using Payment.Service.Observability;

namespace Payment.Service.IntegrationEvents.EventHandlers;

internal class ShipmentDispatchedEventHandler : IEventHandler<ShipmentDispatchedEvent>
{
    private readonly IPaymentStore _store;
    private readonly IPaymentGateway _gateway;
    private readonly PaymentMetrics _metrics;

    public ShipmentDispatchedEventHandler(
        IPaymentStore store,
        IPaymentGateway gateway,
        PaymentMetrics metrics)
    {
        _store = store;
        _gateway = gateway;
        _metrics = metrics;
    }

    public async Task Handle(ShipmentDispatchedEvent @event)
    {
        var payment = await _store.GetByOrder(@event.OrderId);
        if (payment is null)
        {
            return;
        }

        if (payment.Status != PaymentStatus.Authorized)
        {
            // Already captured (redelivery), refunded, or failed — nothing to do.
            return;
        }

        await _gateway.CaptureAsync(payment.ProviderReference!);

        var captured = false;
        await _store.ExecuteAsync(() =>
        {
            captured = payment.Capture(DateTime.UtcNow);
            return Task.CompletedTask;
        });

        if (captured)
        {
            _metrics.RecordStatusChange(PaymentStatus.Captured);
        }
    }
}
