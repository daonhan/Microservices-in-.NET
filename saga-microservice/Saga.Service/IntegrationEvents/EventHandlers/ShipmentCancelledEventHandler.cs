using ECommerce.Shared.Infrastructure.EventBus.Abstractions;

namespace Saga.Service.IntegrationEvents.EventHandlers;

internal sealed class ShipmentCancelledEventHandler : IEventHandler<ShipmentCancelledEvent>
{
    private readonly OrderSagaReplyProcessor _processor;

    public ShipmentCancelledEventHandler(OrderSagaReplyProcessor processor)
    {
        _processor = processor;
    }

    public Task Handle(ShipmentCancelledEvent @event) => _processor.Handle(@event);
}
