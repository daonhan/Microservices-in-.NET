using System.Transactions;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Order.Service.Infrastructure.Data;
using Order.Service.IntegrationEvents.Events;
using Order.Service.Models;

namespace Order.Service.IntegrationEvents.EventHandlers;

internal class StockReservationFailedEventHandler : IEventHandler<StockReservationFailedEvent>
{
    private readonly IOrderStore _orderStore;
    private readonly IOutboxStore _outboxStore;

    public StockReservationFailedEventHandler(IOrderStore orderStore, IOutboxStore outboxStore)
    {
        _orderStore = orderStore;
        _outboxStore = outboxStore;
    }

    public async Task Handle(StockReservationFailedEvent @event)
    {
        await _outboxStore.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            var order = await _orderStore.GetOrderById(@event.OrderId);

            if (order is null || order.Status == OrderStatus.Cancelled)
            {
                return;
            }

            if (!order.TryCancel())
            {
                return;
            }

            await _orderStore.Commit();

            await _outboxStore.AddOutboxEvent(new OrderCancelledEvent(order.OrderId, order.CustomerId));

            scope.Complete();
        });
    }
}
