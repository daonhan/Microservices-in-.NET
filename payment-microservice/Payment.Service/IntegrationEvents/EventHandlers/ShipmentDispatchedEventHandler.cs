using System.Transactions;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Payment.Service.Infrastructure.Data;
using Payment.Service.Infrastructure.Gateways;
using Payment.Service.IntegrationEvents.Events;
using Payment.Service.Models;
using Payment.Service.Observability;

namespace Payment.Service.IntegrationEvents.EventHandlers;

internal class ShipmentDispatchedEventHandler : IEventHandler<ShipmentDispatchedEvent>
{
    private readonly IPaymentStore _store;
    private readonly IOutboxStore _outboxStore;
    private readonly IPaymentGateway _gateway;
    private readonly PaymentMetrics _metrics;

    public ShipmentDispatchedEventHandler(
        IPaymentStore store,
        IOutboxStore outboxStore,
        IPaymentGateway gateway,
        PaymentMetrics metrics)
    {
        _store = store;
        _outboxStore = outboxStore;
        _gateway = gateway;
        _metrics = metrics;
    }

    public async Task Handle(ShipmentDispatchedEvent @event)
    {
        await _outboxStore.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            var payment = await _store.GetByOrder(@event.OrderId);
            if (payment is null)
            {
                scope.Complete();
                return;
            }

            if (payment.Status != PaymentStatus.Authorized)
            {
                // Already captured (redelivery), refunded, or failed — nothing to do.
                scope.Complete();
                return;
            }

            await _gateway.CaptureAsync(payment.ProviderReference!);

            payment.Capture(DateTime.UtcNow);

            await _store.SaveChangesAsync();

            await _outboxStore.AddOutboxEvent(new PaymentCapturedEvent(
                payment.PaymentId,
                payment.OrderId,
                payment.Amount));

            _metrics.RecordStatusChange(PaymentStatus.Captured);

            scope.Complete();
        });
    }
}
