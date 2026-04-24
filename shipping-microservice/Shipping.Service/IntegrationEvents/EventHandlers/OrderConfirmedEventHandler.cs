using System.Transactions;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Shipping.Service.Infrastructure.Data;

namespace Shipping.Service.IntegrationEvents.EventHandlers;

internal class OrderConfirmedEventHandler : IEventHandler<OrderConfirmedEvent>
{
    private readonly IShipmentStore _shipmentStore;
    private readonly IOutboxStore _outboxStore;

    public OrderConfirmedEventHandler(IShipmentStore shipmentStore, IOutboxStore outboxStore)
    {
        _shipmentStore = shipmentStore;
        _outboxStore = outboxStore;
    }

    public async Task Handle(OrderConfirmedEvent @event)
    {
        await _outboxStore.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            await _shipmentStore.RecordOrderConfirmation(@event.OrderId, @event.CustomerId);

            scope.Complete();
        });
    }
}
