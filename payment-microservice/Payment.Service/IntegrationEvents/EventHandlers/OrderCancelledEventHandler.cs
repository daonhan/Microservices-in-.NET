using System.Transactions;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Payment.Service.Infrastructure.Data;
using Payment.Service.IntegrationEvents.Events;
using Payment.Service.Models;
using Payment.Service.Observability;

namespace Payment.Service.IntegrationEvents.EventHandlers;

internal class OrderCancelledEventHandler : IEventHandler<OrderCancelledEvent>
{
    private readonly IPaymentStore _store;
    private readonly IOutboxStore _outboxStore;
    private readonly PaymentMetrics _metrics;

    public OrderCancelledEventHandler(
        IPaymentStore store,
        IOutboxStore outboxStore,
        PaymentMetrics metrics)
    {
        _store = store;
        _outboxStore = outboxStore;
        _metrics = metrics;
    }

    public async Task Handle(OrderCancelledEvent @event)
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

            if (payment.Status != PaymentStatus.Pending && payment.Status != PaymentStatus.Authorized)
            {
                scope.Complete();
                return;
            }

            payment.Void(DateTime.UtcNow);

            await _store.SaveChangesAsync();

            await _outboxStore.AddOutboxEvent(new PaymentFailedEvent(
                payment.PaymentId,
                payment.OrderId,
                payment.CustomerId,
                "Order cancelled"));

            _metrics.RecordStatusChange(PaymentStatus.Failed);

            scope.Complete();
        });
    }
}
