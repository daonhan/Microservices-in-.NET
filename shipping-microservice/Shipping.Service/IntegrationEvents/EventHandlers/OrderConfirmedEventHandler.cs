using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using Shipping.Service.Infrastructure.Data;

namespace Shipping.Service.IntegrationEvents.EventHandlers;

internal class OrderConfirmedEventHandler : IEventHandler<OrderConfirmedEvent>
{
    private readonly IShipmentStore _shipmentStore;
    private readonly IOutboxStore _outboxStore;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;

    public OrderConfirmedEventHandler(
        IShipmentStore shipmentStore,
        IOutboxStore outboxStore,
        IOutboxUnitOfWork outboxUnitOfWork)
    {
        _shipmentStore = shipmentStore;
        _outboxStore = outboxStore;
        _outboxUnitOfWork = outboxUnitOfWork;
    }

    public async Task Handle(OrderConfirmedEvent @event)
    {
        await _outboxUnitOfWork.ExecuteAsync(_outboxStore.CreateExecutionStrategy(), async () =>
        {
            await _shipmentStore.RecordOrderConfirmation(@event.OrderId, @event.CustomerId);

            return [];
        });
    }
}
