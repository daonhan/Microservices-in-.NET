using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using Shipping.Service.Infrastructure.Data;

namespace Shipping.Service.IntegrationEvents.EventHandlers;

internal class OrderConfirmedEventHandler : IEventHandler<OrderConfirmedEvent>
{
    private readonly IShipmentStore _shipmentStore;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;

    public OrderConfirmedEventHandler(
        IShipmentStore shipmentStore,
        IOutboxUnitOfWork outboxUnitOfWork)
    {
        _shipmentStore = shipmentStore;
        _outboxUnitOfWork = outboxUnitOfWork;
    }

    public async Task Handle(OrderConfirmedEvent @event)
    {
        await _outboxUnitOfWork.ExecuteAsync(async () =>
        {
            await _shipmentStore.RecordOrderConfirmation(@event.OrderId, @event.CustomerId);

            return [];
        });
    }
}
